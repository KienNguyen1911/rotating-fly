"""
Unified High-Level SDK for programmatic google_flow usage.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

from google_flow.config import AppConfig
from google_flow.logging import get_logger

if TYPE_CHECKING:
    from google_flow.captcha.base import CaptchaProvider
    from google_flow.core.client import FlowClient
    from google_flow.core.generator import ImageGenerator
    from google_flow.core.session import SessionManager
    from google_flow.types import CreditsInfo

logger = get_logger(__name__)


class FlowSDK:
    """High-level Unified SDK for programmatic use of google_flow.

    Example:
        async with FlowSDK(st_token="...") as sdk:
            image_path = await sdk.generate(
                prompt="A futuristic city in antigravity",
                model="imagen-3.0",
                upscale="2k"
            )
            print(f"Generated: {image_path}")
    """

    def __init__(
        self,
        *,
        st_token: str | None = None,
        at_token: str | None = None,
        project_id: str | None = None,
        config_path: str | None = None,
        captcha_provider: CaptchaProvider | None = None,
        db_path: str | None = None,
        labs_base_url: str | None = None,
        api_base_url: str | None = None,
        timeout: int | None = None,
        max_retries: int | None = None,
    ) -> None:
        self.config = AppConfig.load(config_path)

        # Apply programmatic overrides
        if labs_base_url is not None:
            self.config.flow.labs_base_url = labs_base_url
        if api_base_url is not None:
            self.config.flow.api_base_url = api_base_url
        if timeout is not None:
            self.config.flow.timeout = timeout
        if max_retries is not None:
            self.config.flow.max_retries = max_retries

        self.st_token = st_token
        self.at_token = at_token
        self.project_id = project_id
        self.db_path = db_path
        self.captcha_provider = captcha_provider
        self.config_path = config_path

        self._prev_config: AppConfig | None = None
        self._client: FlowClient | None = None
        self._session: SessionManager | None = None
        self._generator: ImageGenerator | None = None
        self._owns_captcha_provider = False

    async def __aenter__(self) -> FlowSDK:
        from google_flow.config import get_config, set_config

        # Backup current config
        try:
            self._prev_config = get_config()
        except Exception:
            self._prev_config = None

        set_config(self.config)

        # Setup custom in-memory SessionManager if credentials passed programmatically
        from pathlib import Path

        from google_flow.core.session import SessionManager
        from google_flow.types import TokenInfo

        if self.st_token or self.at_token or self.project_id:

            class InMemorySessionManager(SessionManager):

                def save(self) -> None:
                    # Do not persist temporary tokens to disk
                    pass

            token_info = TokenInfo(
                st=self.st_token or "",
                at=self.at_token or "",
                project_id=self.project_id or "",
            )
            self._session = InMemorySessionManager(token_info, Path("dummy_token.json"))
        else:
            self._session = self.config.create_session_manager(self.config_path)

        # Initialize FlowClient
        from google_flow.core.client import FlowClient

        self._client = FlowClient(
            labs_base_url=self.config.flow.labs_base_url,
            api_base_url=self.config.flow.api_base_url,
            timeout=self.config.flow.timeout,
        )
        await self._client._ensure_session()

        # Initialize Captcha Provider
        if self.captcha_provider is None:
            from google_flow.captcha.in_process_provider import (
                InProcessCaptchaProvider,
            )

            try:
                self.captcha_provider = InProcessCaptchaProvider(db_path=self.db_path)
                self._owns_captcha_provider = True
            except Exception as e:
                logger.warning(
                    "Could not initialize InProcessCaptchaProvider: %s. "
                    "Falling back to NullCaptchaProvider.",
                    e,
                )
                from google_flow.captcha.base import NullCaptchaProvider

                self.captcha_provider = NullCaptchaProvider()

        # Initialize ImageGenerator
        from google_flow.core.generator import ImageGenerator

        self._generator = ImageGenerator(
            client=self._client,
            session=self._session,
            captcha_provider=self.captcha_provider.get_token,
            max_retries=self.config.flow.max_retries,
        )

        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        if self._client:
            await self._client.close()

        if (
            self.captcha_provider
            and hasattr(self.captcha_provider, "close")
        ):
            await self.captcha_provider.close()

        from google_flow.config import set_config

        if self._prev_config is not None:
            set_config(self._prev_config)

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

        Parameters
        ----------
        prompt:
            Text prompt for generation.
        model:
            Model ID from the registry.
        reference_image:
            Optional reference image bytes or list of bytes for image-to-image.
        output_path:
            Path to save the result. Auto-generated if None.
        upscale:
            Upscale resolution: "none", "2k", or "4k".
        """
        if not self._generator:
            raise RuntimeError("SDK is not initialized. Use as an async context manager.")

        try:
            return await self._generator.generate(
                prompt=prompt,
                model=model,
                reference_image=reference_image,
                output_path=output_path,
                upscale=upscale,
            )
        finally:
            if hasattr(self.captcha_provider, "finish"):
                await self.captcha_provider.finish()

    async def check_credits(self) -> CreditsInfo:
        """Query account credits and return typed result."""
        if not self._generator:
            raise RuntimeError("SDK is not initialized. Use as an async context manager.")
        return await self._generator.check_credits()

    # ── Profiles / Updater Integration ──────────────────────────────────────

    async def list_profiles(self) -> list[dict[str, Any]]:
        """List all configured profiles in the SQLite database."""
        from google_flow.token_updater.config import config as updater_config
        if self.db_path:
            updater_config.db_path = self.db_path

        from google_flow.token_updater.database import ProfileDB

        db = ProfileDB()
        await db.init()
        return await db.get_all_profiles()

    async def select_profile(self, name_or_id: str | int) -> None:
        """Switch the SDK credentials to run under the selected profile."""
        if not self._session:
            raise RuntimeError("SDK is not initialized. Use as an async context manager.")

        import os
        if isinstance(name_or_id, str) and (os.path.isdir(name_or_id) or "/" in name_or_id or "\\" in name_or_id):
            return await self.select_profile_dir(name_or_id)

        from google_flow.token_updater.config import config as updater_config
        if self.db_path:
            updater_config.db_path = self.db_path

        from google_flow.token_updater.browser import BrowserManager
        from google_flow.token_updater.database import ProfileDB

        db = ProfileDB()
        await db.init()

        if isinstance(name_or_id, int):
            profile = await db.get_profile(name_or_id)
        else:
            profile = await db.get_profile_by_name(name_or_id)

        if not profile:
            raise ValueError(f"Profile '{name_or_id}' not found in database.")

        token = profile.get("connection_token_override")
        if not token:
            # Retrieve fresh token using BrowserManager
            manager = BrowserManager()
            await manager.start()
            try:
                token = await manager.extract_token(profile["id"])
            finally:
                await manager.stop()

        if not token:
            raise ValueError(
                f"Could not retrieve session token for profile '{profile['name']}'. "
                "Ensure they are logged in or configured with a connection token override."
            )

        self._session.token.st = token
        self._session.token.at = ""  # Force refresh of access token
        logger.info("Successfully switched SDK to profile: %s", profile["name"])

    async def select_profile_dir(self, profile_dir: str) -> None:
        """Switch the SDK credentials to run under the selected profile directory."""
        if not self._session:
            raise RuntimeError("SDK is not initialized. Use as an async context manager.")

        from google_flow.token_updater.browser import BrowserManager

        manager = BrowserManager()
        await manager.start()
        try:
            token = await manager.extract_token(profile_dir=profile_dir)
        finally:
            await manager.stop()

        if not token:
            raise ValueError(
                f"Could not retrieve session token from profile directory '{profile_dir}'."
            )

        self._session.token.st = token
        self._session.token.at = ""  # Force refresh of access token
        logger.info("Successfully switched SDK to profile directory: %s", profile_dir)

    async def is_profile_dir_logged_in(self, profile_dir: str) -> bool:
        """Check if a profile directory has a valid active login session."""
        from google_flow.token_updater.browser import BrowserManager

        manager = BrowserManager()
        await manager.start()
        try:
            status = await manager.check_login_status(profile_dir=profile_dir)
            return bool(status.get("is_logged_in", False))
        except Exception:
            return False
        finally:
            await manager.stop()
