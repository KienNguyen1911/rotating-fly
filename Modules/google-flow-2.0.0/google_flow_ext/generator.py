"""
Extended Image Generator (ExtendedImageGenerator).

Extends google_flow.core.generator.ImageGenerator with:
- Token-refresh retry policies for generation and upscaling
- Dynamic project creation and selection via project_id and project_title
- Advanced reference image caching & re-upload fallback
"""

from __future__ import annotations

import hashlib
from typing import Any

from google_flow.constants import DEFAULT_MODEL_ID
from google_flow.core.generator import ImageGenerator
from google_flow.core.retry import execute_with_retry
from google_flow.exceptions import (
    FlowCaptchaError,
    FlowGenerationError,
    FlowRateLimitError,
    FlowServerError,
    FlowTokenExpiredError,
    FlowUpscaleError,
)
from google_flow.logging import get_logger
from google_flow.models.registry import IMAGE_MODELS, get_model_config
from google_flow.types import UpscaleResolution
from google_flow.utils.image import (
    download_image,
    generate_output_path,
    save_base64_image,
)

from google_flow_ext.client import ExtendedFlowClient

logger = get_logger(__name__)


class ExtendedImageGenerator(ImageGenerator):
    """Extended image generator subclassing base ImageGenerator."""

    def __init__(
        self,
        *,
        client: ExtendedFlowClient,
        session: Any,
        captcha_provider: Any | None = None,
        max_retries: int = 3,
    ) -> None:
        super().__init__(
            client=client,
            session=session,
            captcha_provider=captcha_provider,
            max_retries=max_retries,
        )
        self.client: ExtendedFlowClient = client
        self.last_media_id: str | None = None
        self.last_project_id: str | None = None

    async def create_new_project(self, title: str = "Flow CLI Project") -> str:
        """Create a new project on Google Flow website and store its ID.

        Uses the Session Token (ST) -- ``project.createProject`` on Google
        Labs requires the ST cookie, not the Access Token (AT).
        """
        st = self.session.require_session_token()
        proj_info = await self.client.create_project(st, title=title)
        project_id = proj_info["project_id"]
        self.session.update_project(project_id)
        self.last_project_id = project_id
        return project_id

    async def generate_ext(
        self,
        prompt: str,
        *,
        model: str = DEFAULT_MODEL_ID,
        reference_image: bytes | str | list[bytes | str] | None = None,
        output_path: str | None = None,
        upscale: str = "OFF",
        project_id: str | None = None,
        project_title: str | None = None,
    ) -> str:
        """Extended image generation pipeline with auto-token refresh & reference image retry."""
        at = await self.ensure_access_token()

        if project_title:
            logger.info("Creating requested web project '%s' …", project_title)
            project_id = await self.create_new_project(title=project_title)
        elif not project_id:
            project_id = await self.ensure_project()

        self.last_project_id = project_id

        model_cfg = IMAGE_MODELS.get(model)
        if not model_cfg:
            raise ValueError(f"Unknown model: {model}")

        # Process reference images (single or list)
        image_inputs: list[dict[str, Any]] = []
        if reference_image:
            ref_items = (
                reference_image
                if isinstance(reference_image, list)
                else [reference_image]
            )
            for idx, item in enumerate(ref_items, start=1):
                if isinstance(item, str):
                    media_id = item
                else:
                    media_id = await self.client.upload_media_ext(
                        at=at,
                        image_bytes=item,
                        aspect_ratio=model_cfg.aspect_ratio.value,
                        project_id=project_id,
                    )
                image_inputs.append({
                    "name": media_id,
                    "imageInputType": "IMAGE_INPUT_TYPE_REFERENCE",
                })

        # Generate with retry (with reference image cache fallback)
        logger.info("  Generating image …")
        try:
            result, session_id = await self._generate_with_retry(
                at=at,
                project_id=project_id,
                prompt=prompt,
                model_name=model_cfg.model_name,
                aspect_ratio=model_cfg.aspect_ratio.value,
                image_inputs=image_inputs or None,
            )
        except Exception as exc:
            if reference_image:
                ref_items = (
                    reference_image
                    if isinstance(reference_image, list)
                    else [reference_image]
                )
                if any(isinstance(x, bytes) for x in ref_items):
                    logger.warning(
                        "  Generation failed with reference image (%s). Retrying with fresh upload …",
                        exc,
                    )
                    image_inputs = []
                    for idx, item in enumerate(ref_items, start=1):
                        if isinstance(item, str):
                            media_id = item
                        else:
                            img_hash = hashlib.sha256(item).hexdigest()
                            self.client.invalidate_media_cache(project_id, img_hash)
                            media_id = await self.client.upload_media_ext(
                                at=at,
                                image_bytes=item,
                                aspect_ratio=model_cfg.aspect_ratio.value,
                                project_id=project_id,
                                force_reupload=True,
                            )
                        image_inputs.append({
                            "name": media_id,
                            "imageInputType": "IMAGE_INPUT_TYPE_REFERENCE",
                        })
                    result, session_id = await self._generate_with_retry(
                        at=at,
                        project_id=project_id,
                        prompt=prompt,
                        model_name=model_cfg.model_name,
                        aspect_ratio=model_cfg.aspect_ratio.value,
                        image_inputs=image_inputs or None,
                    )
                else:
                    raise
            else:
                raise

        media_list = result.get("media", [])
        if not media_list:
            raise FlowGenerationError("Generation returned empty results")

        image_url = media_list[0]["image"]["generatedImage"]["fifeUrl"]
        gen_media_id = media_list[0].get("name")
        self.last_media_id = gen_media_id
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

        # Save original if path provided
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
        """Generate with retry policy including AT auto-refresh."""
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
            if isinstance(exc, FlowTokenExpiredError):
                logger.warning("  Access Token expired during generate, refreshing …")
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
        """Upscale with retry policy including AT auto-refresh."""
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
            if isinstance(exc, FlowTokenExpiredError):
                logger.warning("  Access Token expired during upscale, refreshing …")
                current_at = await self.refresh_access_token()

        return await execute_with_retry(
            _attempt, policy=self._retry_policy, on_retry=_on_retry
        )
