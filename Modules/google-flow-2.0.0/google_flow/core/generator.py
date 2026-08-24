"""
High-level image generation orchestrator.

Composes :class:`~google_flow.core.client.FlowClient`,
:class:`~google_flow.core.session.SessionManager`, and the retry
module to provide a simple ``generate()`` method that handles
auth refresh, captcha, retry, upscaling, and file saving.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

from google_flow.constants import DEFAULT_MODEL_ID
from google_flow.core.retry import RetryPolicy, execute_with_retry
from google_flow.exceptions import (
    FlowCaptchaError,
    FlowGenerationError,
    FlowRateLimitError,
    FlowServerError,
    FlowSessionRefreshNeededError,
    FlowTokenExpiredError,
    FlowUpscaleError,
)
from google_flow.logging import get_logger
from google_flow.types import CreditsInfo, UpscaleResolution
from google_flow.utils.image import (
    download_image,
    generate_output_path,
    save_base64_image,
)

if TYPE_CHECKING:
    from google_flow.core.client import FlowClient
    from google_flow.core.session import SessionManager

logger = get_logger(__name__)


class ImageGenerator:
    """High-level API for generating, upscaling, and saving images.

    Parameters
    ----------
    client:
        Pre-configured :class:`FlowClient`.
    session:
        :class:`SessionManager` for auth tokens.
    captcha_provider:
        Optional async callable ``(project_id, action) → token``.
    max_retries:
        Max retry attempts for generation / upscale.
    """

    def __init__(
        self,
        *,
        client: FlowClient,
        session: SessionManager,
        captcha_provider: Any | None = None,
        max_retries: int = 3,
    ) -> None:
        self.client = client
        self.session = session
        self._captcha_provider = captcha_provider
        self._retry_policy = RetryPolicy(
            max_retries=max_retries,
            retryable_exceptions=(
                FlowTokenExpiredError,
                FlowRateLimitError,
                FlowServerError,
                FlowCaptchaError,
            ),
        )

    # ── Auth Helpers ────────────────────────────────────────────────

    async def ensure_access_token(self) -> str:
        """Return a valid AT, refreshing if needed or if the saved AT is expired."""
        st = self.session.require_session_token()

        if self.session.access_token and not self._at_is_expired():
            return self.session.access_token

        logger.info("Obtaining/Refreshing Access Token …")
        data = await self.client.st_to_at(st)
        at = self.session.update_from_session_response(data)
        logger.info("Access Token obtained and saved")
        return at

    def _at_is_expired(self) -> bool:
        """Return True if the persisted AT is past its `at_expires` ISO-8601 instant."""
        expires = self.session.token.at_expires
        if not expires:
            return False  # unknown expiry → trust the cached token
        try:
            from datetime import datetime, timezone
            exp = datetime.fromisoformat(expires.replace("Z", "+00:00"))
            return datetime.now(timezone.utc) >= exp
        except (TypeError, ValueError):
            return False

    async def ensure_project(self) -> str:
        """Return the project ID, creating one if needed."""
        if self.session.token.has_project:
            return self.session.token.project_id

        logger.info("Creating project …")
        st = self.session.require_session_token()
        project_id = await self.client.create_project(st)
        self.session.update_project(project_id)
        logger.info("Project created: %s…", project_id[:16])
        return project_id

    # ── Captcha ─────────────────────────────────────────────────────

    async def _get_captcha_token(self, project_id: str) -> str | None:
        if self._captcha_provider is None:
            return None
        return await self._captcha_provider(project_id, "IMAGE_GENERATION")

    # ── Generate ────────────────────────────────────────────────────

    async def generate(
        self,
        prompt: str,
        *,
        model: str | None = None,
        reference_image: bytes | list[bytes] | None = None,
        output_path: str | None = None,
        upscale: str = "none",
    ) -> str:
        """Generate an image and return the saved path or URL.

        This is the primary high-level entry point.

        Parameters
        ----------
        prompt:
            Text prompt for generation.
        model:
            Model ID from the registry.
        reference_image:
            Optional reference image bytes or list of bytes for image-to-image.
        output_path:
            Path to save the result.  Auto-generated if *None*.
        upscale:
            Upscale resolution: ``"none"``, ``"2k"``, or ``"4k"``.

        Returns
        -------
        str
            The file path (if saved) or FIFE URL.
        """
        from google_flow.models.registry import get_model_config

        model_id = model or DEFAULT_MODEL_ID
        model_cfg = get_model_config(model_id)

        logger.info("Starting image generation")
        logger.info("  Model: %s", model_id)
        logger.info("  Prompt: %s", prompt[:100] + ("…" if len(prompt) > 100 else ""))

        at = await self.ensure_access_token()
        project_id = await self.ensure_project()

        # Upload reference image(s)
        image_inputs: list[dict[str, Any]] = []
        if reference_image:
            # Normalize to list of bytes
            ref_images = reference_image if isinstance(reference_image, list) else [reference_image]
            for idx, img_bytes in enumerate(ref_images, start=1):
                logger.info("  Uploading reference image %d …", idx)
                media_id = await self.client.upload_image(
                    at=at,
                    image_bytes=img_bytes,
                    aspect_ratio=model_cfg.aspect_ratio.value,
                    project_id=project_id,
                )
                image_inputs.append({
                    "name": media_id,
                    "imageInputType": "IMAGE_INPUT_TYPE_REFERENCE",
                })
                logger.info("  Reference image %d uploaded", idx)

        # Generate with retry
        logger.info("  Generating image …")
        result, session_id = await self._generate_with_retry(
            at=at,
            project_id=project_id,
            prompt=prompt,
            model_name=model_cfg.model_name,
            aspect_ratio=model_cfg.aspect_ratio.value,
            image_inputs=image_inputs or None,
        )

        media_list = result.get("media", [])
        if not media_list:
            raise FlowGenerationError("Generation returned empty results")

        image_url = media_list[0]["image"]["generatedImage"]["fifeUrl"]
        gen_media_id = media_list[0].get("name")
        logger.info("  Image generated successfully")

        # Upscale if requested
        upscale_res = UpscaleResolution.from_string(upscale)
        if upscale_res.api_value:
            if not output_path:
                output_path = str(generate_output_path(suffix=upscale_res.value))

            if not gen_media_id:
                logger.warning("  No media ID for upscale, saving original …")
                saved = await download_image(image_url, output_path)
                return str(saved)

            logger.info("  Upscaling to %s …", upscale_res.value.upper())
            try:
                encoded = await self._upscale_with_retry(
                    at=at,
                    project_id=project_id,
                    media_id=gen_media_id,
                    target_resolution=upscale_res.api_value,
                    session_id=session_id,
                )
                saved = save_base64_image(encoded, output_path)
                logger.info("  Upscaled image saved: %s", saved)
                return str(saved)
            except Exception as upscale_err:
                logger.warning("  Upscale failed: %s — saving original", upscale_err)
                try:
                    saved = await download_image(image_url, output_path)
                    return str(saved)
                except Exception as save_err:
                    raise FlowUpscaleError(
                        "Upscale failed and fallback save also failed",
                        detail=f"upscale={upscale_err}; save={save_err}",
                    ) from upscale_err

        # Just save the original
        if output_path:
            saved = await download_image(image_url, output_path)
            logger.info("  Saved: %s", saved)
            return str(saved)

        return image_url

    async def _generate_with_retry(
        self,
        at: str,
        project_id: str,
        prompt: str,
        model_name: str,
        aspect_ratio: str,
        image_inputs: list[dict[str, Any]] | None,
    ) -> tuple[dict[str, Any], str]:
        """Generate with retry, including AT refresh on auth errors."""

        current_at = at

        async def _attempt() -> tuple[dict[str, Any], str]:
            nonlocal current_at
            captcha = await self._get_captcha_token(project_id)
            return await self.client.generate_image(
                at=current_at,
                project_id=project_id,
                prompt=prompt,
                model_name=model_name,
                aspect_ratio=aspect_ratio,
                image_inputs=image_inputs,
                recaptcha_token=captcha,
            )

        async def _on_retry(attempt: int, exc: BaseException, delay: float) -> None:
            nonlocal current_at
            if isinstance(exc, FlowSessionRefreshNeededError):
                # ST can no longer mint a working AT — propagate so caller
                # can prompt the user to re-login instead of looping.
                raise exc
            if isinstance(exc, FlowTokenExpiredError):
                logger.warning("  Access Token expired, refreshing …")
                current_at = await self.refresh_access_token()

        return await execute_with_retry(
            _attempt, policy=self._retry_policy, on_retry=_on_retry
        )

    async def _upscale_with_retry(
        self,
        at: str,
        project_id: str,
        media_id: str,
        target_resolution: str,
        session_id: str | None,
    ) -> str:
        """Upscale with retry, including AT refresh on auth errors."""

        current_at = at

        async def _attempt() -> str:
            nonlocal current_at
            captcha = await self._get_captcha_token(project_id)
            return await self.client.upsample_image(
                at=current_at,
                project_id=project_id,
                media_id=media_id,
                target_resolution=target_resolution,
                session_id=session_id,
                user_paygate_tier=self.session.token.user_paygate_tier,
                recaptcha_token=captcha,
            )

        async def _on_retry(attempt: int, exc: BaseException, delay: float) -> None:
            nonlocal current_at
            if isinstance(exc, FlowSessionRefreshNeededError):
                raise exc
            if isinstance(exc, FlowTokenExpiredError):
                logger.warning("  Access Token expired, refreshing …")
                current_at = await self.refresh_access_token()

        return await execute_with_retry(
            _attempt, policy=self._retry_policy, on_retry=_on_retry
        )

    # ── Token Refresh ────────────────────────────────────────────

    async def refresh_access_token(self) -> str:
        """Force-refresh the AT.

        Raises
        ------
        FlowSessionRefreshNeededError
            If Labs reports the ST can no longer mint a working AT. The
            caller should propagate this so the user is prompted to
            re-login (retrying is futile).
        """
        st = self.session.require_session_token()
        logger.info("Refreshing Access Token …")
        data = await self.client.st_to_at(st)
        at = self.session.update_from_session_response(data)
        logger.info("Access Token refreshed")
        return at

    # ── Credits ─────────────────────────────────────────────────────

    async def check_credits(self) -> CreditsInfo:
        """Query account credits and return typed result."""
        at = await self.ensure_access_token()
        data = await self.client.get_credits(at)
        return CreditsInfo(
            credits=data.get("credits", 0),
            tier=data.get("userPaygateTier", "PAYGATE_TIER_NOT_PAID"),
        )
