"""Browser Management v3.1 - Persistence Context + VNC Login + Headless Refresh"""
import asyncio
import contextlib
import json
import os
import re
import shutil
import subprocess
from datetime import datetime
from typing import Any

from playwright.async_api import BrowserContext, Playwright, async_playwright

from .config import config
from .database import profile_db
from .logger import logger
from google_flow.utils.proxy import format_proxy_for_playwright, parse_proxy

try:
    import pyautogui
    import pygetwindow as pygetwindow
    import win32con
    import win32gui

    pyautogui.FAILSAFE = False
    DESKTOP_AUTOMATION_AVAILABLE = True
except Exception:
    pyautogui = None
    pygetwindow = None
    win32con = None
    win32gui = None
    DESKTOP_AUTOMATION_AVAILABLE = False


# Memory optimization parameters
BROWSER_ARGS = [
    "--no-sandbox",
    "--disable-setuid-sandbox",
    "--disable-dev-shm-usage",
    "--disable-gpu",
    "--disable-software-rasterizer",
    "--disable-extensions",
    "--disable-background-networking",
    "--disable-sync",
    "--disable-translate",
    "--disable-features=TranslateUI",
    "--disable-blink-features=AutomationControlled",
    "--no-first-run",
    "--no-default-browser-check",
    "--single-process",  # Single process mode, saving memory
    "--max_old_space_size=128",  # Limit V8 memory
    "--js-flags=--max-old-space-size=128",
]

LOGIN_BROWSER_ARGS = BROWSER_ARGS[:6] + ["--disable-blink-features=AutomationControlled"]

BLOCKED_RESOURCE_TYPES = {"image", "media", "font", "stylesheet"}
EMAIL_PATTERN = re.compile(r"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", re.IGNORECASE)
BUTTON_CANDIDATE_SELECTORS = "button, [role='button'], input[type='submit'], input[type='button'], a[role='button'], cr-button"
ACCOUNT_INPUT_SELECTORS = [
    "#identifierId",
    "input[name='identifier']",
    "input[autocomplete='username']",
    "input[autocomplete='email']",
    "input[type='email']",
    "input[type='tel']",
]
PASSWORD_INPUT_SELECTORS = [
    "input[name='Passwd']",
    "input[autocomplete='current-password']",
    "input[autocomplete='password']",
    "input[type='password']",
]
ACCOUNT_SUBMIT_SELECTORS = [
    "#identifierNext",
    "#identifierNext button",
    "[id='identifierNext'] button",
]
PASSWORD_SUBMIT_SELECTORS = [
    "#passwordNext",
    "#passwordNext button",
    "[id='passwordNext'] button",
]

SUPERVISOR_CONF = "/etc/supervisor/conf.d/supervisord.conf"
VNC_START_ORDER = ("xvfb", "fluxbox", "x11vnc", "novnc")
VNC_STOP_ORDER = ("novnc", "x11vnc", "fluxbox", "xvfb")


class BrowserManager:
    """Browser Manager - Persistence Context"""

    def __init__(self):
        self._playwright: Playwright | None = None
        self._active_context: BrowserContext | None = None
        self._active_profile_id: int | None = None
        self._active_profile_dir: str | None = None
        self._lock = asyncio.Lock()

    async def start(self):
        """Start Playwright"""
        if self._playwright:
            return
        logger.info("Launch Playwright...")
        self._playwright = await async_playwright().start()
        os.makedirs(config.profiles_dir, exist_ok=True)
        logger.info("Playwright has started")

    async def stop(self):
        """stop"""
        await self._close_active()
        await self._stop_vnc_stack()
        if self._playwright:
            await self._playwright.stop()
            self._playwright = None

    def _supervisorctl(self, *args: str, timeout: float = 15.0) -> subprocess.CompletedProcess[str]:
        exe = shutil.which("supervisorctl")
        if not exe:
            raise RuntimeError("supervisorctl not found")
        cmd = [exe, "-c", SUPERVISOR_CONF, *args]
        return subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, check=False)

    def _get_supervisor_status(self) -> dict[str, str]:
        try:
            cp = self._supervisorctl("status", timeout=8.0)
        except Exception:
            return {}

        status: dict[str, str] = {}
        for line in (cp.stdout or "").splitlines():
            parts = line.split()
            if len(parts) >= 2:
                status[parts[0]] = parts[1]
        return status

    async def _ensure_vnc_stack(self) -> bool:
        if not config.enable_vnc:
            return False

        status = self._get_supervisor_status()
        for prog in VNC_START_ORDER:
            if status.get(prog) == "RUNNING":
                continue
            try:
                cp = self._supervisorctl("start", prog, timeout=20.0)
                if cp.returncode != 0:
                    logger.warning(f"Failed to start {prog}: {(cp.stdout or '').strip()} {(cp.stderr or '').strip()}")
                    return False
            except Exception as e:
                logger.warning(f"Exception starting {prog}: {e}")
                return False

            if prog == "xvfb":
                await asyncio.sleep(0.4)

        return True

    async def _stop_vnc_stack(self) -> None:
        if not config.enable_vnc:
            return

        for prog in VNC_STOP_ORDER:
            with contextlib.suppress(Exception):
                self._supervisorctl("stop", prog, timeout=10.0)

    async def _close_active(self):
        """Close current browser"""
        if self._active_context:
            with contextlib.suppress(Exception):
                await self._active_context.close()
            self._active_context = None
            self._active_profile_id = None
            self._active_profile_dir = None
            logger.info("Browser is closed")

    def _get_profile_dir(self, profile_id: int) -> str:
        """Get Profile persistence directory"""
        return os.path.join(os.path.abspath(config.profiles_dir), f"profile_{profile_id}")

    def _clean_locks(self, profile_dir: str):
        """Clean Chromium lock files"""
        lock_files = ["SingletonLock", "SingletonCookie", "SingletonSocket"]
        for lock in lock_files:
            lock_path = os.path.join(profile_dir, lock)
            if os.path.exists(lock_path):
                try:
                    os.remove(lock_path)
                    logger.info(f"Cleaned lock file: {lock}")
                except Exception:
                    pass

    def _mask_token(self, token: str) -> str:
        if not token or len(token) <= 8:
            return token or ""
        return f"{token[:4]}...{token[-4:]}"

    def _normalize_email(self, value: str) -> str:
        return str(value or "").strip().lower()

    def _extract_email_from_text(self, text: str) -> str | None:
        content = str(text or "")
        for match in EMAIL_PATTERN.findall(content):
            normalized = self._normalize_email(match)
            if normalized:
                return normalized
        return None

    def _resolve_known_email(self, profile: dict[str, Any], body_text: str = "") -> str | None:
        stored_email = self._normalize_email(str(profile.get("email") or ""))
        if stored_email:
            return stored_email

        page_email = self._extract_email_from_text(body_text)
        if page_email:
            return page_email

        login_account = self._normalize_email(str(profile.get("login_account") or ""))
        if login_account and EMAIL_PATTERN.fullmatch(login_account):
            return login_account

        return None

    async def _get_proxy(self, profile: dict[str, Any]) -> dict | None:
        """Get proxy configuration"""
        if profile.get("proxy_enabled") and profile.get("proxy_url"):
            proxy_config = parse_proxy(profile["proxy_url"])
            if proxy_config:
                proxy = format_proxy_for_playwright(proxy_config)
                logger.info(f"[{profile['name']}] Use proxy: {proxy['server']}")
                return proxy
        return None

    async def _safe_page_text(self, page) -> str:
        try:
            body = page.locator("body").first
            if await body.count() <= 0:
                return ""
            return str(await body.inner_text(timeout=2000) or "")
        except Exception:
            return ""

    def _text_contains_any(self, text: str, patterns: list[str]) -> bool:
        lowered = str(text or "").lower()
        if not lowered:
            return False
        return any(str(pattern or "").strip().lower() in lowered for pattern in patterns if str(pattern or "").strip())

    async def _get_locator_search_text(self, locator) -> str:
        parts: list[str] = []

        with contextlib.suppress(Exception):
            parts.append(str(await locator.inner_text(timeout=1000) or ""))

        with contextlib.suppress(Exception):
            parts.append(str(await locator.text_content(timeout=1000) or ""))

        for attr in ("value", "aria-label", "title", "name", "data-identifier", "data-email"):
            with contextlib.suppress(Exception):
                parts.append(str(await locator.get_attribute(attr) or ""))

        return " ".join(part.strip() for part in parts if str(part or "").strip())

    async def _click_first_visible(self, page, selectors: list[str]) -> bool:
        for selector in selectors:
            try:
                locator = page.locator(selector).first
                if await locator.count() <= 0 or not await locator.is_visible():
                    continue
                await locator.click(timeout=5000)
                await asyncio.sleep(1)
                return True
            except Exception:
                continue
        return False

    async def _has_visible_selector(self, page, selectors: list[str]) -> bool:
        for selector in selectors:
            try:
                locator = page.locator(selector).first
                if await locator.count() > 0 and await locator.is_visible():
                    return True
            except Exception:
                continue
        return False

    async def _wait_for_page_progress(
        self,
        page,
        previous_url: str,
        current_selectors: list[str],
        success_selectors: list[str] | None = None,
        attempts: int = 5,
    ) -> bool:
        success_selectors = success_selectors or []
        for _ in range(max(1, attempts)):
            if str(page.url or "") != previous_url:
                return True
            if success_selectors and await self._has_visible_selector(page, success_selectors):
                return True
            if current_selectors and not await self._has_visible_selector(page, current_selectors):
                return True
            await asyncio.sleep(0.4)
        return False

    async def _click_button_by_text(self, page, patterns: list[str]) -> bool:
        escaped = [re.escape(str(pattern or "").strip()) for pattern in patterns if str(pattern or "").strip()]
        if not escaped:
            return False

        regex = re.compile("|".join(escaped), re.IGNORECASE)
        try:
            candidates = page.locator(BUTTON_CANDIDATE_SELECTORS)
            count = min(await candidates.count(), 100)
        except Exception:
            return False

        for index in range(count):
            try:
                locator = candidates.nth(index)
                if not await locator.is_visible():
                    continue
                label = str(await self._get_locator_search_text(locator) or "").strip()
                if not label or not regex.search(label):
                    continue
                await locator.click(timeout=5000)
                await asyncio.sleep(1)
                return True
            except Exception:
                continue
        return False

    async def _click_text_if_visible(self, page, patterns: list[str]) -> bool:
        for pattern in patterns:
            text = str(pattern or "").strip()
            if not text:
                continue
            try:
                candidate_buttons = page.locator(BUTTON_CANDIDATE_SELECTORS)
                count = min(await candidate_buttons.count(), 40)
                for index in range(count):
                    locator = candidate_buttons.nth(index)
                    if not await locator.is_visible():
                        continue
                    label = str(await self._get_locator_search_text(locator) or "").strip()
                    if text.lower() not in label.lower():
                        continue
                    await locator.click(timeout=5000)
                    await asyncio.sleep(1)
                    return True
                locator = page.get_by_text(text, exact=False).first
                if await locator.count() <= 0:
                    continue
                if not await locator.is_visible():
                    continue
                await locator.click(timeout=5000)
                await asyncio.sleep(1)
                return True
            except Exception:
                continue
        return False

    async def _fill_and_submit_first_visible(
        self,
        page,
        selectors: list[str],
        value: str,
        *,
        submit_selectors: list[str] | None = None,
        submit_patterns: list[str] | None = None,
        success_selectors: list[str] | None = None,
    ) -> bool:
        if not str(value or "").strip():
            return False

        submit_selectors = submit_selectors or []
        submit_patterns = submit_patterns or []
        success_selectors = success_selectors or []

        for selector in selectors:
            try:
                locator = page.locator(selector).first
                if await locator.count() <= 0 or not await locator.is_visible():
                    continue
                previous_url = str(page.url or "")
                await locator.click(timeout=5000)
                await locator.fill("", timeout=5000)
                await locator.fill(value, timeout=5000)
                await asyncio.sleep(0.4)

                try:
                    await locator.press("Enter", timeout=3000)
                    if await self._wait_for_page_progress(page, previous_url, selectors, success_selectors):
                        return True
                except Exception:
                    pass

                if submit_selectors and await self._click_first_visible(page, submit_selectors):
                    if await self._wait_for_page_progress(page, previous_url, selectors, success_selectors):
                        return True

                if submit_patterns and await self._click_button_by_text(page, submit_patterns):
                    if await self._wait_for_page_progress(page, previous_url, selectors, success_selectors):
                        return True
            except Exception:
                continue
        return False

    def _detect_login_blocker(self, body_text: str) -> str | None:
        text = str(body_text or "")
        lowered = text.lower()
        if not lowered:
            return None

        blocked_markers = [
            (
                [
                    "wrong password",
                    "Wrong password",
                    "Password is incorrect",
                ],
                "The login password is wrong, please check and try again.",
            ),
            (
                [
                    "couldn’t find your google account",
                    "couldn't find your google account",
                    "Can't find your google account",
                    "Enter a valid email address or phone number",
                ],
                "The login account does not exist or cannot be recognized",
            ),
            (
                [
                    "2-step verification",
                    "verify it’s you",
                    "verify it's you",
                    "check your phone",
                    "Verify your identity",
                    "two-step verification",
                    "Two-step verification",
                ],
                "This account requires manual secondary verification. Please use manual login instead.",
            ),
            (
                [
                    "too many failed attempts",
                    "Too many attempts",
                    "try again later",
                    "try again later",
                ],
                "Too many login attempts, please try again later",
            ),
            (
                [
                    "enter the characters",
                    "Not your computer? Please log in using guest mode",
                    "confirm you’re not a robot",
                    "Confirm you are not a robot",
                ],
                "Additional manual verification is required during the login process, please use manual login instead.",
            ),
        ]

        for markers, message in blocked_markers:
            if any(marker.lower() in lowered for marker in markers):
                return message
        return None

    async def _click_account_choice(self, page, login_account: str) -> bool:
        normalized_account = self._normalize_email(login_account or "")
        if not normalized_account:
            return False

        attr_selectors = [
            f'[data-identifier="{normalized_account}"]',
            f'[data-email="{normalized_account}"]',
        ]
        if await self._click_first_visible(page, attr_selectors):
            return True

        try:
            candidates = page.locator("button, [role='button'], li, div[data-identifier], div[data-email]")
            count = min(await candidates.count(), 80)
        except Exception:
            count = 0

        for index in range(count):
            try:
                locator = candidates.nth(index)
                if not await locator.is_visible():
                    continue
                label = self._normalize_email(await self._get_locator_search_text(locator))
                if normalized_account not in label:
                    continue
                await locator.click(timeout=5000)
                await asyncio.sleep(1)
                return True
            except Exception:
                continue

        return await self._click_text_if_visible(page, [login_account])

    async def _handle_chromium_signin_prompt(self, page, body_text: str) -> bool:
        text = str(body_text or "")
        markers = [
            "Sign in to Chromium",
            "Login to Chromium",
            "Set up a work profile",
            "Set up work profile",
            "Use Chromium without an account",
            "Continue as",
        ]
        if not self._text_contains_any(text, markers):
            return False

        if await self._click_button_by_text(page, ["Continue as", "continue as", "continue in this capacity", "Continue", "続行", "계속", "Continuar"]):
            return True
        return bool(await self._click_button_by_text(page, ["Use Chromium without an account", "Don't use account", "Not using an account", "Not logged in yet", "Talk about it later", "Not now", "アカウントなし", "계정 없이", "Sin cuenta", "Plus tard"]))

    async def _handle_managed_profile_prompt(self, page, body_text: str) -> bool:
        text = str(body_text or "")
        markers = [
            "Continue to work in this profile",
            "This profile will be managed",
            "Your organization manages this profile",
            "Create a work profile",
            "Separate browsing for work",
            "You're signing in with a managed account",
            "Set up your new profile",
            "This profile will be managed",
            "This information will be managed",
        ]
        if not self._text_contains_any(text, markers):
            return False

        return await self._click_button_by_text(
            page,
            [
                "Continue to work in this profile",
                "Continue",
                "continue",
                "I understand",
                "I understand",
                "I understand",
                "Confirm",
                "confirm",
                "Create profile",
                "Create profile",
            ],
        )

    async def _handle_profile_data_choice_prompt(self, page, body_text: str) -> bool:
        text = str(body_text or "")
        markers = [
            "How do you want to handle your existing browsing data",
            "Keep existing browsing data separate",
            "Continue using this profile",
            "Use existing data",
            "Create new profile",
            "What do you want to do with existing profile data",
        ]
        if not self._text_contains_any(text, markers):
            return False

        if await self._click_text_if_visible(
            page,
            [
                "Continue using this profile",
                "Use existing data",
                "Keep existing browsing data separate",
                "Continue to use this information",
                "Continue to use this information",
            ],
        ):
            return True

        return await self._click_button_by_text(
            page,
            ["Continue", "continue", "Confirm", "confirm", "Create new profile", "Create new profile"],
        )

    async def _handle_browser_settings_prompts(self, page, body_text: str) -> bool:
        text = str(body_text or "")

        positive_markers = [
            "Turn on sync",
            "Sync and personalize",
            "Save and continue",
            "Sync your stuff",
            "Save time by syncing",
            "Turn on sync",
            "Sync and personalize",
            "Save and continue",
        ]
        dismiss_markers = [
            "Make Chrome your default browser",
            "Make Chromium your default browser",
            "Help improve Chrome",
            "Help improve Chromium",
            "Import bookmarks and settings",
            "Set as default",
            "Default browser",
            "Import bookmarks",
            "help improve",
        ]

        if self._text_contains_any(text, positive_markers):
            return await self._click_button_by_text(
                page,
                [
                    "Save and continue",
                    "Yes, I'm in",
                    "Turn on sync",
                    "Continue",
                    "continue",
                    "Save and continue",
                    "Turn on sync",
                    "Save して続行",
                    "동기화 켜기",
                    "Guardar y continuar",
                ],
            )

        if self._text_contains_any(text, dismiss_markers):
            return await self._click_button_by_text(
                page,
                ["Not now", "No thanks", "Skip", "Talk about it later", "Not yet", "jump over", "After", "나중에", "Ahora no", "Non merci"],
            )

        return False

    async def _advance_google_login(self, page, login_account: str, login_password: str) -> bool:
        if await self._click_button_by_text(page, ["Use another account", "Use another account", "Use another account", "Don't use it", "다른 계정 사용", "Usar otra cuenta", "Utiliser un autre compte"]):
            return True
        if await self._click_account_choice(page, login_account):
            return True
        if await self._fill_and_submit_first_visible(
            page,
            ACCOUNT_INPUT_SELECTORS,
            login_account,
            submit_selectors=ACCOUNT_SUBMIT_SELECTORS,
            submit_patterns=["Next step", "Next", "continue", "Continue", "times", "다음", "Siguiente", "Suivant", "Weiter", "Avançar"],
            success_selectors=PASSWORD_INPUT_SELECTORS,
        ):
            return True
        return bool(await self._fill_and_submit_first_visible(page, PASSWORD_INPUT_SELECTORS, login_password, submit_selectors=PASSWORD_SUBMIT_SELECTORS, submit_patterns=["Next step", "Next", "continue", "Continue", "Log in", "Sign in", "times", "続行", "로그인", "Iniciar sesión", "Connexion", "Anmelden", "Fazer login", "Войти"], success_selectors=["[href*='labs.google']", "[data-test-id='profile-menu-button']"]))

    async def _install_page_route(self, page) -> None:
        async def _route(route, request):
            try:
                if request.resource_type in BLOCKED_RESOURCE_TYPES:
                    await route.abort()
                else:
                    await route.continue_()
            except Exception:
                with contextlib.suppress(Exception):
                    await route.continue_()

        with contextlib.suppress(Exception):
            await page.route("**/*", _route)

    async def _focus_browser_window_for_native_prompt(self) -> bool:
        if os.name != "nt" or not DESKTOP_AUTOMATION_AVAILABLE or pygetwindow is None:
            return False

        title_keywords = [
            "Flow - Chromium",
            "Chromium",
            "Google Chrome",
        ]
        for keyword in title_keywords:
            try:
                windows = [window for window in pygetwindow.getWindowsWithTitle(keyword) if getattr(window, "title", "")]
            except Exception:
                continue
            if not windows:
                continue

            window = windows[0]
            hwnd = getattr(window, "_hWnd", None)
            with contextlib.suppress(Exception):
                window.restore()
            try:
                window.activate()
            except Exception:
                if hwnd and win32gui is not None and win32con is not None:
                    try:
                        win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
                        win32gui.SetForegroundWindow(hwnd)
                    except Exception:
                        pass
            await asyncio.sleep(0.8)
            return True
        return False

    async def _handle_native_chrome_profile_prompts(self) -> bool:
        if os.name != "nt" or not DESKTOP_AUTOMATION_AVAILABLE or pyautogui is None:
            return False
        if not await self._focus_browser_window_for_native_prompt():
            return False

        acted = False
        sequences = [
            ("enter",),
            ("tab", "enter"),
            ("shift+tab", "enter"),
            ("esc",),
        ]
        for sequence in sequences:
            try:
                for key in sequence:
                    if key == "shift+tab":
                        pyautogui.hotkey("shift", "tab")
                    else:
                        pyautogui.press(key)
                    await asyncio.sleep(0.8)
                acted = True
            except Exception:
                return acted
        return acted

    async def _handle_managed_account_prompts(self, page, body_text: str) -> bool:
        if await self._click_button_by_text(page, ["Sign in with Google"]):
            return True

        if await self._handle_chromium_signin_prompt(page, body_text):
            return True
        if await self._handle_managed_profile_prompt(page, body_text):
            return True
        if await self._handle_profile_data_choice_prompt(page, body_text):
            return True
        if await self._handle_browser_settings_prompts(page, body_text):
            return True

        return bool(await self._click_button_by_text(page, ["Continue to work in this profile", "Continue as", "Save and continue", "I understand", "I understand", "I understand", "confirm", "Confirm", "continue", "Continue", "続行", "계속", "Continuar", "Bestätigen"]))

    async def _handle_labs_onboarding(self, page, body_text: str) -> bool:
        text = str(body_text or "")
        if await page.locator("#marketing-emails").count() > 0 or await page.locator("#research-emails").count() > 0:
            for selector in ("#marketing-emails", "#research-emails"):
                try:
                    checkbox = page.locator(selector).first
                    if await checkbox.count() <= 0 or not await checkbox.is_visible():
                        continue
                    checked = str(await checkbox.get_attribute("aria-checked") or "").strip().lower() == "true"
                    if not checked:
                        await checkbox.click(timeout=5000)
                        await asyncio.sleep(0.3)
                except Exception:
                    continue
            if await self._click_button_by_text(page, ["Next step", "Next", "continue", "Continue", "times", "다음", "Siguiente", "Suivant"]):
                return True

        onboarding_markers = [
            "Experience the creativity of AI tools",
            "Experience the creativity",
            "View our Privacy Policy",
            "privacy policy",
            "Privacy Policy",
            "Your data and labs.google/fx",
            "Welcome to",
            "Get started",
            "Start using",
            "Introducing",
        ]
        if any(marker.lower() in text.lower() for marker in onboarding_markers):
            with contextlib.suppress(Exception):
                await page.evaluate("window.scrollTo(0, document.body.scrollHeight)")
            await asyncio.sleep(0.5)
            if await self._click_button_by_text(
                page,
                ["continue", "Continue", "agree", "Agree", "Next step", "Next", "Got it", "Done", "Skip", "Accept", "Start", "OK", "times", "Agree", "다음", "동의", "Aceptar", "Accepter", "Akzeptieren", "Начать"],
            ):
                return True

        return False

    async def _is_labs_session_ready(self, page, body_text: str) -> bool:
        url = str(page.url or "").lower()
        if "labs.google" not in url:
            return False
        if "accounts.google.com" in url:
            return False

        text = str(body_text or "")
        blocked_markers = [
            "Experience the creativity of AI tools",
            "Experience the creativity",
            "View our Privacy Policy",
            "Privacy Policy",
            "Sign in to Chrome",
            "Sign in to Chromium",
            "Set up a work profile",
            "Use Chromium without an account",
            "Continue to work in this profile",
            "How do you want to handle your existing browsing data",
            "Turn on sync",
            "Save and continue",
            "Sign in with Google",
            "Too many failed attempts",
        ]
        if any(marker.lower() in text.lower() for marker in blocked_markers):
            return False

        try:
            if await page.locator(", ".join(ACCOUNT_INPUT_SELECTORS + PASSWORD_INPUT_SELECTORS)).count() > 0:
                return False
        except Exception:
            return False

        return True

    async def _settle_labs_session(self, profile: dict[str, Any], context: BrowserContext, page) -> str | None:
        native_prompt_attempts = 0
        for _ in range(40):
            with contextlib.suppress(Exception):
                await page.wait_for_load_state("domcontentloaded", timeout=3000)

            body_text = await self._safe_page_text(page)

            if native_prompt_attempts < 3 and not str(body_text or "").strip() and await self._handle_native_chrome_profile_prompts():
                native_prompt_attempts += 1
                logger.info(f"[{profile['name']}] Chromium native profile prompts processed")
                continue

            if await self._handle_managed_account_prompts(page, body_text):
                logger.info(f"[{profile['name']}] processed Google / profile confirmation prompt")
                continue

            if await self._handle_labs_onboarding(page, body_text):
                logger.info(f"[{profile['name']}] processed labs first boot")
                continue

            if await self._is_labs_session_ready(page, body_text):
                logger.info(f"[{profile['name']}] labs session page is ready")
                break

            await asyncio.sleep(1.0)

        token = await self._get_session_cookie(context)
        deadline = asyncio.get_running_loop().time() + 12.0
        while asyncio.get_running_loop().time() < deadline:
            token = await self._get_session_cookie(context)
            if token:
                break
            await asyncio.sleep(0.5)

        if not token:
            with contextlib.suppress(Exception):
                await page.wait_for_load_state("networkidle", timeout=8000)
            token = await self._get_session_cookie(context)

        if token:
            for _ in range(4):
                body_text = await self._safe_page_text(page)
                if await self._handle_managed_account_prompts(page, body_text):
                    continue
                if await self._handle_labs_onboarding(page, body_text):
                    continue
                break
        return token

    async def _persist_login_state(
        self,
        profile_id: int | None,
        token: str | None,
        email: str | None = None,
        is_logged_in: bool | None = None,
    ) -> None:
        if profile_id is None:
            return
        logged_in = bool(token) if is_logged_in is None else bool(is_logged_in)
        update_data: dict[str, Any] = {"is_logged_in": 1 if logged_in else 0}
        if token:
            update_data["last_token"] = self._mask_token(token)
            update_data["last_token_time"] = datetime.now().isoformat()
            update_data["login_method"] = "browser"
        normalized_email = self._normalize_email(email or "")
        if normalized_email:
            update_data["email"] = normalized_email
        await profile_db.update_profile(profile_id, **update_data)

    async def _save_google_cookies_from_context(
        self,
        profile_id: int | None,
        context: BrowserContext,
    ) -> None:
        """Extract Google cookies from the browser context and store them for subsequent protocol refreshes"""
        if profile_id is None:
            return
        try:
            all_google_cookies = []
            for domain in [".google.com", "accounts.google.com"]:
                try:
                    cookies = await context.cookies(f"https://{domain}")
                    all_google_cookies.extend(cookies)
                except Exception:
                    pass

            if not all_google_cookies:
                return

            # Convert to JSON storage
            google_cookies_json = json.dumps(all_google_cookies)
            await profile_db.update_profile(profile_id, google_cookies=google_cookies_json)
            logger.info(f"[Profile {profile_id}] has extracted {len(all_google_cookies)} Google cookies from the browser for protocol refresh")
        except Exception as e:
            logger.warning(f"[Profile {profile_id}] Failed to extract Google cookies: {e}")

    def _parse_cookies_payload(self, cookies_json: str) -> list[dict[str, Any]]:
        data = json.loads(cookies_json)
        if isinstance(data, list):
            return data
        if isinstance(data, dict):
            cookies = data.get("cookies")
            if isinstance(cookies, list):
                return cookies
        return []

    def _to_playwright_cookies(self, cookies: list[dict[str, Any]]) -> list[dict[str, Any]]:
        out: list[dict[str, Any]] = []
        for c in cookies:
            if not isinstance(c, dict):
                continue

            name = c.get("name")
            value = c.get("value")
            if not name or value is None:
                continue

            domain = c.get("domain") or c.get("host")
            url = c.get("url")
            path = c.get("path") or "/"

            if isinstance(domain, str) and "://" in domain:
                domain = None

            cookie: dict[str, Any] = {"name": str(name), "value": str(value)}

            if c.get("httpOnly") is not None:
                cookie["httpOnly"] = bool(c.get("httpOnly"))
            if c.get("secure") is not None:
                cookie["secure"] = bool(c.get("secure"))

            expires = c.get("expires")
            if expires is None:
                expires = c.get("expirationDate") or c.get("expiry")
            if expires is not None:
                with contextlib.suppress(TypeError, ValueError):
                    cookie["expires"] = float(expires)

            same_site = c.get("sameSite")
            if isinstance(same_site, str):
                m = same_site.strip().lower()
                if m in {"lax"}:
                    cookie["sameSite"] = "Lax"
                elif m in {"strict"}:
                    cookie["sameSite"] = "Strict"
                elif m in {"none", "no_restriction"}:
                    cookie["sameSite"] = "None"

            if isinstance(url, str) and url.startswith("http"):
                cookie["url"] = url
            elif isinstance(domain, str) and domain:
                cookie["domain"] = domain
                cookie["path"] = str(path)
            else:
                continue

            out.append(cookie)
        return out

    async def _get_session_cookie(self, context: BrowserContext) -> str | None:
        try:
            cookies = await context.cookies("https://labs.google")
        except Exception:
            cookies = await context.cookies()

        for cookie in cookies:
            if cookie.get("name") == config.session_cookie_name:
                return cookie.get("value")
        return None

    async def import_cookies(
        self,
        profile_id: int | None = None,
        cookies_json: str = "",
        *,
        profile_dir: str | None = None,
    ) -> dict[str, Any]:
        """Import Cookie (JSON) and write it into the persistence profile"""
        if len(cookies_json) > 300_000:
            return {"success": False, "error": "The cookie content is too large (it is recommended to export only the cookies of the labs.google domain name)"}

        async with self._lock:
            if profile_id is not None:
                profile = await profile_db.get_profile(profile_id)
                if not profile:
                    return {"success": False, "error": "Profile does not exist"}
                resolved_dir = self._get_profile_dir(profile_id)
            elif profile_dir is not None:
                resolved_dir = os.path.abspath(profile_dir)
                profile = {
                    "id": None,
                    "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                    "login_account": None,
                    "login_password": None,
                    "proxy_enabled": False,
                    "proxy_url": None,
                    "connection_token_override": None,
                    "is_logged_in": 0,
                }
            else:
                return {"success": False, "error": "Either profile_id or profile_dir must be provided"}

            try:
                raw = self._parse_cookies_payload(cookies_json)
            except Exception as e:
                return {"success": False, "error": f"Cookie JSON parsing failed: {e}"}

            if not raw:
                return {"success": False, "error": "Cookie list not recognized (please paste a JSON array or object containing the cookies field)"}

            cookies = self._to_playwright_cookies(raw)
            if not cookies:
                return {"success": False, "error": "Cookie list is empty or the format is not supported (at least name/value/domain+path or url required)"}

            context = None
            try:
                if not self._playwright:
                    await self.start()

                os.makedirs(resolved_dir, exist_ok=True)
                self._clean_locks(resolved_dir)
                proxy = await self._get_proxy(profile)

                context = await self._playwright.chromium.launch_persistent_context(
                    user_data_dir=resolved_dir,
                    headless=True,
                    channel="chrome",
                    viewport={"width": 1024, "height": 768},
                    locale="en-US",
                    timezone_id="America/New_York",
                    proxy=proxy,
                    args=BROWSER_ARGS,
                    ignore_default_args=["--enable-automation"],
                )

                await context.add_cookies(cookies)

                # After importing, visit the labs page to refresh the session.
                token = await self._extract_from_context(profile, context)

                return {
                    "success": True,
                    "imported": len(cookies),
                    "raw_count": len(raw),
                    "has_token": bool(token),
                }

            except Exception as e:
                logger.error(f"[{profile['name']}] Cookie import failed: {e}")
                return {"success": False, "error": str(e)}
            finally:
                if context:
                    with contextlib.suppress(Exception):
                        await context.close()

    async def export_cookies(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
    ) -> dict[str, Any]:
        """Export labs.google domain name Cookie, the format is compatible with the import interface."""
        async with self._lock:
            if profile_id is not None:
                profile = await profile_db.get_profile(profile_id)
                if not profile:
                    return {"success": False, "error": "Profile does not exist"}
                resolved_dir = self._get_profile_dir(profile_id)
            elif profile_dir is not None:
                resolved_dir = os.path.abspath(profile_dir)
                profile = {
                    "id": None,
                    "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                    "login_account": None,
                    "login_password": None,
                    "proxy_enabled": False,
                    "proxy_url": None,
                    "connection_token_override": None,
                    "is_logged_in": 0,
                }
            else:
                return {"success": False, "error": "Either profile_id or profile_dir must be provided"}

            context = None
            try:
                is_active = False
                if profile_id is not None and self._active_profile_id == profile_id:
                    is_active = True
                elif profile_dir is not None and self._active_profile_dir == resolved_dir:
                    is_active = True

                if is_active and self._active_context:
                    cookies = await self._active_context.cookies("https://labs.google")
                else:
                    if not os.path.exists(resolved_dir):
                        return {"success": False, "error": "No persistent data, please log in or import session data first"}

                    if not self._playwright:
                        await self.start()

                    self._clean_locks(resolved_dir)
                    proxy = await self._get_proxy(profile)
                    context = await self._playwright.chromium.launch_persistent_context(
                        user_data_dir=resolved_dir,
                        headless=True,
                        channel="chrome",
                        viewport={"width": 1024, "height": 768},
                        locale="en-US",
                        timezone_id="America/New_York",
                        proxy=proxy,
                        args=BROWSER_ARGS,
                        ignore_default_args=["--enable-automation"],
                    )
                    cookies = await context.cookies("https://labs.google")

                if not cookies:
                    return {"success": False, "error": "There are no exported cookies for the current account."}

                return {
                    "success": True,
                    "kind": "session",
                    "source": "active_context" if is_active and self._active_context else "browser_profile",
                    "profile_id": profile_id,
                    "profile_name": profile.get("name") or "",
                    "cookies": cookies,
                    "cookie_count": len(cookies),
                    "count": len(cookies),
                    "cookies_json": json.dumps(cookies, ensure_ascii=False, indent=2),
                    "has_token": any(c.get("name") == config.session_cookie_name for c in cookies),
                }

            except Exception as e:
                logger.error(f"[{profile['name']}] Cookie export failed: {e}")
                return {"success": False, "error": str(e)}
            finally:
                if context:
                    with contextlib.suppress(Exception):
                        await context.close()

    async def launch_for_login(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
    ) -> bool:
        """Launch a browser for VNC login (non-headless)"""
        if not config.enable_vnc:
            logger.warning("VNC login disabled (enabled by setting ENABLE_VNC=1)")
            return False
        async with self._lock:
            await self._close_active()

            if profile_id is not None:
                profile = await profile_db.get_profile(profile_id)
                if not profile:
                    logger.error(f"Profile {profile_id} does not exist")
                    return False
                resolved_dir = self._get_profile_dir(profile_id)
            elif profile_dir is not None:
                resolved_dir = os.path.abspath(profile_dir)
                profile = {
                    "id": None,
                    "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                    "login_account": None,
                    "login_password": None,
                    "proxy_enabled": False,
                    "proxy_url": None,
                    "connection_token_override": None,
                    "is_logged_in": 0,
                }
            else:
                logger.error("Either profile_id or profile_dir must be provided")
                return False

            try:
                if not self._playwright:
                    await self.start()

                ok = await self._ensure_vnc_stack()
                if not ok:
                    logger.error(f"[{profile['name']}] VNC service failed to start")
                    return False

                os.makedirs(resolved_dir, exist_ok=True)
                self._clean_locks(resolved_dir)  # Clean up lock files
                proxy = await self._get_proxy(profile)

                # Non-headless, for VNC login
                self._active_context = await self._playwright.chromium.launch_persistent_context(
                    user_data_dir=resolved_dir,
                    headless=False,  # VNC visible
                    viewport={"width": 1024, "height": 768},
                    locale="en-US",
                    channel="chrome",
                    timezone_id="America/New_York",
                    proxy=proxy,
                    args=LOGIN_BROWSER_ARGS,
                    ignore_default_args=["--enable-automation"],
                )
                self._active_profile_id = profile_id
                self._active_profile_dir = resolved_dir

                page = self._active_context.pages[0] if self._active_context.pages else await self._active_context.new_page()
                await page.goto(config.labs_url, wait_until="domcontentloaded")

                logger.info(f"[{profile['name']}] browser has been started, please log in via VNC")
                return True

            except Exception as e:
                logger.error(f"[{profile['name']}] failed to start: {e}")
                return False

    async def launch_for_login_ui(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
    ) -> bool:
        async with self._lock:
            await self._close_active()

            if profile_id is not None:
                profile = await profile_db.get_profile(profile_id)
                if not profile:
                    logger.error(f"Profile {profile_id} does not exist")
                    return False
                resolved_dir = self._get_profile_dir(profile_id)
            elif profile_dir is not None:
                resolved_dir = os.path.abspath(profile_dir)
                profile = {
                    "id": None,
                    "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                    "login_account": None,
                    "login_password": None,
                    "proxy_enabled": False,
                    "proxy_url": None,
                    "connection_token_override": None,
                    "is_logged_in": 0,
                }
            else:
                logger.error("Either profile_id or profile_dir must be provided")
                return False

            try:
                if not self._playwright:
                    await self.start()

                # ok = await self._ensure_vnc_stack()
                # if not ok:
                #     logger.error(f"[{profile['name']}] VNC service failed to start")
                #     return False

                os.makedirs(resolved_dir, exist_ok=True)
                self._clean_locks(resolved_dir)  # Clean up lock files
                proxy = await self._get_proxy(profile)

                # Non-headless, for VNC login
                self._active_context = await self._playwright.chromium.launch_persistent_context(
                    user_data_dir=resolved_dir,
                    headless=False,  # VNC visible
                    viewport={"width": 1024, "height": 768},
                    locale="en-US",
                    channel="chrome",
                    timezone_id="America/New_York",
                    proxy=proxy,
                    args=LOGIN_BROWSER_ARGS,
                    ignore_default_args=["--enable-automation"],
                )
                self._active_profile_id = profile_id
                self._active_profile_dir = resolved_dir

                page = self._active_context.pages[0] if self._active_context.pages else await self._active_context.new_page()
                await page.goto(config.labs_url, wait_until="domcontentloaded")

                logger.info(f"[{profile['name']}] browser has been started, please log in via VNC")
                return True

            except Exception as e:
                logger.error(f"[{profile['name']}] failed to start: {e}")
                return False

    async def close_browser(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
    ) -> dict[str, Any]:
        """Close browser and save state"""
        async with self._lock:
            resolved_dir = os.path.abspath(profile_dir) if profile_dir else None
            is_active = False
            if profile_id is not None and self._active_profile_id == profile_id:
                is_active = True
                resolved_dir = self._get_profile_dir(profile_id)
            elif resolved_dir is not None and self._active_profile_dir == resolved_dir:
                is_active = True

            if not is_active:
                return {"success": False, "error": "The Profile browser is not running"}

            if self._active_context:
                # Check login status
                is_logged_in = False
                if profile_id is not None:
                    profile = await profile_db.get_profile(profile_id)
                else:
                    profile = {
                        "id": None,
                        "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                        "login_account": None,
                        "login_password": None,
                        "proxy_enabled": False,
                        "proxy_url": None,
                        "connection_token_override": None,
                        "is_logged_in": 0,
                    }
                try:
                    cookies = await self._active_context.cookies("https://labs.google")
                    is_logged_in = any(c["name"] == config.session_cookie_name for c in cookies)
                except Exception:
                    pass

                await self._persist_login_state(
                    profile_id,
                    None,
                    email=self._resolve_known_email(profile or {}),
                    is_logged_in=is_logged_in,
                )
                if is_logged_in and profile_id is not None:
                    await self._save_google_cookies_from_context(profile_id, self._active_context)
                await self._close_active()
                await self._stop_vnc_stack()

                status = "Logged in" if is_logged_in else "Not logged in"
                ident = f"Profile {profile_id}" if profile_id is not None else f"Profile dir {resolved_dir}"
                logger.info(f"{ident} browser is closed, status: {status}")
                return {"success": True, "is_logged_in": is_logged_in}

            return {"success": True}

    async def extract_token(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
    ) -> str | None:
        """Extract Token (Headless mode, using persistence context)"""
        async with self._lock:
            if profile_id is not None:
                profile = await profile_db.get_profile(profile_id)
                if not profile:
                    return None
                resolved_dir = self._get_profile_dir(profile_id)
            elif profile_dir is not None:
                resolved_dir = os.path.abspath(profile_dir)
                profile = {
                    "id": None,
                    "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                    "login_account": None,
                    "login_password": None,
                    "proxy_enabled": False,
                    "proxy_url": None,
                    "connection_token_override": None,
                    "is_logged_in": 0,
                }
            else:
                raise ValueError("Either profile_id or profile_dir must be provided")

            # Check if there is persistent data
            if not os.path.exists(resolved_dir):
                logger.warning(f"[{profile['name']}] No persistent data, please log in first")
                return None

            # If the current profile browser is running (VNC login), extract it directly
            is_active = False
            if profile_id is not None and self._active_profile_id == profile_id:
                is_active = True
            elif self._active_profile_dir == resolved_dir:
                is_active = True

            if is_active and self._active_context:
                return await self._extract_from_context(profile, self._active_context)

            # Otherwise, start in headless mode
            context = None
            try:
                if not self._playwright:
                    await self.start()

                self._clean_locks(resolved_dir)  # Clean up lock files
                proxy = await self._get_proxy(profile)

                logger.info(f"[{profile['name']}] Headless mode extraction Token...")

                # Headless + persistence context
                context = await self._playwright.chromium.launch_persistent_context(
                    user_data_dir=resolved_dir,
                    headless=True,  # Headless saves resources
                    viewport={"width": 1024, "height": 768},
                    locale="en-US",
                    channel="chrome",
                    timezone_id="America/New_York",
                    proxy=proxy,
                    args=BROWSER_ARGS,  # Complete memory optimization parameters
                    ignore_default_args=["--enable-automation"],
                )

                token = await self._extract_from_context(profile, context)
                return token

            except Exception as e:
                logger.error(f"[{profile['name']}] extraction failed: {e}")
                return None
            finally:
                if context:
                    with contextlib.suppress(Exception):
                        await context.close()
                    logger.info(f"[{profile['name']}] Headless browser is closed")

    async def _extract_from_context(self, profile: dict[str, Any], context: BrowserContext) -> str | None:
        """Extract Token from context (refresh session via signin page)"""
        page = None
        try:
            page = await context.new_page()
            await self._install_page_route(page)

            # Visit the labs page and automatically advance to Google / Hosted Profile / labs first boot if necessary.
            logger.info(f"[{profile['name']}] Visit {config.labs_url} to refresh session...")
            await page.goto(config.labs_url, wait_until="domcontentloaded", timeout=60000)

            token = await self._settle_labs_session(profile, context, page)
            body_text = await self._safe_page_text(page)

            await self._persist_login_state(
                profile["id"],
                token,
                email=self._resolve_known_email(profile, body_text),
            )
            if token:
                logger.info(f"[{profile['name']}] Token extraction successful")
                await self._save_google_cookies_from_context(profile["id"], context)
            else:
                logger.warning(f"[{profile['name']}] Token not found, session may have expired")

            return token

        except Exception as e:
            logger.error(f"[{profile['name']}] Extraction exception: {e}")
            return None
        finally:
            if page:
                with contextlib.suppress(Exception):
                    await page.close()

    async def auto_login(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
        login_account: str | None = None,
        login_password: str | None = None,
        proxy_url: str | None = None,
    ) -> dict[str, Any]:
        if profile_id is not None:
            profile = await profile_db.get_profile(profile_id)
            if not profile:
                return {"success": False, "error": "Profile does not exist"}
            resolved_dir = self._get_profile_dir(profile_id)
            acc = str(profile.get("login_account") or "").strip()
            pwd = str(profile.get("login_password") or "").strip()
        elif profile_dir is not None:
            resolved_dir = os.path.abspath(profile_dir)
            acc = str(login_account or "").strip()
            pwd = str(login_password or "").strip()
            profile = {
                "id": None,
                "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                "login_account": acc,
                "login_password": pwd,
                "proxy_enabled": bool(proxy_url),
                "proxy_url": proxy_url,
                "connection_token_override": None,
                "is_logged_in": 0,
            }
        else:
            return {"success": False, "error": "Either profile_id or profile_dir must be provided"}

        if not acc or not pwd:
            return {"success": False, "error": "Please configure the login account and password for this account first"}

        async with self._lock:
            await self._close_active()

            context = None
            page = None
            use_vnc = False
            try:
                if not self._playwright:
                    await self.start()

                os.makedirs(resolved_dir, exist_ok=True)
                self._clean_locks(resolved_dir)
                proxy = await self._get_proxy(profile)

                if config.enable_vnc:
                    use_vnc = await self._ensure_vnc_stack()

                context = await self._playwright.chromium.launch_persistent_context(
                    user_data_dir=resolved_dir,
                    channel="chrome",
                    headless=not use_vnc,
                    viewport={"width": 1280, "height": 900},
                    locale="en-US",
                    timezone_id="America/New_York",
                    proxy=proxy,
                    args=LOGIN_BROWSER_ARGS if use_vnc else BROWSER_ARGS,
                    ignore_default_args=["--enable-automation"],
                )

                page = context.pages[0] if context.pages else await context.new_page()
                await self._install_page_route(page)
                await page.goto(config.login_url, wait_until="domcontentloaded", timeout=60000)

                for _ in range(45):
                    with contextlib.suppress(Exception):
                        await page.wait_for_load_state("domcontentloaded", timeout=3000)

                    body_text = await self._safe_page_text(page)
                    blocker = self._detect_login_blocker(body_text)
                    if blocker:
                        await self._persist_login_state(
                            profile_id,
                            None,
                            email=self._resolve_known_email(profile, body_text),
                        )
                        return {"success": False, "error": blocker, "requires_manual_action": True}

                    if use_vnc and not str(body_text or "").strip() and await self._handle_native_chrome_profile_prompts():
                        continue

                    if await self._handle_managed_account_prompts(page, body_text):
                        continue

                    if await self._advance_google_login(page, acc, pwd):
                        continue

                    if await self._handle_labs_onboarding(page, body_text):
                        continue

                    if await self._is_labs_session_ready(page, body_text):
                        break

                    await asyncio.sleep(1.0)

                token = await self._settle_labs_session(profile, context, page)
                body_text = await self._safe_page_text(page)
                await self._persist_login_state(
                    profile_id,
                    token,
                    email=self._resolve_known_email(profile, body_text),
                )
                if not token:
                    return {"success": False, "error": "The session token was not obtained, please use manual login instead."}

                if profile_id is not None:
                    await self._save_google_cookies_from_context(profile_id, context)

                return {
                    "success": True,
                    "is_logged_in": True,
                    "has_token": True,
                    "profile_name": profile["name"],
                }

            except Exception as e:
                logger.error(f"[{profile['name']}] Automatic login failed: {e}")
                return {"success": False, "error": str(e)}
            finally:
                if page:
                    with contextlib.suppress(Exception):
                        await page.close()
                if context:
                    with contextlib.suppress(Exception):
                        await context.close()
                if use_vnc:
                    await self._stop_vnc_stack()

    async def check_login_status(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
    ) -> dict[str, Any]:
        """Check login status"""
        if profile_id is not None:
            profile = await profile_db.get_profile(profile_id)
            if not profile:
                return {"success": False, "error": "Profile does not exist"}
        elif profile_dir is not None:
            resolved_dir = os.path.abspath(profile_dir)
            profile = {
                "id": None,
                "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                "login_account": None,
                "login_password": None,
                "proxy_enabled": False,
                "proxy_url": None,
                "connection_token_override": None,
                "is_logged_in": 0,
            }
        else:
            return {"success": False, "error": "Either profile_id or profile_dir must be provided"}

        token = await self.peek_token(profile_id, profile_dir=profile_dir)
        await self._persist_login_state(
            profile_id,
            token,
            email=self._resolve_known_email(profile),
        )
        return {
            "success": True,
            "is_logged_in": token is not None,
            "profile_name": profile["name"]
        }

    async def peek_token(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
    ) -> str | None:
        """Obtain the token lightly (no access to the page, only read the cookie)"""
        async with self._lock:
            if profile_id is not None:
                profile = await profile_db.get_profile(profile_id)
                if not profile:
                    return None
                resolved_dir = self._get_profile_dir(profile_id)
            elif profile_dir is not None:
                resolved_dir = os.path.abspath(profile_dir)
                profile = {
                    "id": None,
                    "name": os.path.basename(resolved_dir.rstrip(r"\/")),
                    "login_account": None,
                    "login_password": None,
                    "proxy_enabled": False,
                    "proxy_url": None,
                    "connection_token_override": None,
                    "is_logged_in": 0,
                }
            else:
                raise ValueError("Either profile_id or profile_dir must be provided")

            if not os.path.exists(resolved_dir):
                return None

            is_active = False
            if profile_id is not None and self._active_profile_id == profile_id:
                is_active = True
            elif self._active_profile_dir == resolved_dir:
                is_active = True

            if is_active and self._active_context:
                return await self._get_session_cookie(self._active_context)

            context = None
            try:
                if not self._playwright:
                    await self.start()

                self._clean_locks(resolved_dir)
                proxy = await self._get_proxy(profile)
                context = await self._playwright.chromium.launch_persistent_context(
                    user_data_dir=resolved_dir,
                    channel="chrome",
                    headless=True,
                    viewport={"width": 1024, "height": 768},
                    locale="en-US",
                    timezone_id="America/New_York",
                    proxy=proxy,
                    args=BROWSER_ARGS,
                    ignore_default_args=["--enable-automation"],
                )
                return await self._get_session_cookie(context)
            except Exception:
                return None
            finally:
                if context:
                    with contextlib.suppress(Exception):
                        await context.close()

    async def delete_profile_data(
        self,
        profile_id: int | None = None,
        *,
        profile_dir: str | None = None,
    ):
        """Delete profile data"""
        if profile_id is not None:
            resolved_dir = self._get_profile_dir(profile_id)
        elif profile_dir is not None:
            resolved_dir = os.path.abspath(profile_dir)
        else:
            raise ValueError("Either profile_id or profile_dir must be provided")

        if os.path.exists(resolved_dir):
            shutil.rmtree(resolved_dir)
            logger.info(f"Deleted: {resolved_dir}")

    def get_active_profile_id(self) -> int | None:
        return self._active_profile_id

    def get_status(self) -> dict[str, Any]:
        status = self._get_supervisor_status()
        vnc_stack_running = all(status.get(p) == "RUNNING" for p in ("xvfb", "x11vnc", "novnc")) if status else False
        return {
            "is_running": self._playwright is not None,
            "active_profile_id": self._active_profile_id,
            "active_profile_dir": self._active_profile_dir,
            "has_active_browser": self._active_context is not None,
            "profiles_dir": config.profiles_dir,
            "enable_vnc": bool(config.enable_vnc),
            "vnc_stack_running": bool(vnc_stack_running),
        }


browser_manager = BrowserManager()
