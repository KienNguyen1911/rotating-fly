"""
Playwright-based reCAPTCHA token provider.

Launches a headless (or headed) Chromium instance, navigates to the
Flow project page, waits for ``grecaptcha.enterprise`` to load, and
executes the reCAPTCHA challenge to obtain a token.
"""

from __future__ import annotations

import asyncio
import time
from typing import Any

from google_flow.captcha.base import CaptchaProvider
from google_flow.constants import RECAPTCHA_SITE_KEY
from google_flow.exceptions import FlowCaptchaError
from google_flow.logging import get_logger

logger = get_logger(__name__)

try:
    from playwright.async_api import async_playwright

    HAS_PLAYWRIGHT = True
except Exception:
    HAS_PLAYWRIGHT = False
    async_playwright = None  # type: ignore[assignment]


class PlaywrightCaptchaProvider(CaptchaProvider):
    """Acquire reCAPTCHA tokens via a local Playwright browser.

    Parameters
    ----------
    st_token:
        Session token to inject as a cookie.
    headless:
        Run the browser without a visible window.
    timeout_seconds:
        Page-load and script timeout.
    settle_seconds:
        Seconds to wait after page load before executing reCAPTCHA.
    """

    def __init__(
            self,
            st_token: str | None = None,
            *,
            headless: bool = False,
            timeout_seconds: int = 90,
            settle_seconds: float = 2.0,
    ) -> None:
        if not HAS_PLAYWRIGHT:
            raise FlowCaptchaError(
                "Playwright is not installed.  "
                "Run: pip install playwright && python -m playwright install chromium"
            )
        self.st_token = st_token
        self.headless = headless
        self.timeout_seconds = timeout_seconds
        self.settle_seconds = settle_seconds
        self._lock: asyncio.Lock | None = None
        self._pw_context: Any = None
        self._pw: Any = None
        self._browser: Any = None
        self._context: Any = None
        self._page: Any = None
        self._last_loaded_time: float = 0.0

    async def _ensure_browser(self) -> None:
        """Verify browser is started, connected, and context is active. Rebuild if broken."""
        is_broken = False

        if self._browser is None or self._pw is None or self._context is None or self._page is None:
            is_broken = True
        elif not self._browser.is_connected:
            logger.info("Playwright browser connection is lost (possibly closed by user). Rebuilding...")
            is_broken = True
        elif self._page.is_closed():
            logger.info("Playwright page was closed. Rebuilding...")
            is_broken = True
        else:
            try:
                # Access pages to test if the context is still alive and responsive
                _ = self._context.pages
            except Exception:
                logger.info("Playwright browser context is invalid. Rebuilding...")
                is_broken = True

        if is_broken:
            await self.close()
            await self.start()

    async def start(self) -> None:
        """Launch the persistent Playwright browser instance."""
        if self._browser is not None:
            return
        logger.info("Starting persistent Playwright browser instance...")
        from playwright.async_api import async_playwright, Browser
        self._pw_context = async_playwright()
        self._pw = await self._pw_context.start()
        self._browser: Browser = await self._pw.chromium.launch(
            headless=self.headless,
            channel="chrome",
            args=[
                "--disable-blink-features=AutomationControlled",
                "--no-default-browser-check",
                "--disable-dev-shm-usage",
                '--window-position=-32000,-32000'
            ],
        )
        self._context = await self._browser.new_context(
            viewport={"width": 1440, "height": 900}
        )
        if self.st_token:
            await self._context.add_cookies([{
                "name": "__Secure-next-auth.session-token",
                "value": self.st_token,
                "domain": "labs.google",
                "path": "/",
                "httpOnly": True,
                "secure": True,
                "sameSite": "Lax",
            }])
        self._page = self._context.pages[0] if self._context.pages else await self._context.new_page()
        await self._open_url()

    async def close(self) -> None:
        """Close the persistent Playwright browser and stop driver."""
        self._page = None
        self._context = None
        self._last_loaded_time = 0.0
        if self._browser is not None:
            logger.info("Closing persistent Playwright browser...")
            try:
                await self._browser.close()
            except Exception as e:
                logger.warning("Error closing browser: %s", e)
            self._browser = None

        if self._pw is not None:
            try:
                await self._pw.stop()
            except Exception as e:
                logger.warning("Error stopping Playwright: %s", e)
            self._pw = None
            self._pw_context = None

    async def __aenter__(self) -> PlaywrightCaptchaProvider:
        await self.start()
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        await self.close()

    async def _open_url(self):
        page = self._page
        logger.info("Loading Google Flow project page to initialize reCAPTCHA...")
        await page.goto(
            "https://labs.google/fx/tools/flow/project/",
            wait_until="domcontentloaded",
            timeout=self.timeout_seconds * 1000,
        )
        await page.wait_for_timeout(
            int(max(0.0, self.settle_seconds) * 1000)
        )
        self._last_loaded_time = time.time()

    async def get_token(
            self,
            project_id: str = "",
            action: str = "IMAGE_GENERATION",
    ) -> str | None:
        """Acquire reCAPTCHA token using the persistent browser instance."""
        url = f"https://labs.google/fx/tools/flow/project/{project_id}"

        if self._lock is None:
            self._lock = asyncio.Lock()

        logger.info("Waiting for Playwright captcha solver lock (serial request)...")
        async with self._lock:
            logger.info("Acquired Playwright captcha solver lock.")
            await self._ensure_browser()

            assert self._browser is not None
            assert self._context is not None
            assert self._page is not None

            # Reload if last loaded more than 120 seconds ago
            elapsed = time.time() - self._last_loaded_time
            if self._last_loaded_time == 0.0 or elapsed > 120.0:
                logger.info(f"Page last loaded {elapsed:.1f}s ago (or never). Reloading page...")
                await self._open_url()
            else:
                logger.info(f"Reusing already loaded page (elapsed since last load: {elapsed:.1f}s).")

            page = self._page
            # Wait for reCAPTCHA enterprise to be available
            await page.wait_for_function(
                "typeof grecaptcha !== 'undefined' "
                "&& typeof grecaptcha.enterprise !== 'undefined' "
                "&& typeof grecaptcha.enterprise.execute === 'function'",
                timeout=20000,
            )

            # Execute reCAPTCHA
            token = await page.evaluate(
                """
                async ({siteKey, actionName}) => {
                    return await new Promise((resolve, reject) => {
                        try {
                            grecaptcha.enterprise.ready(async () => {
                                try {
                                    const t = await grecaptcha.enterprise.execute(
                                        siteKey, {action: actionName}
                                    );
                                    resolve(t || "");
                                } catch (err) {
                                    reject(err?.message || String(err));
                                }
                            });
                        } catch (err) {
                            reject(err?.message || String(err));
                        }
                    });
                }
                """,
                {"siteKey": RECAPTCHA_SITE_KEY, "actionName": action},
            )

            if not token:
                raise FlowCaptchaError(
                    "reCAPTCHA executed but returned an empty token"
                )

            logger.debug("reCAPTCHA token acquired (%d chars)", len(token))
            return token

            # finally:
            #     await context.close()
