"""
In-process reCAPTCHA solver using CaptchaRuntime.
"""

from __future__ import annotations

import asyncio

from google_flow.captcha.base import CaptchaProvider
from google_flow.captcha_service.core.config import config as fcs_config
from google_flow.captcha_service.core.database import Database
from google_flow.captcha_service.services.captcha_runtime import CaptchaRuntime
from google_flow.exceptions import FlowCaptchaError
from google_flow.logging import get_logger

logger = get_logger(__name__)


class InProcessCaptchaProvider(CaptchaProvider):
    """In-process reCAPTCHA solver using FCS Playwright slots.

    Avoids the need for a separate HTTP bridge / service.
    """

    def __init__(self, db_path: str | None = None) -> None:
        self.db_path = db_path
        self._db: Database | None = None
        self._runtime: CaptchaRuntime | None = None
        self._lock = asyncio.Lock()
        self.last_session_id: str | None = None

    async def _ensure_initialized(self) -> None:
        if self._runtime is not None:
            return

        async with self._lock:
            if self._runtime is not None:
                return

            if self.db_path:
                fcs_config.update_config_sections({"storage": {"db_path": self.db_path}})

            self._db = Database()
            await self._db.init_db()
            await self._db.initialize_log_store()
            self._runtime = CaptchaRuntime(self._db)
            await self._runtime.start()

    async def get_token(
        self,
        project_id: str,
        action: str = "IMAGE_GENERATION",
    ) -> str | None:
        await self._ensure_initialized()
        assert self._runtime is not None

        try:
            res = await self._runtime.solve(
                project_id=project_id,
                action=action,
                token_id=None,
                api_key_id=0,
            )
            self.last_session_id = res.get("session_id")
            return res.get("token")
        except Exception as e:
            raise FlowCaptchaError(f"In-process captcha solving failed: {e}") from e

    async def finish(self, session_id: str | None = None, status: str = "success") -> None:
        """Mark the solved captcha session as finished."""
        sid = session_id or self.last_session_id
        if not sid or not self._runtime:
            return
        try:
            await self._runtime.finish(sid)
        except Exception as e:
            logger.warning("Failed to finalize captcha session %s: %s", sid, e)

    async def close(self) -> None:
        """Close the underlying runtime and database."""
        async with self._lock:
            if self._runtime:
                await self._runtime.close()
                self._runtime = None
            if self._db:
                await self._db.close()
                self._db = None
