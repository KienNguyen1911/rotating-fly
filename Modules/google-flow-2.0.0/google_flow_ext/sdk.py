"""
Extended Flow SDK (ExtendedFlowSDK).

Extends google_flow.core.sdk.FlowSDK to construct ExtendedFlowClient and
ExtendedImageGenerator while maintaining backwards compatibility.
"""

from __future__ import annotations

from typing import Any

from google_flow.core.sdk import FlowSDK
from google_flow.logging import get_logger

from google_flow_ext.client import ExtendedFlowClient
from google_flow_ext.generator import ExtendedImageGenerator

logger = get_logger(__name__)


class ExtendedFlowSDK(FlowSDK):
    """SDK Context Manager using ExtendedFlowClient and ExtendedImageGenerator."""

    async def __aenter__(self) -> ExtendedFlowSDK:
        # Save previous global config if any
        from google_flow.config import get_config, set_config

        self._prev_config = get_config()
        set_config(self.config)

        # Initialize SessionManager
        from google_flow.core.session import SessionManager

        self._session = self.config.create_session_manager(self.config_path)

        # Initialize ExtendedFlowClient
        self._client = ExtendedFlowClient(
            labs_base_url=self.config.flow.labs_base_url,
            api_base_url=self.config.flow.api_base_url,
            timeout=self.config.flow.timeout,
        )
        await self._client.__aenter__()

        # Initialize CaptchaProvider (mirror parent FlowSDK behaviour: if no
        # provider was injected, try the in-process one and fall back to Null).
        if self.captcha_provider is None:
            try:
                from google_flow.captcha.in_process_provider import InProcessCaptchaProvider
                self.captcha_provider = InProcessCaptchaProvider(db_path=self.db_path)
                self._owns_captcha_provider = True
            except Exception as exc:
                logger.warning("Could not initialize InProcessCaptchaProvider: %s", exc)
                from google_flow.captcha.base import NullCaptchaProvider
                self.captcha_provider = NullCaptchaProvider()

        # Initialize ExtendedImageGenerator
        captcha_cb = self.captcha_provider.get_token if self.captcha_provider else None
        self._generator = ExtendedImageGenerator(
            client=self._client,
            session=self._session,
            captcha_provider=captcha_cb,
            max_retries=self.config.flow.max_retries,
        )

        return self

    @property
    def generator(self) -> ExtendedImageGenerator:
        """Return the initialized ExtendedImageGenerator instance."""
        if self._generator is None:
            raise RuntimeError("SDK is not initialized. Use 'async with ExtendedFlowSDK():'")
        return self._generator
