"""
Extended Flow Client (ExtendedFlowClient).

Extends google_flow.core.client.FlowClient with extra features:
- Dynamic project creation (createProject) on Google Flow website
- Project-isolated SHA256 media upload caching and cache invalidation
"""

from __future__ import annotations

import base64
import hashlib
import json

from typing import Any

from google_flow.core.client import FlowClient, _classify_error
from google_flow.constants import CLIENT_TOOL_NAME
from google_flow.exceptions import FlowUploadError
from google_flow.logging import get_logger

logger = get_logger(__name__)


class ExtendedFlowClient(FlowClient):
    """Extended HTTP client inheriting directly from FlowClient."""

    def __init__(self, *args: Any, **kwargs: Any) -> None:
        super().__init__(*args, **kwargs)
        # Store media cache: (project_id, img_hash) -> media_id
        self._media_cache: dict[tuple[str | None, str], str] = {}

    async def create_project(
        self, st: str, title: str = "Flow CLI Project"
    ) -> dict[str, Any]:
        """Create a new project on Google Flow website via tRPC API.

        The Google Labs ``project.createProject`` endpoint requires the
        Session Token (ST) via the ``__Secure-next-auth.session-token`` cookie,
        not the Access Token (AT).  Sending ``at_token`` here returns HTTP 401.

        Also, the payload schema is strict: ``projectTitle`` (not ``title``)
        and ``toolName`` must be one of the literal strings accepted by the
        upstream zod validator (e.g. ``"PINHOLE"``).  Sending the wrong key
        or an unknown literal returns HTTP 400.
        """
        # labs_base_url already ends with "/api" (see google_flow.constants.LABS_BASE_URL),
        # so we only append the trpc path here -- no extra "/api" prefix.
        url = f"{self.labs_base_url}/trpc/project.createProject"
        payload = {"json": {"projectTitle": title, "toolName": CLIENT_TOOL_NAME}}

        logger.debug("Creating new project with title='%s'", title)
        data = await self._request("POST", url, json_data=payload, st_token=st)

        try:
            # Response shape: result.data.json.result.projectId
            result = data["result"]["data"]["json"]["result"]
            project_id = result["projectId"]
            logger.info("Created project '%s' (ID: %s)", title, project_id)
            return {
                "project_id": project_id,
                "title": result.get("projectInfo", {}).get("projectTitle", title),
                "project_url": f"https://labs.google/fx/tools/flow/project/{project_id}",
            }
        except (KeyError, TypeError) as err:
            raise FlowUploadError(
                "Unexpected response when creating project",
                detail=json.dumps(data),
            ) from err

    def invalidate_media_cache(
        self, project_id: str | None, img_hash: str
    ) -> None:
        """Evict a specific image hash from the upload cache for a project."""
        cache_key = (project_id, img_hash)
        if cache_key in self._media_cache:
            del self._media_cache[cache_key]
            logger.debug("Evicted media cache key: %s", cache_key)

    async def upload_media_ext(
        self,
        at: str,
        image_bytes: bytes,
        mime_type: str = "image/png",
        aspect_ratio: str = "IMAGE_ASPECT_RATIO_SQUARE",
        project_id: str | None = None,
        force_reupload: bool = False,
    ) -> str:
        """Upload an image with project-isolated SHA256 cache & force reupload support."""
        img_hash = hashlib.sha256(image_bytes).hexdigest()
        cache_key = (project_id, img_hash)

        if not force_reupload and cache_key in self._media_cache:
            cached_media_id = self._media_cache[cache_key]
            logger.debug("Using cached media ID %s for project %s", cached_media_id, project_id)
            return cached_media_id

        # Use base upload_image (the base client method that actually exists).
        media_id = await self.upload_image(
            at=at,
            image_bytes=image_bytes,
            aspect_ratio=aspect_ratio,
        )

        self._media_cache[cache_key] = media_id
        return media_id
