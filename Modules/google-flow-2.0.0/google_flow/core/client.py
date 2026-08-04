"""
Low-level Flow API HTTP client.

Handles raw HTTP requests, session pooling, authentication header
injection, and error classification.  Business-level orchestration
(generate, upscale, …) lives in :mod:`google_flow.core.generator`.
"""

from __future__ import annotations

import base64
import hashlib
import json
import random
import time
import uuid
from typing import Any

from google_flow.constants import (
    API_BASE_URL,
    CLIENT_TOOL_NAME,
    LABS_BASE_URL,
)
from google_flow.exceptions import (
    FlowAuthError,
    FlowError,
    FlowNetworkError,
    FlowRateLimitError,
    FlowServerError,
    FlowTokenExpiredError,
    FlowUploadError,
)
from google_flow.logging import get_logger
from google_flow.utils.http import (
    HAS_CURL_CFFI,
    build_headers,
)
from google_flow.utils.image import detect_mime_type

logger = get_logger(__name__)


def _classify_error(exc: Exception) -> FlowError:
    """Map a raw HTTP/network exception to a typed FlowError."""
    text = str(exc).lower()

    if "http 401" in text or "unauthenticated" in text:
        return FlowTokenExpiredError(str(exc))
    if "recaptcha" in text:
        from google_flow.exceptions import FlowCaptchaError
        return FlowCaptchaError(str(exc))
    if "http 403" in text:
        return FlowAuthError(str(exc))
    if "http 429" in text or "too many requests" in text:
        return FlowRateLimitError(str(exc))
    if "http 500" in text or "internal" in text:
        return FlowServerError(str(exc))
    if "timeout" in text:
        from google_flow.exceptions import FlowTimeoutError
        return FlowTimeoutError(str(exc))
    if any(kw in text for kw in ("connect", "dns", "resolve", "network")):
        return FlowNetworkError(str(exc))

    return FlowError(str(exc))


class FlowClient:
    """Low-level async HTTP client for the Flow API.

    Use as an async context manager so that the underlying HTTP
    session is properly closed::

        async with FlowClient() as client:
            data = await client.st_to_at(st_token)

    Parameters
    ----------
    labs_base_url:
        Base URL for the Labs tRPC API.
    api_base_url:
        Base URL for the AI Sandbox REST API.
    timeout:
        Default request timeout in seconds.
    """

    def __init__(
        self,
        *,
        labs_base_url: str = LABS_BASE_URL,
        api_base_url: str = API_BASE_URL,
        timeout: int = 120,
    ) -> None:
        self.labs_base_url = labs_base_url
        self.api_base_url = api_base_url
        self.timeout = timeout

        self._session: Any | None = None
        self._owns_session = False
        self._upload_cache: dict[tuple[str | None, str], str] = {}

    # ── Context Manager ─────────────────────────────────────────────

    async def __aenter__(self) -> FlowClient:
        await self._ensure_session()
        return self

    async def __aexit__(self, *args: Any) -> None:
        await self.close()

    async def _ensure_session(self) -> Any:
        if self._session is not None:
            return self._session

        if HAS_CURL_CFFI:
            from curl_cffi.requests import AsyncSession

            self._session = AsyncSession()
        else:
            import aiohttp

            self._session = aiohttp.ClientSession()

        self._owns_session = True
        return self._session

    async def close(self) -> None:
        """Release the pooled HTTP session."""
        if self._session is not None and self._owns_session:
            if HAS_CURL_CFFI:
                await self._session.close()
            else:
                await self._session.close()
            self._session = None
            self._owns_session = False

    # ── Internal HTTP ───────────────────────────────────────────────

    async def _request(
        self,
        method: str,
        url: str,
        *,
        json_data: dict[str, Any] | None = None,
        st_token: str | None = None,
        at_token: str | None = None,
        timeout: int | None = None,
    ) -> dict[str, Any]:
        """Send an authenticated HTTP request and return parsed JSON."""
        extra_headers: dict[str, str] = {}
        if st_token:
            extra_headers["Cookie"] = (
                f"__Secure-next-auth.session-token={st_token}"
            )
        if at_token:
            extra_headers["authorization"] = f"Bearer {at_token}"

        account_id = (st_token or at_token or "")[:16] or None
        headers = build_headers(account_id=account_id, extra=extra_headers)
        request_timeout = timeout or self.timeout

        logger.debug("%s %s", method, url)
        if json_data:
            logger.debug("Body: %s", json.dumps(json_data, ensure_ascii=False)[:500])

        session = await self._ensure_session()

        try:
            if HAS_CURL_CFFI:
                if method.upper() == "GET":
                    resp = await session.get(
                        url,
                        headers=headers,
                        timeout=request_timeout,
                        impersonate="chrome110",
                    )
                else:
                    resp = await session.post(
                        url,
                        headers=headers,
                        json=json_data,
                        timeout=request_timeout,
                        impersonate="chrome110",
                    )
                if resp.status_code >= 400:
                    body = resp.text[:500]
                    raise _classify_error(Exception(f"HTTP {resp.status_code}: {body}"))
                return resp.json()
            else:
                import aiohttp

                kw: dict[str, Any] = {
                    "headers": headers,
                    "timeout": aiohttp.ClientTimeout(total=request_timeout),
                }
                if method.upper() == "GET":
                    async with session.get(url, **kw) as resp:
                        if resp.status >= 400:
                            body = await resp.text()
                            raise _classify_error(
                                Exception(f"HTTP {resp.status}: {body[:500]}")
                            )
                        return await resp.json()
                else:
                    kw["json"] = json_data
                    async with session.post(url, **kw) as resp:
                        if resp.status >= 400:
                            body = await resp.text()
                            raise _classify_error(
                                Exception(f"HTTP {resp.status}: {body[:500]}")
                            )
                        return await resp.json()

        except FlowError:
            raise
        except Exception as exc:
            raise _classify_error(exc) from exc

    # ── Public Helpers ──────────────────────────────────────────────

    @staticmethod
    def generate_session_id() -> str:
        """Create a session ID for client context."""
        return f";{int(time.time() * 1000)}"

    # ── Auth Endpoints ──────────────────────────────────────────────

    async def st_to_at(self, st: str) -> dict[str, Any]:
        """Exchange a Session Token (ST) for an Access Token (AT)."""
        url = f"{self.labs_base_url}/auth/session"
        return await self._request("GET", url, st_token=st)

    async def create_project(
        self, st: str, title: str = "Flow CLI Project"
    ) -> str:
        """Create a new Flow project and return its ID."""
        url = f"{self.labs_base_url}/trpc/project.createProject"
        data = {"json": {"projectTitle": title, "toolName": CLIENT_TOOL_NAME}}
        result = await self._request("POST", url, json_data=data, st_token=st)
        return result["result"]["data"]["json"]["result"]["projectId"]

    async def get_credits(self, at: str) -> dict[str, Any]:
        """Query account credits."""
        url = f"{self.api_base_url}/credits"
        return await self._request("GET", url, at_token=at)

    # ── Image Upload ────────────────────────────────────────────────

    async def upload_image(
        self,
        at: str,
        image_bytes: bytes,
        aspect_ratio: str = "IMAGE_ASPECT_RATIO_LANDSCAPE",
        project_id: str | None = None,
    ) -> str:
        """Upload a reference image and return its media ID."""
        img_hash = hashlib.sha256(image_bytes).hexdigest()
        cache_key = (project_id, img_hash)
        if cache_key in self._upload_cache:
            cached_media_id = self._upload_cache[cache_key]
            logger.info(
                "Using cached media ID %s for image (hash: %s)",
                cached_media_id,
                img_hash[:12],
            )
            return cached_media_id

        if aspect_ratio.startswith("VIDEO_"):
            aspect_ratio = aspect_ratio.replace("VIDEO_", "IMAGE_")

        mime_type = detect_mime_type(image_bytes)
        image_b64 = base64.b64encode(image_bytes).decode("utf-8")
        ext = "png" if "png" in mime_type else "jpg"
        filename = f"google_flow_upload_{int(time.time() * 1000)}.{ext}"

        url = f"{self.api_base_url}/flow/uploadImage"
        ctx: dict[str, Any] = {"tool": CLIENT_TOOL_NAME}
        if project_id:
            ctx["projectId"] = project_id

        data = {
            "clientContext": ctx,
            "fileName": filename,
            "imageBytes": image_b64,
            "isHidden": False,
            "isUserUploaded": True,
            "mimeType": mime_type,
        }
        result = await self._request("POST", url, json_data=data, at_token=at)

        media_id = result.get("media", {}).get("name") or result.get(
            "mediaGenerationId", {}
        ).get("mediaGenerationId")
        if not media_id:
            raise FlowUploadError(
                "Upload response missing media ID",
                detail=f"keys={list(result.keys())}",
            )

        self._upload_cache[cache_key] = media_id
        return media_id

    # ── Generation ──────────────────────────────────────────────────

    async def generate_image(
        self,
        at: str,
        project_id: str,
        prompt: str,
        model_name: str,
        aspect_ratio: str,
        *,
        image_inputs: list[dict[str, Any]] | None = None,
        recaptcha_token: str | None = None,
    ) -> tuple[dict[str, Any], str]:
        """Submit an image generation request (single attempt, no retry).

        Returns ``(api_response, session_id)``.
        """
        url = f"{self.api_base_url}/projects/{project_id}/flowMedia:batchGenerateImages"
        session_id = self.generate_session_id()

        ctx: dict[str, Any] = {
            "sessionId": session_id,
            "projectId": project_id,
            "tool": CLIENT_TOOL_NAME,
        }
        if recaptcha_token:
            ctx["recaptchaContext"] = {
                "token": recaptcha_token,
                "applicationType": "RECAPTCHA_APPLICATION_TYPE_WEB",
            }

        request_data = {
            "clientContext": ctx,
            "seed": random.randint(1, 999999),
            "imageModelName": model_name,
            "imageAspectRatio": aspect_ratio,
            "structuredPrompt": {"parts": [{"text": prompt}]},
            "imageInputs": image_inputs or [],
        }

        json_data = {
            "clientContext": ctx,
            "mediaGenerationContext": {"batchId": str(uuid.uuid4())},
            "useNewMedia": True,
            "requests": [request_data],
        }

        result = await self._request(
            "POST", url, json_data=json_data, at_token=at, timeout=self.timeout
        )
        return result, session_id

    # ── Upscale ─────────────────────────────────────────────────────

    async def upsample_image(
        self,
        at: str,
        project_id: str,
        media_id: str,
        target_resolution: str,
        *,
        session_id: str | None = None,
        user_paygate_tier: str = "PAYGATE_TIER_NOT_PAID",
        recaptcha_token: str | None = None,
    ) -> str:
        """Upscale an image and return the base64-encoded result."""
        url = f"{self.api_base_url}/flow/upsampleImage"
        sid = session_id or self.generate_session_id()

        ctx: dict[str, Any] = {
            "sessionId": sid,
            "projectId": project_id,
            "tool": CLIENT_TOOL_NAME,
            "userPaygateTier": user_paygate_tier,
        }
        if recaptcha_token:
            ctx["recaptchaContext"] = {
                "token": recaptcha_token,
                "applicationType": "RECAPTCHA_APPLICATION_TYPE_WEB",
            }

        data = {
            "mediaId": media_id,
            "targetResolution": target_resolution,
            "clientContext": ctx,
        }
        result = await self._request(
            "POST", url, json_data=data, at_token=at, timeout=max(self.timeout, 300)
        )
        encoded = result.get("encodedImage", "")
        if not encoded:
            from google_flow.exceptions import FlowUpscaleError

            raise FlowUpscaleError(
                "Upscale response missing encodedImage",
                detail=f"keys={list(result.keys())}",
            )
        return encoded
