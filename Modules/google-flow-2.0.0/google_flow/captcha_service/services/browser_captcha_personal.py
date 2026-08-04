"""
Browser automatically obtains reCAPTCHA token
Use nodriver (successor of undetected-chromedriver) to implement anti-detection of browsers
Support resident mode: maintain a globally shared resident tab pool and generate tokens instantly
"""
import asyncio
import contextlib
import gc
import hashlib
import inspect
import os
import shutil
import subprocess
import sys
import time
from collections.abc import Iterable
from typing import Any, Optional

from ..core.config import config
from ..core.logger import debug_logger
from .browser_captcha import TokenAcquireResult
from google_flow.constants import RECAPTCHA_SITE_KEY


# ==================== Docker environment detection ====================
def _is_running_in_docker() -> bool:
    """Detect if running in Docker container"""
    # Method 1: Check /.dockerenv file
    if os.path.exists('/.dockerenv'):
        return True
    # Method 2: Check cgroup
    try:
        with open('/proc/1/cgroup') as f:
            content = f.read()
            if 'docker' in content or 'kubepods' in content or 'containerd' in content:
                return True
    except:
        pass
    # Method 3: Check environment variables
    return bool(os.environ.get('DOCKER_CONTAINER') or os.environ.get('KUBERNETES_SERVICE_HOST'))


IS_DOCKER = _is_running_in_docker()


def _is_truthy_env(name: str) -> bool:
    """Determine whether the environment variable is true."""
    value = os.environ.get(name, "")
    return value.strip().lower() in {"1", "true", "yes", "on"}


ALLOW_DOCKER_HEADED = (
    _is_truthy_env("ALLOW_DOCKER_HEADED_CAPTCHA")
    or _is_truthy_env("ALLOW_DOCKER_BROWSER_CAPTCHA")
)
DOCKER_HEADED_BLOCKED = IS_DOCKER and not ALLOW_DOCKER_HEADED


# ==================== nodriver automatic installation ====================
def _run_pip_install(package: str, use_mirror: bool = False) -> bool:
    """Run pip install command

    Args:
        package: package name
        use_mirror: whether to use domestic mirror

    Returns:
        Is the installation successful?
    """
    cmd = [sys.executable, '-m', 'pip', 'install', package]
    if use_mirror:
        cmd.extend(['-i', 'https://pypi.tuna.tsinghua.edu.cn/simple'])

    try:
        debug_logger.log_info(f"[BrowserCaptcha] Installing {package}...")
        print(f"[BrowserCaptcha] Installing {package}...")
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
        if result.returncode == 0:
            debug_logger.log_info(f"[BrowserCaptcha] ✅ {package} installed successfully")
            print(f"[BrowserCaptcha] ✅ {package} installed successfully")
            return True
        else:
            debug_logger.log_warning(f"[BrowserCaptcha] {package} installation failed: {result.stderr[:200]}")
            return False
    except Exception as e:
        debug_logger.log_warning(f"[BrowserCaptcha] {package} installation exception: {e}")
        return False


def _ensure_nodriver_installed() -> bool:
    """Check if nodriver is available and not install it automatically at runtime."""
    try:
        import nodriver
        debug_logger.log_info("[BrowserCaptcha] nodriver installed")
        return True
    except ImportError:
        debug_logger.log_warning("[BrowserCaptcha] nodriver is not installed, please install it manually: pip install nodriver")
        print("[BrowserCaptcha] ⚠️ nodriver is not installed, please install it manually: pip install nodriver")
        return False


def _normalize_browser_executable_path(value: str | None) -> str | None:
    candidate = str(value or "").strip().strip('"').strip("'")
    return candidate or None


def _resolve_browser_executable_path() -> str | None:
    """Use explicit configuration first, fall back to the system browser and Playwright Chromium second."""
    env_candidate = _normalize_browser_executable_path(os.environ.get("BROWSER_EXECUTABLE_PATH"))
    if env_candidate:
        if os.path.isfile(env_candidate):
            return env_candidate
        resolved_env = shutil.which(env_candidate)
        if resolved_env:
            os.environ["BROWSER_EXECUTABLE_PATH"] = resolved_env
            debug_logger.log_warning(
                f"[BrowserCaptcha] BROWSER_EXECUTABLE_PATH is not an absolute path and has been resolved to: {resolved_env}"
            )
            return resolved_env
        debug_logger.log_warning(f"[BrowserCaptcha] BROWSER_EXECUTABLE_PATH is not available: {env_candidate}")

    command_candidates = [
        "google-chrome",
        "google-chrome-stable",
        "chromium",
        "chromium-browser",
        "microsoft-edge",
        "microsoft-edge-stable",
        "msedge",
        "chrome",
    ]
    for command in command_candidates:
        resolved = shutil.which(command)
        if resolved:
            os.environ.setdefault("BROWSER_EXECUTABLE_PATH", resolved)
            debug_logger.log_info(f"[BrowserCaptcha] Use system browser executable: {resolved}")
            return resolved

    filesystem_candidates = [
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        "/usr/bin/google-chrome",
        "/usr/bin/google-chrome-stable",
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
        "/usr/bin/microsoft-edge",
        "/usr/bin/microsoft-edge-stable",
        "/opt/google/chrome/chrome",
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
    ]
    for candidate in filesystem_candidates:
        if os.path.isfile(candidate):
            os.environ.setdefault("BROWSER_EXECUTABLE_PATH", candidate)
            debug_logger.log_info(f"[BrowserCaptcha] Use known browser path: {candidate}")
            return candidate

    try:
        from playwright.sync_api import sync_playwright

        with sync_playwright() as playwright:
            playwright_candidate = _normalize_browser_executable_path(
                getattr(playwright.chromium, "executable_path", None)
            )
        if playwright_candidate and os.path.exists(playwright_candidate):
            os.environ["BROWSER_EXECUTABLE_PATH"] = playwright_candidate
            debug_logger.log_info(
                f"[BrowserCaptcha] Using Playwright Chromium executable: {playwright_candidate}"
            )
            return playwright_candidate
    except Exception as exc:
        debug_logger.log_warning(f"[BrowserCaptcha] Failed to parse Playwright Chromium path: {exc}")

    debug_logger.log_warning(
        "[BrowserCaptcha] No available Chrome/Chromium executable file was found, leaving it to nodriver for self-detection"
    )
    return None


# Try importing nodriver
uc = None
NODRIVER_AVAILABLE = False

if DOCKER_HEADED_BLOCKED:
    debug_logger.log_warning(
        "[BrowserCaptcha] Detects Docker environment and disables built-in browser coding by default."
        "To enable it set ALLOW_DOCKER_HEADED_CAPTCHA=true and provide DISPLAY/Xvfb."
    )
    print("[BrowserCaptcha] ⚠️ Docker environment detected, built-in browser coding is disabled by default")
    print("[BrowserCaptcha] To enable please set ALLOW_DOCKER_HEADED_CAPTCHA=true and provide DISPLAY/Xvfb")
else:
    if IS_DOCKER and ALLOW_DOCKER_HEADED:
        debug_logger.log_warning(
            "[BrowserCaptcha] Docker's built-in browser coding whitelist has been enabled, please ensure that DISPLAY/Xvfb is available"
        )
        print("[BrowserCaptcha] ✅ Docker’s built-in browser coding whitelist is enabled")
    if _ensure_nodriver_installed():
        try:
            import nodriver as uc
            NODRIVER_AVAILABLE = True
        except ImportError as e:
            debug_logger.log_error(f"[BrowserCaptcha] nodriver import failed: {e}")
            print(f"[BrowserCaptcha] ❌ nodriver import failed: {e}")


class ResidentTabInfo:
    """Resident tab information structure"""
    def __init__(self, tab, slot_id: str, project_id: str | None = None):
        self.tab = tab
        self.slot_id = slot_id
        self.project_id = project_id or slot_id
        self.recaptcha_ready = False
        self.created_at = time.time()
        self.last_used_at = time.time()  # last use time
        self.use_count = 0  # Number of uses
        self.solve_lock = asyncio.Lock()  # Serialize execution on the same tab to reduce concurrency conflicts


class BrowserCaptchaService:
    """The browser automatically obtains reCAPTCHA token (nodriver header mode)

    Two modes are supported:
    1. Resident Mode: Maintain a global shared resident tab page pool, and whoever grabs the free page will execute it.
    2. Legacy Mode: Create a new tab page with each request (fallback)
    """

    _instance: Optional['BrowserCaptchaService'] = None
    _lock = asyncio.Lock()

    def __init__(self, db=None):
        """Initialize service"""
        self.headless = False  # nodriver head mode
        self.browser = None
        self._initialized = False
        self.website_key = RECAPTCHA_SITE_KEY
        self.db = db
        # Use None to let nodriver automatically create temporary directories to avoid directory locking issues
        self.user_data_dir = None

        # Resident mode related attributes: The coding tab page is a global shared pool and is no longer bound one-to-one by project_id
        self._resident_tabs: dict[str, ResidentTabInfo] = {}  # slot_id -> Resident tab information
        self._project_resident_affinity: dict[str, str] = {}  # project_id -> slot_id (last used)
        self._resident_slot_seq = 0
        self._resident_pick_index = 0
        self._resident_lock = asyncio.Lock()  # Protect resident tab operations
        self._browser_lock = asyncio.Lock()  # Protect browser initialization/closure/restart to avoid repeatedly pulling up instances
        self._tab_build_lock = asyncio.Lock()  # Serialize cold start/rebuild to reduce nodriver jitter
        self._legacy_lock = asyncio.Lock()  # Avoid legacy fallback concurrency and create temporary tabs
        self._max_resident_tabs = 5  # Maximum number of resident tabs (supports concurrency)
        self._idle_tab_ttl_seconds = 600  # Tab idle timeout (seconds)
        self._idle_reaper_task: asyncio.Task | None = None  # Idle recycling task
        self._command_timeout_seconds = 8.0
        self._navigation_timeout_seconds = 20.0
        self._solve_timeout_seconds = 45.0
        self._session_refresh_timeout_seconds = 45.0

        # Compatible with old API (retain single resident attribute as alias)
        self.resident_project_id: str | None = None  # backwards compatible
        self.resident_tab = None                         # backwards compatible
        self._running = False                            # backwards compatible
        self._recaptcha_ready = False                    # backwards compatible
        self._last_fingerprint: dict[str, Any] | None = None
        self._resident_error_streaks: dict[str, int] = {}
        # Customized site coding resident page (for score-test)
        self._custom_tabs: dict[str, dict[str, Any]] = {}
        self._custom_lock = asyncio.Lock()
        self._stats = {
            "req_total": 0,
            "gen_ok": 0,
            "gen_fail": 0,
            "api_403": 0,
        }
        self._closing = False

    @classmethod
    async def get_instance(cls, db=None) -> 'BrowserCaptchaService':
        """Get singleton instance"""
        if cls._instance is None:
            async with cls._lock:
                if cls._instance is None:
                    cls._instance = cls(db)
                    # Start idle tab recycling task
                    cls._instance._idle_reaper_task = asyncio.create_task(
                        cls._instance._idle_tab_reaper_loop()
                    )
        return cls._instance

    async def reload_config(self):
        """Hot update configuration (reload from database)"""
        from ..core.config import config
        old_max_tabs = self._max_resident_tabs
        old_idle_ttl = self._idle_tab_ttl_seconds

        self._max_resident_tabs = config.personal_max_resident_tabs
        self._idle_tab_ttl_seconds = config.personal_idle_tab_ttl_seconds

        debug_logger.log_info(
            f"[BrowserCaptcha] Personal configuration has been hot updated: "
            f"max_tabs {old_max_tabs}->{self._max_resident_tabs}, "
            f"idle_ttl {old_idle_ttl}s->{self._idle_tab_ttl_seconds}s"
        )

    def _check_available(self):
        """Check if the service is available"""
        if DOCKER_HEADED_BLOCKED:
            raise RuntimeError(
                "Docker environment is detected and built-in browser coding is disabled by default."
                "To enable it, set the environment variable ALLOW_DOCKER_HEADED_CAPTCHA=true and provide DISPLAY/Xvfb."
            )
        if IS_DOCKER and not os.environ.get("DISPLAY"):
            raise RuntimeError(
                "Docker's built-in browser coding is enabled, but DISPLAY is not set."
                "Please set DISPLAY (eg :99) and start Xvfb."
            )
        if not NODRIVER_AVAILABLE or uc is None:
            raise RuntimeError(
                "nodriver is not installed or available."
                "Please install manually: pip install nodriver"
            )

    async def _run_with_timeout(self, awaitable, timeout_seconds: float, label: str):
        """Uniformly close the nodriver operation timeout to prevent a single stuck from holding up the entire request link."""
        effective_timeout = max(0.5, float(timeout_seconds or 0))
        try:
            return await asyncio.wait_for(awaitable, timeout=effective_timeout)
        except asyncio.TimeoutError as e:
            raise TimeoutError(f"{label} timeout ({effective_timeout:.1f}s)") from e

    async def _tab_evaluate(self, tab, script: str, label: str, timeout_seconds: float | None = None):
        return await self._run_with_timeout(
            tab.evaluate(script),
            timeout_seconds or self._command_timeout_seconds,
            label,
        )

    async def _tab_get(self, tab, url: str, label: str, timeout_seconds: float | None = None):
        return await self._run_with_timeout(
            tab.get(url),
            timeout_seconds or self._navigation_timeout_seconds,
            label,
        )

    async def _browser_get(self, url: str, label: str, new_tab: bool = False, timeout_seconds: float | None = None):
        return await self._run_with_timeout(
            self.browser.get(url, new_tab=new_tab),
            timeout_seconds or self._navigation_timeout_seconds,
            label,
        )

    async def _tab_reload(self, tab, label: str, timeout_seconds: float | None = None):
        return await self._run_with_timeout(
            tab.reload(),
            timeout_seconds or self._navigation_timeout_seconds,
            label,
        )

    async def _get_browser_cookies(self, label: str, timeout_seconds: float | None = None):
        return await self._run_with_timeout(
            self.browser.cookies.get_all(),
            timeout_seconds or self._command_timeout_seconds,
            label,
        )

    async def _browser_send_command(
        self,
        method: str,
        params: dict[str, Any] | None = None,
        label: str | None = None,
        timeout_seconds: float | None = None,
    ):
        return await self._run_with_timeout(
            self.browser.connection.send(method, params) if params else self.browser.connection.send(method),
            timeout_seconds or self._command_timeout_seconds,
            label or method,
        )

    async def _idle_tab_reaper_loop(self):
        """Idle tab recycling loop"""
        while True:
            try:
                await asyncio.sleep(30)  # Check every 30 seconds
                current_time = time.time()
                tabs_to_close = []

                async with self._resident_lock:
                    for slot_id, resident_info in list(self._resident_tabs.items()):
                        if resident_info.solve_lock.locked():
                            continue
                        idle_seconds = current_time - resident_info.last_used_at
                        if idle_seconds >= self._idle_tab_ttl_seconds:
                            tabs_to_close.append(slot_id)
                            debug_logger.log_info(
                                f"[BrowserCaptcha] slot={slot_id} idle {idle_seconds:.0f}s, ready for recycling"
                            )

                for slot_id in tabs_to_close:
                    await self._close_resident_tab(slot_id)

            except asyncio.CancelledError:
                return
            except Exception as e:
                debug_logger.log_warning(f"[BrowserCaptcha] Idle tab recycling exception: {e}")

    async def _evict_lru_tab_if_needed(self) -> bool:
        """If the shared pool limit is reached, use the LRU policy to evict the idle tabs that have not been used for the longest time."""
        async with self._resident_lock:
            if len(self._resident_tabs) < self._max_resident_tabs:
                return True

            lru_slot_id = None
            lru_project_hint = None
            lru_last_used = float('inf')

            for slot_id, resident_info in self._resident_tabs.items():
                if resident_info.solve_lock.locked():
                    continue
                if resident_info.last_used_at < lru_last_used:
                    lru_last_used = resident_info.last_used_at
                    lru_slot_id = slot_id
                    lru_project_hint = resident_info.project_id

        if lru_slot_id:
            debug_logger.log_info(
                f"[BrowserCaptcha] The number of tabs has reached the upper limit ({self._max_resident_tabs}),"
                f"Eliminate the slot={lru_slot_id}, project_hint={lru_project_hint} that has not been used for the longest time"
            )
            await self._close_resident_tab(lru_slot_id)
            return True

        debug_logger.log_warning(
            f"[BrowserCaptcha] The number of tabs has reached the upper limit ({self._max_resident_tabs}),"
            "But there are currently no free tabs that are safe to retire"
        )
        return False

    async def _get_reserved_tab_ids(self) -> set[int]:
        """Collect tabs currently occupied by the resident/custom pool. Legacy mode must not be reused."""
        reserved_tab_ids: set[int] = set()

        async with self._resident_lock:
            for resident_info in self._resident_tabs.values():
                if resident_info and resident_info.tab:
                    reserved_tab_ids.add(id(resident_info.tab))

        async with self._custom_lock:
            for item in self._custom_tabs.values():
                tab = item.get("tab") if isinstance(item, dict) else None
                if tab:
                    reserved_tab_ids.add(id(tab))

        return reserved_tab_ids

    def _next_resident_slot_id(self) -> str:
        self._resident_slot_seq += 1
        return f"slot-{self._resident_slot_seq}"

    def _forget_project_affinity_for_slot_locked(self, slot_id: str | None):
        if not slot_id:
            return
        stale_projects = [
            project_id
            for project_id, mapped_slot_id in self._project_resident_affinity.items()
            if mapped_slot_id == slot_id
        ]
        for project_id in stale_projects:
            self._project_resident_affinity.pop(project_id, None)

    def _resolve_affinity_slot_locked(self, project_id: str | None) -> str | None:
        normalized_project_id = str(project_id or "").strip()
        if not normalized_project_id:
            return None
        slot_id = self._project_resident_affinity.get(normalized_project_id)
        if slot_id and slot_id in self._resident_tabs:
            return slot_id
        if slot_id:
            self._project_resident_affinity.pop(normalized_project_id, None)
        return None

    def _remember_project_affinity(self, project_id: str | None, slot_id: str | None, resident_info: ResidentTabInfo | None):
        normalized_project_id = str(project_id or "").strip()
        if not normalized_project_id or not slot_id or resident_info is None:
            return
        self._project_resident_affinity[normalized_project_id] = slot_id
        resident_info.project_id = normalized_project_id

    def _resolve_resident_slot_for_project_locked(
        self,
        project_id: str | None = None,
    ) -> tuple[str | None, ResidentTabInfo | None]:
        """The nearest mapping is given priority; when there is no mapping, it falls back to the shared pool global selection."""
        slot_id = self._resolve_affinity_slot_locked(project_id)
        if slot_id:
            resident_info = self._resident_tabs.get(slot_id)
            if resident_info and resident_info.tab:
                return slot_id, resident_info
        return self._select_resident_slot_locked(project_id)

    def _select_resident_slot_locked(
        self,
        project_id: str | None = None,
    ) -> tuple[str | None, ResidentTabInfo | None]:
        candidates = [
            (slot_id, resident_info)
            for slot_id, resident_info in self._resident_tabs.items()
            if resident_info and resident_info.tab
        ]
        if not candidates:
            return None, None

        # The shared coding pool is no longer bound by project_id; here it is only based on "whether it is ready/idle/usage history"
        # Make global selections to avoid hard binding requests to fixed tabs when working with 4 tokens/4 projects.
        ready_idle = [
            (slot_id, resident_info)
            for slot_id, resident_info in candidates
            if resident_info.recaptcha_ready and not resident_info.solve_lock.locked()
        ]
        ready_busy = [
            (slot_id, resident_info)
            for slot_id, resident_info in candidates
            if resident_info.recaptcha_ready and resident_info.solve_lock.locked()
        ]
        cold_idle = [
            (slot_id, resident_info)
            for slot_id, resident_info in candidates
            if not resident_info.recaptcha_ready and not resident_info.solve_lock.locked()
        ]

        pool = ready_idle or ready_busy or cold_idle or candidates
        pool.sort(key=lambda item: (item[1].last_used_at, item[1].use_count, item[1].created_at, item[0]))

        pick_index = self._resident_pick_index % len(pool)
        self._resident_pick_index = (self._resident_pick_index + 1) % max(len(candidates), 1)
        return pool[pick_index]

    async def _ensure_resident_tab(
        self,
        project_id: str | None = None,
        *,
        force_create: bool = False,
        return_slot_key: bool = False,
    ):
        """Make sure there are available tabs in the shared coding tab pool.

        Logic:
        - Prioritize reusing idle tabs
        - If all tabs are busy and the upper limit has not been reached, continue to expand the capacity
        - Allow requests to be queued and wait for existing tabs after reaching the upper limit
        """
        def wrap(slot_id: str | None, resident_info: ResidentTabInfo | None):
            if return_slot_key:
                return slot_id, resident_info
            return resident_info

        async with self._resident_lock:
            slot_id, resident_info = self._select_resident_slot_locked(project_id)
            if self._resident_tabs:
                all_busy = all(info.solve_lock.locked() for info in self._resident_tabs.values())
            else:
                all_busy = True

            should_create = force_create or not resident_info or (all_busy and len(self._resident_tabs) < self._max_resident_tabs)
            if not should_create:
                return wrap(slot_id, resident_info)

            if len(self._resident_tabs) >= self._max_resident_tabs:
                return wrap(slot_id, resident_info)

        async with self._tab_build_lock:
            async with self._resident_lock:
                slot_id, resident_info = self._select_resident_slot_locked(project_id)
                if self._resident_tabs:
                    all_busy = all(info.solve_lock.locked() for info in self._resident_tabs.values())
                else:
                    all_busy = True

                should_create = force_create or not resident_info or (all_busy and len(self._resident_tabs) < self._max_resident_tabs)
                if not should_create:
                    return wrap(slot_id, resident_info)

                if len(self._resident_tabs) >= self._max_resident_tabs:
                    return wrap(slot_id, resident_info)

                new_slot_id = self._next_resident_slot_id()

            resident_info = await self._create_resident_tab(new_slot_id, project_id=project_id)
            if resident_info is None:
                async with self._resident_lock:
                    slot_id, fallback_info = self._select_resident_slot_locked(project_id)
                return wrap(slot_id, fallback_info)

            async with self._resident_lock:
                self._resident_tabs[new_slot_id] = resident_info
                self._sync_compat_resident_state()
                return wrap(new_slot_id, resident_info)

    async def _rebuild_resident_tab(
        self,
        project_id: str | None = None,
        *,
        slot_id: str | None = None,
        return_slot_key: bool = False,
    ):
        """Rebuild a tab in the shared pool. Prioritize rebuilding the slots recently used by the current project."""
        def wrap(actual_slot_id: str | None, resident_info: ResidentTabInfo | None):
            if return_slot_key:
                return actual_slot_id, resident_info
            return resident_info

        async with self._tab_build_lock:
            async with self._resident_lock:
                actual_slot_id = slot_id
                if actual_slot_id is None:
                    actual_slot_id, _ = self._resolve_resident_slot_for_project_locked(project_id)

                old_resident = self._resident_tabs.pop(actual_slot_id, None) if actual_slot_id else None
                self._forget_project_affinity_for_slot_locked(actual_slot_id)
                if actual_slot_id:
                    self._resident_error_streaks.pop(actual_slot_id, None)
                self._sync_compat_resident_state()

            if old_resident:
                try:
                    async with old_resident.solve_lock:
                        await self._close_tab_quietly(old_resident.tab)
                except Exception:
                    await self._close_tab_quietly(old_resident.tab)

            actual_slot_id = actual_slot_id or self._next_resident_slot_id()
            resident_info = await self._create_resident_tab(actual_slot_id, project_id=project_id)
            if resident_info is None:
                debug_logger.log_warning(
                    f"[BrowserCaptcha] slot={actual_slot_id}, project_id={project_id} Failed to rebuild shared tab page"
                )
                return wrap(actual_slot_id, None)

            async with self._resident_lock:
                self._resident_tabs[actual_slot_id] = resident_info
                self._remember_project_affinity(project_id, actual_slot_id, resident_info)
                self._sync_compat_resident_state()
                return wrap(actual_slot_id, resident_info)

    def _sync_compat_resident_state(self):
        """Synchronize legacy single-resident compatible attributes."""
        first_resident = next(iter(self._resident_tabs.values()), None)
        if first_resident:
            self.resident_project_id = first_resident.project_id
            self.resident_tab = first_resident.tab
            self._running = True
            self._recaptcha_ready = bool(first_resident.recaptcha_ready)
        else:
            self.resident_project_id = None
            self.resident_tab = None
            self._running = False
            self._recaptcha_ready = False

    async def _close_tab_quietly(self, tab):
        if not tab:
            return
        with contextlib.suppress(Exception):
            await self._run_with_timeout(
                tab.close(),
                timeout_seconds=5.0,
                label="tab.close",
            )

    def _detach_asyncio_subprocess_resources(self, proc) -> None:
        """Disconnect the pipe reference on the closed asyncio child process object to avoid transport noise during Windows destruction."""
        if proc is None:
            return

        for stream_name in ("stdin", "stdout", "stderr"):
            try:
                stream = getattr(proc, stream_name, None)
            except Exception:
                stream = None

            if stream is not None:
                transport = None
                for attr_name in ("_transport", "transport"):
                    try:
                        candidate = getattr(stream, attr_name, None)
                    except Exception:
                        candidate = None
                    if candidate is not None:
                        transport = candidate
                        break

                if transport is not None:
                    try:
                        close_method = getattr(transport, "close", None)
                        if callable(close_method):
                            close_method()
                    except Exception:
                        pass

                try:
                    close_method = getattr(stream, "close", None)
                    if callable(close_method):
                        close_method()
                except Exception:
                    pass

            with contextlib.suppress(Exception):
                setattr(proc, stream_name, None)

        try:
            proc_transport = getattr(proc, "_transport", None)
        except Exception:
            proc_transport = None

        if proc_transport is not None:
            pipe_entries = None
            try:
                pipe_entries = getattr(proc_transport, "_pipes", None)
            except Exception:
                pipe_entries = None

            if isinstance(pipe_entries, dict):
                for pipe_proto in list(pipe_entries.values()):
                    pipe_transport = getattr(pipe_proto, "pipe", None)
                    if pipe_transport is not None:
                        try:
                            close_method = getattr(pipe_transport, "close", None)
                            if callable(close_method):
                                close_method()
                        except Exception:
                            pass
                    with contextlib.suppress(Exception):
                        pipe_proto.pipe = None
                    with contextlib.suppress(Exception):
                        pipe_proto.proc = None
                with contextlib.suppress(Exception):
                    proc_transport._pipes = {}

            try:
                close_method = getattr(proc_transport, "close", None)
                if callable(close_method):
                    close_method()
            except Exception:
                pass
            with contextlib.suppress(Exception):
                proc_transport._proc = None

        with contextlib.suppress(Exception):
            proc._transport = None

    async def _disconnect_browser_connection(self, connection):
        if not connection:
            return
        disconnect_method = getattr(connection, "disconnect", None)
        if disconnect_method is None:
            return
        result = disconnect_method()
        if inspect.isawaitable(result):
            await self._run_with_timeout(
                result,
                timeout_seconds=5.0,
                label="connection.disconnect",
            )

    async def _wait_browser_process_exit(self, proc, timeout_seconds: float = 5.0):
        if proc is None:
            return
        wait_method = getattr(proc, "wait", None)
        if not callable(wait_method):
            return
        try:
            wait_result = wait_method()
            if inspect.isawaitable(wait_result):
                await asyncio.wait_for(wait_result, timeout=timeout_seconds)
        except asyncio.TimeoutError:
            kill_method = getattr(proc, "kill", None)
            if callable(kill_method):
                try:
                    kill_method()
                except ProcessLookupError:
                    pass
                except Exception:
                    pass
            try:
                wait_result = wait_method()
                if inspect.isawaitable(wait_result):
                    await asyncio.wait_for(
                        wait_result,
                        timeout=max(1.0, timeout_seconds / 2),
                    )
            except Exception:
                pass
        except ProcessLookupError:
            pass
        except Exception:
            pass

    async def _stop_browser_process(self, browser_instance):
        """Compatible with nodriver synchronization stop API to safely stop the browser process."""
        if not browser_instance:
            return
        connection = getattr(browser_instance, "connection", None)
        proc = getattr(browser_instance, "_process", None) or getattr(browser_instance, "process", None)

        if connection:
            try:
                await self._disconnect_browser_connection(connection)
            except Exception as e:
                debug_logger.log_warning(
                    f"[BrowserCaptcha] disconnect Browser connection failed: {e}"
                )

        stop_method = getattr(browser_instance, "stop", None)
        if stop_method is None:
            return
        result = stop_method()
        if inspect.isawaitable(result):
            await self._run_with_timeout(
                result,
                timeout_seconds=10.0,
                label="browser.stop",
            )
        if proc:
            await self._wait_browser_process_exit(proc, timeout_seconds=5.0)
            self._detach_asyncio_subprocess_resources(proc)
        if connection:
            with contextlib.suppress(Exception):
                connection._websocket = None
        with contextlib.suppress(Exception):
            browser_instance._process = None
        if sys.platform.startswith("win"):
            # Give the connection_lost callback under Windows a chance to finish to avoid destroying the transport after the event loop is closed.
            await asyncio.sleep(0)
            await asyncio.sleep(0.05)

    async def _shutdown_browser_runtime_locked(self, reason: str):
        """Under the premise of holding _browser_lock, completely clean up the current browser running state."""
        browser_instance = self.browser
        self.browser = None
        self._initialized = False
        self._last_fingerprint = None

        async with self._resident_lock:
            resident_items = list(self._resident_tabs.values())
            self._resident_tabs.clear()
            self._project_resident_affinity.clear()
            self._resident_error_streaks.clear()
            self._sync_compat_resident_state()

        custom_items = list(self._custom_tabs.values())
        self._custom_tabs.clear()

        closed_tabs = set()

        async def close_once(tab):
            if not tab:
                return
            tab_key = id(tab)
            if tab_key in closed_tabs:
                return
            closed_tabs.add(tab_key)
            await self._close_tab_quietly(tab)

        for resident_info in resident_items:
            await close_once(resident_info.tab)

        for item in custom_items:
            tab = item.get("tab") if isinstance(item, dict) else None
            await close_once(tab)

        if browser_instance:
            try:
                await self._stop_browser_process(browser_instance)
            except Exception as e:
                debug_logger.log_warning(
                    f"[BrowserCaptcha] Failed to stop browser instance ({reason}): {e}"
                )

    async def initialize(self):
        """Initialize nodriver browser"""
        self._check_available()

        async with self._browser_lock:
            browser_needs_restart = False

            if self._initialized and self.browser:
                try:
                    if self.browser.stopped:
                        debug_logger.log_warning("[BrowserCaptcha] The browser has stopped and is preparing to reinitialize...")
                        browser_needs_restart = True
                    else:
                        if self._idle_reaper_task is None or self._idle_reaper_task.done():
                            self._idle_reaper_task = asyncio.create_task(self._idle_tab_reaper_loop())
                        return
                except Exception as e:
                    debug_logger.log_warning(f"[BrowserCaptcha] Browser status check exception, ready to re-initialize: {e}")
                    browser_needs_restart = True
            elif self.browser is not None or self._initialized:
                browser_needs_restart = True

            if browser_needs_restart:
                await self._shutdown_browser_runtime_locked(reason="initialize_recovery")

            try:
                if self.user_data_dir:
                    debug_logger.log_info(f"[BrowserCaptcha] Starting nodriver browser (user data directory: {self.user_data_dir})...")
                    os.makedirs(self.user_data_dir, exist_ok=True)
                else:
                    debug_logger.log_info("[BrowserCaptcha] Starting nodriver browser (using temporary directory)...")

                browser_executable_path = _resolve_browser_executable_path()
                if browser_executable_path:
                    debug_logger.log_info(
                        f"[BrowserCaptcha] Use the specified browser executable file: {browser_executable_path}"
                    )

                # Start the nodriver browser (starts in the background, does not occupy the foreground)
                config = uc.Config(
                    headless=self.headless,
                    user_data_dir=self.user_data_dir,
                    browser_executable_path=browser_executable_path,
                    sandbox=False,
                    browser_args=[
                        '--disable-dev-shm-usage',
                        '--disable-setuid-sandbox',
                        '--disable-gpu',
                        '--window-size=1280,720',
                        '--window-position=3000,3000',  # Move window position off screen
                        '--profile-directory=Default',
                        '--disable-extensions',
                        '--disable-background-networking',
                        '--disable-sync',
                        '--disable-translate',
                        '--disable-default-apps',
                        '--no-first-run',
                        '--no-default-browser-check',
                    ]
                )
                self.browser = await self._run_with_timeout(
                    uc.start(config),
                    timeout_seconds=30.0,
                    label="nodriver.start",
                )

                self._initialized = True
                if self._idle_reaper_task is None or self._idle_reaper_task.done():
                    self._idle_reaper_task = asyncio.create_task(self._idle_tab_reaper_loop())
                debug_logger.log_info(f"[BrowserCaptcha] ✅ nodriver browser started (Profile: {self.user_data_dir})")

            except Exception as e:
                self.browser = None
                self._initialized = False
                debug_logger.log_error(f"[BrowserCaptcha] ❌ Browser startup failed: {str(e)}")
                raise

    async def warmup_resident_tabs(self, project_ids: Iterable[str], limit: int | None = None) -> list[str]:
        """Preheat the shared coding tag page pool to reduce the cold start jitter of the first request."""
        normalized_project_ids: list[str] = []
        seen_projects = set()
        for raw_project_id in project_ids:
            project_id = str(raw_project_id or "").strip()
            if not project_id or project_id in seen_projects:
                continue
            seen_projects.add(project_id)
            normalized_project_ids.append(project_id)

        await self.initialize()

        try:
            warm_limit = self._max_resident_tabs if limit is None else max(1, min(self._max_resident_tabs, int(limit)))
        except Exception:
            warm_limit = self._max_resident_tabs

        warmed_slots: list[str] = []
        for index in range(warm_limit):
            warm_project_id = normalized_project_ids[index] if index < len(normalized_project_ids) else f"warmup-{index + 1}"
            slot_id, resident_info = await self._ensure_resident_tab(
                warm_project_id,
                force_create=True,
                return_slot_key=True,
            )
            if resident_info and resident_info.tab and slot_id:
                if slot_id not in warmed_slots:
                    warmed_slots.append(slot_id)
                continue
            debug_logger.log_warning(f"[BrowserCaptcha] Failed to warm up shared tab (seed={warm_project_id})")

        return warmed_slots

    # ========== Resident Mode API ==========

    async def start_resident_mode(self, project_id: str):
        """Start resident mode

        Args:
            project_id: Project ID used for resident
        """
        if not str(project_id or "").strip():
            debug_logger.log_warning("[BrowserCaptcha] Failed to start resident mode: project_id is empty")
            return

        warmed_slots = await self.warmup_resident_tabs([project_id], limit=1)
        if warmed_slots:
            debug_logger.log_info(f"[BrowserCaptcha] ✅ The shared resident coding pool has been activated (seed_project: {project_id})")
            return

        debug_logger.log_error(f"[BrowserCaptcha] Resident mode startup failed (seed_project: {project_id})")

    async def stop_resident_mode(self, project_id: str | None = None):
        """Stop resident mode

        Args:
            project_id: Specify project_id or slot_id; if None, close all resident tabs
        """
        target_slot_id = None
        if project_id:
            async with self._resident_lock:
                target_slot_id = project_id if project_id in self._resident_tabs else self._resolve_affinity_slot_locked(project_id)

        if target_slot_id:
            await self._close_resident_tab(target_slot_id)
            self._resident_error_streaks.pop(target_slot_id, None)
            debug_logger.log_info(f"[BrowserCaptcha] Shared tab closed slot={target_slot_id} (request={project_id})")
            return

        async with self._resident_lock:
            slot_ids = list(self._resident_tabs.keys())
            resident_items = list(self._resident_tabs.values())
            self._resident_tabs.clear()
            self._project_resident_affinity.clear()
            self._resident_error_streaks.clear()
            self._sync_compat_resident_state()

        for resident_info in resident_items:
            if resident_info and resident_info.tab:
                await self._close_tab_quietly(resident_info.tab)
        debug_logger.log_info(f"[BrowserCaptcha] Closed all shared resident tabs ({len(slot_ids)} in total)")

    async def _wait_for_document_ready(self, tab, retries: int = 30, interval_seconds: float = 1.0) -> bool:
        """Wait for the page document to load."""
        for _ in range(retries):
            try:
                ready_state = await self._tab_evaluate(
                    tab,
                    "document.readyState",
                    label="document.readyState",
                    timeout_seconds=2.0,
                )
                if ready_state == "complete":
                    return True
            except Exception:
                pass
            await asyncio.sleep(interval_seconds)
        return False

    def _is_server_side_flow_error(self, error_text: str) -> bool:
        error_lower = (error_text or "").lower()
        return any(keyword in error_lower for keyword in [
            "http error 500",
            "public_error",
            "internal error",
            "reason=internal",
            "reason: internal",
            "\"reason\":\"internal\"",
            "server error",
            "upstream error",
        ])

    async def _clear_tab_site_storage(self, tab) -> dict[str, Any]:
        """Clear the local storage state of the current site, but keep the cookie login state."""
        result = await self._tab_evaluate(tab, """
            (async () => {
                const summary = {
                    local_storage_cleared: false,
                    session_storage_cleared: false,
                    cache_storage_deleted: [],
                    indexed_db_deleted: [],
                    indexed_db_errors: [],
                    service_worker_unregistered: 0,
                };

                try {
                    window.localStorage.clear();
                    summary.local_storage_cleared = true;
                } catch (e) {
                    summary.local_storage_error = String(e);
                }

                try {
                    window.sessionStorage.clear();
                    summary.session_storage_cleared = true;
                } catch (e) {
                    summary.session_storage_error = String(e);
                }

                try {
                    if (typeof caches !== 'undefined') {
                        const cacheKeys = await caches.keys();
                        for (const key of cacheKeys) {
                            const deleted = await caches.delete(key);
                            if (deleted) {
                                summary.cache_storage_deleted.push(key);
                            }
                        }
                    }
                } catch (e) {
                    summary.cache_storage_error = String(e);
                }

                try {
                    if (navigator.serviceWorker) {
                        const registrations = await navigator.serviceWorker.getRegistrations();
                        for (const registration of registrations) {
                            const ok = await registration.unregister();
                            if (ok) {
                                summary.service_worker_unregistered += 1;
                            }
                        }
                    }
                } catch (e) {
                    summary.service_worker_error = String(e);
                }

                try {
                    if (typeof indexedDB !== 'undefined' && typeof indexedDB.databases === 'function') {
                        const dbs = await indexedDB.databases();
                        const names = Array.from(new Set(
                            dbs
                                .map((item) => item && item.name)
                                .filter((name) => typeof name === 'string' && name)
                        ));
                        for (const name of names) {
                            try {
                                await new Promise((resolve) => {
                                    const request = indexedDB.deleteDatabase(name);
                                    request.onsuccess = () => resolve(true);
                                    request.onerror = () => resolve(false);
                                    request.onblocked = () => resolve(false);
                                });
                                summary.indexed_db_deleted.push(name);
                            } catch (e) {
                                summary.indexed_db_errors.push(`${name}: ${String(e)}`);
                            }
                        }
                    } else {
                        summary.indexed_db_unsupported = true;
                    }
                } catch (e) {
                    summary.indexed_db_errors.push(String(e));
                }

                return summary;
            })()
        """, label="clear_tab_site_storage", timeout_seconds=15.0)
        return result if isinstance(result, dict) else {}

    async def _clear_resident_storage_and_reload(self, project_id: str) -> bool:
        """Clean the site data of the resident tab and refresh it, and try to heal it in place."""
        async with self._resident_lock:
            slot_id, resident_info = self._resolve_resident_slot_for_project_locked(project_id)

        if not resident_info or not resident_info.tab:
            debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id} has no shared tabs to clean")
            return False

        try:
            async with resident_info.solve_lock:
                cleanup_summary = await self._clear_tab_site_storage(resident_info.tab)
                debug_logger.log_warning(
                    f"[BrowserCaptcha] project_id={project_id}, slot={slot_id} Site storage has been cleaned and ready to be refreshed and restored: {cleanup_summary}"
                )

                resident_info.recaptcha_ready = False
                await self._tab_reload(
                    resident_info.tab,
                    label=f"clear_resident_reload:{slot_id or project_id}",
                )

                if not await self._wait_for_document_ready(resident_info.tab, retries=30, interval_seconds=1.0):
                    debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id}, slot={slot_id} page loading timeout after cleaning")
                    return False

                resident_info.recaptcha_ready = await self._wait_for_recaptcha(resident_info.tab)
                if resident_info.recaptcha_ready:
                    resident_info.last_used_at = time.time()
                    self._remember_project_affinity(project_id, slot_id, resident_info)
                    self._resident_error_streaks.pop(slot_id, None)
                    debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id}, slot={slot_id} Recovered after cleaning reCAPTCHA")
                    return True

                debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id}, slot={slot_id} still cannot be restored after cleaning reCAPTCHA")
                return False
        except Exception as e:
            debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id}, slot={slot_id} Clean or refresh failed: {e}")
            return False

    async def _recreate_resident_tab(self, project_id: str) -> bool:
        """Close and rebuild the resident tab."""
        slot_id, resident_info = await self._rebuild_resident_tab(project_id, return_slot_key=True)
        if resident_info is None:
            debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id} failed to rebuild shared tab page")
            return False
        debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id} Rebuilt shared tab slot={slot_id}")
        return True

    async def _restart_browser_for_project(self, project_id: str) -> bool:
        """Restart the entire nodriver browser and restore the shared coding pool."""
        async with self._resident_lock:
            restore_slots = max(1, min(self._max_resident_tabs, len(self._resident_tabs) or 1))
            restore_project_ids: list[str] = []
            seen_projects = set()
            for candidate in [project_id, *self._project_resident_affinity.keys()]:
                normalized_project_id = str(candidate or "").strip()
                if not normalized_project_id or normalized_project_id in seen_projects:
                    continue
                seen_projects.add(normalized_project_id)
                restore_project_ids.append(normalized_project_id)
                if len(restore_project_ids) >= restore_slots:
                    break

        debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id} Prepare to restart the nodriver browser to restore")
        await self._shutdown_browser_runtime(cancel_idle_reaper=False, reason=f"restart_project:{project_id}")

        warmed_slots = await self.warmup_resident_tabs(restore_project_ids, limit=restore_slots)
        if not warmed_slots:
            debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id} Failed to restore shared tabs after restarting the browser")
            return False

        slot_id, resident_info = await self._ensure_resident_tab(project_id, return_slot_key=True)
        if resident_info is None or not slot_id:
            debug_logger.log_warning(f"[BrowserCaptcha] project_id={project_id} Unable to locate available shared tabs after restarting the browser")
            return False

        self._remember_project_affinity(project_id, slot_id, resident_info)
        self._resident_error_streaks.pop(slot_id, None)
        debug_logger.log_warning(
            f"[BrowserCaptcha] project_id={project_id} The shared tab pool has been restored after the browser restarts "
            f"(slots={len(warmed_slots)}, active_slot={slot_id})"
        )
        return True

    async def report_flow_error(self, project_id: str, error_reason: str, error_message: str = ""):
        """When the upstream generates an interface exception, perform self-healing recovery on the resident tab page."""
        if not project_id:
            return

        async with self._resident_lock:
            slot_id, _ = self._resolve_resident_slot_for_project_locked(project_id)

        if not slot_id:
            return

        streak = self._resident_error_streaks.get(slot_id, 0) + 1
        self._resident_error_streaks[slot_id] = streak
        error_text = f"{error_reason or ''} {error_message or ''}".strip()
        error_lower = error_text.lower()
        debug_logger.log_warning(
            f"[BrowserCaptcha] project_id={project_id}, slot={slot_id} received upstream exception, streak={streak}, reason={error_reason}, detail={error_message[:200]}"
        )

        if not self._initialized or not self.browser:
            return

        # 403 Error: Clean cache first and then rebuild
        if "403" in error_text or "forbidden" in error_lower or "recaptcha" in error_lower:
            debug_logger.log_warning(
                f"[BrowserCaptcha] project_id={project_id} 403/reCAPTCHA error detected, clear cache and rebuild"
            )
            healed = await self._clear_resident_storage_and_reload(project_id)
            if not healed:
                await self._recreate_resident_tab(project_id)
            return

        # Server error: Determine recovery strategy based on the number of consecutive failures
        if self._is_server_side_flow_error(error_text):
            recreate_threshold = max(2, int(getattr(config, "browser_personal_recreate_threshold", 2) or 2))
            restart_threshold = max(3, int(getattr(config, "browser_personal_restart_threshold", 3) or 3))

            if streak >= restart_threshold:
                await self._restart_browser_for_project(project_id)
                return
            if streak >= recreate_threshold:
                await self._recreate_resident_tab(project_id)
                return

            healed = await self._clear_resident_storage_and_reload(project_id)
            if not healed:
                await self._recreate_resident_tab(project_id)
            return

        # Other errors: Rebuild the tab directly
        await self._recreate_resident_tab(project_id)

    async def _wait_for_recaptcha(self, tab) -> bool:
        """Wait for reCAPTCHA to load

        Returns:
            True if reCAPTCHA loaded successfully
        """
        debug_logger.log_info("[BrowserCaptcha] Inject reCAPTCHA script...")

        # Inject reCAPTCHA Enterprise script
        await self._tab_evaluate(tab, f"""
            (() => {{
                if (document.querySelector('script[src*="recaptcha"]')) return;
                const script = document.createElement('script');
                script.src = 'https://www.google.com/recaptcha/enterprise.js?render={self.website_key}';
                script.async = true;
                document.head.appendChild(script);
            }})()
        """, label="inject_recaptcha_script", timeout_seconds=5.0)

        # Wait for reCAPTCHA to load (reduce wait time)
        for i in range(15):  # Reduced to 15 times, maximum 7.5 seconds
            try:
                is_ready = await self._tab_evaluate(
                    tab,
                    "typeof grecaptcha !== 'undefined' && "
                    "typeof grecaptcha.enterprise !== 'undefined' && "
                    "typeof grecaptcha.enterprise.execute === 'function'",
                    label="check_recaptcha_ready",
                    timeout_seconds=2.5,
                )

                if is_ready:
                    debug_logger.log_info(f"[BrowserCaptcha] reCAPTCHA is ready (waited {i * 0.5}s)")
                    return True

                await tab.sleep(0.5)
            except Exception as e:
                debug_logger.log_warning(f"[BrowserCaptcha] Exception when checking reCAPTCHA: {e}")
                await tab.sleep(0.3)  # Reduce waiting time when exception occurs

        debug_logger.log_warning("[BrowserCaptcha] reCAPTCHA loading timeout")
        return False

    async def _wait_for_custom_recaptcha(
        self,
        tab,
        website_key: str,
        enterprise: bool = False,
    ) -> bool:
        """Wait for any site's reCAPTCHA to load for score testing."""
        debug_logger.log_info("[BrowserCaptcha] Detect custom reCAPTCHA...")

        ready_check = (
            "typeof grecaptcha !== 'undefined' && typeof grecaptcha.enterprise !== 'undefined' && "
            "typeof grecaptcha.enterprise.execute === 'function'"
        ) if enterprise else (
            "typeof grecaptcha !== 'undefined' && typeof grecaptcha.execute === 'function'"
        )
        script_path = "recaptcha/enterprise.js" if enterprise else "recaptcha/api.js"
        label = "Enterprise" if enterprise else "V3"

        is_ready = await self._tab_evaluate(
            tab,
            ready_check,
            label="check_custom_recaptcha_preloaded",
            timeout_seconds=2.5,
        )
        if is_ready:
            debug_logger.log_info(f"[BrowserCaptcha] Custom reCAPTCHA {label} loaded")
            return True

        debug_logger.log_info("[BrowserCaptcha] Custom reCAPTCHA not detected, injecting script...")
        await self._tab_evaluate(tab, f"""
            (() => {{
                if (document.querySelector('script[src*="recaptcha"]')) return;
                const script = document.createElement('script');
                script.src = 'https://www.google.com/{script_path}?render={website_key}';
                script.async = true;
                document.head.appendChild(script);
            }})()
        """, label="inject_custom_recaptcha_script", timeout_seconds=5.0)

        await tab.sleep(3)
        for i in range(20):
            is_ready = await self._tab_evaluate(
                tab,
                ready_check,
                label="check_custom_recaptcha_ready",
                timeout_seconds=2.5,
            )
            if is_ready:
                debug_logger.log_info(f"[BrowserCaptcha] Custom reCAPTCHA {label} loaded (waited {i * 0.5} seconds)")
                return True
            await tab.sleep(0.5)

        debug_logger.log_warning("[BrowserCaptcha] Custom reCAPTCHA loading timeout")
        return False

    async def _execute_recaptcha_on_tab(self, tab, action: str = "IMAGE_GENERATION") -> str | None:
        """Execute reCAPTCHA on the specified tab page to obtain the token

        Args:
            tab: nodriver tab page object
            action: reCAPTCHA action type (IMAGE_GENERATION or VIDEO_GENERATION)

        Returns:
            reCAPTCHA token or None
        """
        # Generate unique variable names to avoid conflicts
        ts = int(time.time() * 1000)
        token_var = f"_recaptcha_token_{ts}"
        error_var = f"_recaptcha_error_{ts}"

        execute_script = f"""
            (() => {{
                window.{token_var} = null;
                window.{error_var} = null;

                try {{
                    grecaptcha.enterprise.ready(function() {{
                        grecaptcha.enterprise.execute('{self.website_key}', {{action: '{action}'}})
                            .then(function(token) {{
                                window.{token_var} = token;
                            }})
                            .catch(function(err) {{
                                window.{error_var} = err.message || 'execute failed';
                            }});
                    }});
                }} catch (e) {{
                    window.{error_var} = e.message || 'exception';
                }}
            }})()
        """

        # Inject execution script
        await self._tab_evaluate(
            tab,
            execute_script,
            label=f"execute_recaptcha:{action}",
            timeout_seconds=5.0,
        )

        # Poll to wait for results (up to 30 seconds)
        token = None
        for _i in range(60):
            await tab.sleep(0.5)
            token = await self._tab_evaluate(
                tab,
                f"window.{token_var}",
                label=f"poll_recaptcha_token:{action}",
                timeout_seconds=2.0,
            )
            if token:
                break
            error = await self._tab_evaluate(
                tab,
                f"window.{error_var}",
                label=f"poll_recaptcha_error:{action}",
                timeout_seconds=2.0,
            )
            if error:
                debug_logger.log_error(f"[BrowserCaptcha] reCAPTCHA error: {error}")
                break

        # Clean up temporary variables
        with contextlib.suppress(BaseException):
            await self._tab_evaluate(
                tab,
                f"delete window.{token_var}; delete window.{error_var};",
                label="cleanup_recaptcha_temp_vars",
                timeout_seconds=5.0,
            )

        if token:
            debug_logger.log_info(f"[BrowserCaptcha] ✅ Token obtained successfully (length: {len(token)})")
        else:
            debug_logger.log_warning("[BrowserCaptcha] Token acquisition failed, leaving it to the upper layer to perform tab recovery")

        return token

    async def _execute_custom_recaptcha_on_tab(
        self,
        tab,
        website_key: str,
        action: str = "homepage",
        enterprise: bool = False,
    ) -> str | None:
        """Execute reCAPTCHA for any site on the specified tab."""
        ts = int(time.time() * 1000)
        token_var = f"_custom_recaptcha_token_{ts}"
        error_var = f"_custom_recaptcha_error_{ts}"
        execute_target = "grecaptcha.enterprise.execute" if enterprise else "grecaptcha.execute"

        execute_script = f"""
            (() => {{
                window.{token_var} = null;
                window.{error_var} = null;

                try {{
                    grecaptcha.ready(function() {{
                        {execute_target}('{website_key}', {{action: '{action}'}})
                            .then(function(token) {{
                                window.{token_var} = token;
                            }})
                            .catch(function(err) {{
                                window.{error_var} = err.message || 'execute failed';
                            }});
                    }});
                }} catch (e) {{
                    window.{error_var} = e.message || 'exception';
                }}
            }})()
        """

        await self._tab_evaluate(
            tab,
            execute_script,
            label=f"execute_custom_recaptcha:{action}",
            timeout_seconds=5.0,
        )

        token = None
        for _ in range(30):
            await tab.sleep(0.5)
            token = await self._tab_evaluate(
                tab,
                f"window.{token_var}",
                label=f"poll_custom_recaptcha_token:{action}",
                timeout_seconds=2.0,
            )
            if token:
                break
            error = await self._tab_evaluate(
                tab,
                f"window.{error_var}",
                label=f"poll_custom_recaptcha_error:{action}",
                timeout_seconds=2.0,
            )
            if error:
                debug_logger.log_error(f"[BrowserCaptcha] Custom reCAPTCHA error: {error}")
                break

        with contextlib.suppress(BaseException):
            await self._tab_evaluate(
                tab,
                f"delete window.{token_var}; delete window.{error_var};",
                label="cleanup_custom_recaptcha_temp_vars",
                timeout_seconds=5.0,
            )

        if token:
            post_wait_seconds = 3
            with contextlib.suppress(Exception):
                post_wait_seconds = float(getattr(config, "browser_recaptcha_settle_seconds", 3) or 3)
            if post_wait_seconds > 0:
                debug_logger.log_info(
                    f"[BrowserCaptcha] Custom reCAPTCHA completed, wait for additional {post_wait_seconds:.1f}s before returning token"
                )
                await tab.sleep(post_wait_seconds)

        return token

    async def _verify_score_on_tab(self, tab, token: str, verify_url: str) -> dict[str, Any]:
        """Directly read the scores displayed on the test page to avoid inconsistency between verify.php and the page display caliber."""
        _ = token
        _ = verify_url
        started_at = time.time()
        timeout_seconds = 25.0
        refresh_clicked = False
        last_snapshot: dict[str, Any] = {}

        with contextlib.suppress(Exception):
            timeout_seconds = float(getattr(config, "browser_score_dom_wait_seconds", 25) or 25)

        while (time.time() - started_at) < timeout_seconds:
            try:
                result = await self._tab_evaluate(tab, """
                    (() => {
                        const bodyText = ((document.body && document.body.innerText) || "")
                            .replace(/\\u00a0/g, " ")
                            .replace(/\\r/g, "");
                        const patterns = [
                            { source: "current_score", regex: /Your score is:\\s*([01](?:\\.\\d+)?)/i },
                            { source: "selected_score", regex: /Selected Score Test:[\\s\\S]{0,400}?Score:\\s*([01](?:\\.\\d+)?)/i },
                            { source: "history_score", regex: /(?:^|\\n)\\s*Score:\\s*([01](?:\\.\\d+)?)\\s*;/i },
                        ];
                        let score = null;
                        let source = "";
                        for (const item of patterns) {
                            const match = bodyText.match(item.regex);
                            if (!match) continue;
                            const parsed = Number(match[1]);
                            if (!Number.isNaN(parsed) && parsed >= 0 && parsed <= 1) {
                                score = parsed;
                                source = item.source;
                                break;
                            }
                        }
                        const uaMatch = bodyText.match(/Current User Agent:\\s*([^\\n]+)/i);
                        const ipMatch = bodyText.match(/Current IP Address:\\s*([^\\n]+)/i);
                        return {
                            score,
                            source,
                            raw_text: bodyText.slice(0, 4000),
                            current_user_agent: uaMatch ? uaMatch[1].trim() : "",
                            current_ip_address: ipMatch ? ipMatch[1].trim() : "",
                            title: document.title || "",
                            url: location.href || "",
                        };
                    })()
                """, label="verify_score_dom", timeout_seconds=10.0)
            except Exception as e:
                result = {"error": f"{type(e).__name__}: {str(e)[:200]}"}

            if isinstance(result, dict):
                last_snapshot = result
                score = result.get("score")
                if isinstance(score, (int, float)):
                    elapsed_ms = int((time.time() - started_at) * 1000)
                    return {
                        "verify_mode": "browser_page_dom",
                        "verify_elapsed_ms": elapsed_ms,
                        "verify_http_status": None,
                        "verify_result": {
                            "success": True,
                            "score": score,
                            "source": result.get("source") or "antcpt_dom",
                            "raw_text": result.get("raw_text") or "",
                            "current_user_agent": result.get("current_user_agent") or "",
                            "current_ip_address": result.get("current_ip_address") or "",
                            "page_title": result.get("title") or "",
                            "page_url": result.get("url") or "",
                        },
                    }

            if not refresh_clicked and (time.time() - started_at) >= 2:
                refresh_clicked = True
                with contextlib.suppress(Exception):
                    await self._tab_evaluate(tab, """
                        (() => {
                            const nodes = Array.from(
                                document.querySelectorAll('button, input[type="button"], input[type="submit"], a')
                            );
                            const target = nodes.find((node) => {
                                const text = (node.innerText || node.textContent || node.value || "").trim();
                                return /Refresh score now!?/i.test(text);
                            });
                            if (target) {
                                target.click();
                                return true;
                            }
                            return false;
                        })()
                    """, label="verify_score_click_refresh", timeout_seconds=5.0)

            await tab.sleep(0.5)

        elapsed_ms = int((time.time() - started_at) * 1000)
        if not isinstance(last_snapshot, dict):
            last_snapshot = {"raw": last_snapshot}

        return {
            "verify_mode": "browser_page_dom",
            "verify_elapsed_ms": elapsed_ms,
            "verify_http_status": None,
            "verify_result": {
                "success": False,
                "score": None,
                "source": "antcpt_dom_timeout",
                "raw_text": last_snapshot.get("raw_text") or "",
                "current_user_agent": last_snapshot.get("current_user_agent") or "",
                "current_ip_address": last_snapshot.get("current_ip_address") or "",
                "page_title": last_snapshot.get("title") or "",
                "page_url": last_snapshot.get("url") or "",
                "error": last_snapshot.get("error") or "Score not read in page",
            },
        }

    async def _extract_tab_fingerprint(self, tab) -> dict[str, Any] | None:
        """Extract browser fingerprint information from nodriver tab."""
        try:
            fingerprint = await self._tab_evaluate(tab, """
                () => {
                    const ua = navigator.userAgent || "";
                    const lang = navigator.language || "";
                    const uaData = navigator.userAgentData || null;
                    let secChUa = "";
                    let secChUaMobile = "";
                    let secChUaPlatform = "";

                    if (uaData) {
                        if (Array.isArray(uaData.brands) && uaData.brands.length > 0) {
                            secChUa = uaData.brands
                                .map((item) => `"${item.brand}";v="${item.version}"`)
                                .join(", ");
                        }
                        secChUaMobile = uaData.mobile ? "?1" : "?0";
                        if (uaData.platform) {
                            secChUaPlatform = `"${uaData.platform}"`;
                        }
                    }

                    return {
                        user_agent: ua,
                        accept_language: lang,
                        sec_ch_ua: secChUa,
                        sec_ch_ua_mobile: secChUaMobile,
                        sec_ch_ua_platform: secChUaPlatform,
                    };
                }
            """, label="extract_tab_fingerprint", timeout_seconds=8.0)
            if not isinstance(fingerprint, dict):
                return None

            # Personal mode currently does not configure the browser proxy separately and uses direct connection explicitly to avoid confusion with the global proxy.
            result: dict[str, Any] = {"proxy_url": None}
            for key in ("user_agent", "accept_language", "sec_ch_ua", "sec_ch_ua_mobile", "sec_ch_ua_platform"):
                value = fingerprint.get(key)
                if isinstance(value, str) and value:
                    result[key] = value
            return result
        except Exception as e:
            debug_logger.log_warning(f"[BrowserCaptcha] Failed to extract nodriver fingerprint: {e}")
            return None

    # ========== Main API ==========

    async def _get_token_raw(self, project_id: str, action: str = "IMAGE_GENERATION") -> str | None:
        """Get reCAPTCHA token

        Use a globally shared pool of coding tags. Tag pages are no longer bound one-to-one by project_id.
        Whoever gets the free tab will use it; only Session Token refresh/failure recovery will give priority to the latest mapping.

        Args:
            project_id: Flow project ID
            action: reCAPTCHA action type
                - IMAGE_GENERATION: Image generation and 2K/4K image enlargement (default)
                - VIDEO_GENERATION: Video generation and video amplification

        Returns:
            reCAPTCHA token string, returns None if acquisition fails
        """
        debug_logger.log_info(f"[BrowserCaptcha] get_token start: project_id={project_id}, action={action}, current tab number={len(self._resident_tabs)}/{self._max_resident_tabs}")

        # Make sure the browser is initialized
        await self.initialize()
        self._last_fingerprint = None

        debug_logger.log_info(
            f"[BrowserCaptcha] Start getting tabs from the shared coding pool (project: {project_id}, current: {len(self._resident_tabs)}/{self._max_resident_tabs})"
        )
        slot_id, resident_info = await self._ensure_resident_tab(project_id, return_slot_key=True)
        if resident_info is None or not slot_id:
            debug_logger.log_warning(
                f"[BrowserCaptcha] Shared tab pool is unavailable, fallback to legacy mode (project: {project_id})"
            )
            return await self._get_token_legacy(project_id, action)

        debug_logger.log_info(
            f"[BrowserCaptcha] ✅ Shared tabs available (slot={slot_id}, project={project_id}, use_count={resident_info.use_count})"
        )

        if resident_info and resident_info.tab and not resident_info.recaptcha_ready:
            debug_logger.log_warning(
                f"[BrowserCaptcha] The shared tab page is not ready and is ready to be rebuilt. cold slot={slot_id}, project={project_id}"
            )
            slot_id, resident_info = await self._rebuild_resident_tab(
                project_id,
                slot_id=slot_id,
                return_slot_key=True,
            )

        # Use the resident tab to generate token (execute outside the lock to avoid blocking)
        if resident_info and resident_info.recaptcha_ready and resident_info.tab:
            start_time = time.time()
            debug_logger.log_info(
                f"[BrowserCaptcha] Instantly generate tokens from shared resident tabs (slot={slot_id}, project={project_id}, action={action})..."
            )
            try:
                async with resident_info.solve_lock:
                    token = await self._run_with_timeout(
                        self._execute_recaptcha_on_tab(resident_info.tab, action),
                        timeout_seconds=self._solve_timeout_seconds,
                        label=f"resident_solve:{slot_id}:{project_id}:{action}",
                    )
                duration_ms = (time.time() - start_time) * 1000
                if token:
                    # Update usage time and count
                    resident_info.last_used_at = time.time()
                    resident_info.use_count += 1
                    self._remember_project_affinity(project_id, slot_id, resident_info)
                    self._resident_error_streaks.pop(slot_id, None)
                    self._last_fingerprint = await self._extract_tab_fingerprint(resident_info.tab)
                    debug_logger.log_info(
                        f"[BrowserCaptcha] ✅ Token generated successfully (slot={slot_id}, time taken {duration_ms:.0f}ms, number of uses: {resident_info.use_count})"
                    )
                    return token
                else:
                    debug_logger.log_warning(
                        f"[BrowserCaptcha] Shared tab generation failed (slot={slot_id}, project={project_id}), try to rebuild..."
                    )
            except Exception as e:
                debug_logger.log_warning(f"[BrowserCaptcha] Shared tab exception (slot={slot_id}): {e}, try to rebuild...")

            # The resident tab page is invalid, try to rebuild it.
            debug_logger.log_info(f"[BrowserCaptcha] Start rebuilding shared tab (slot={slot_id}, project={project_id})")
            slot_id, resident_info = await self._rebuild_resident_tab(
                project_id,
                slot_id=slot_id,
                return_slot_key=True,
            )
            debug_logger.log_info(f"[BrowserCaptcha] Shared tab page reconstruction completed (slot={slot_id}, project={project_id})")

            # Attempt to build immediately after rebuilding (executing outside the lock)
            if resident_info:
                try:
                    async with resident_info.solve_lock:
                        token = await self._run_with_timeout(
                            self._execute_recaptcha_on_tab(resident_info.tab, action),
                            timeout_seconds=self._solve_timeout_seconds,
                            label=f"resident_resolve_after_rebuild:{slot_id}:{project_id}:{action}",
                        )
                    if token:
                        resident_info.last_used_at = time.time()
                        resident_info.use_count += 1
                        self._remember_project_affinity(project_id, slot_id, resident_info)
                        self._resident_error_streaks.pop(slot_id, None)
                        self._last_fingerprint = await self._extract_tab_fingerprint(resident_info.tab)
                        debug_logger.log_info(f"[BrowserCaptcha] ✅ Token generated successfully after reconstruction (slot={slot_id})")
                        return token
                except Exception:
                    pass

        # Final Fallback: Use legacy mode
        debug_logger.log_warning(f"[BrowserCaptcha] All resident modes failed, fallback to traditional mode (project: {project_id})")
        legacy_token = await self._get_token_legacy(project_id, action)
        if legacy_token and slot_id:
            self._resident_error_streaks.pop(slot_id, None)
        return legacy_token

    async def _create_resident_tab(self, slot_id: str, project_id: str | None = None) -> ResidentTabInfo | None:
        """Create a shared resident coding tab

        Args:
            slot_id: shared tab slot ID
            project_id: Project ID that triggers creation, only used for logs and recent mappings

        Returns:
            ResidentTabInfo object, or None (creation failed)
        """
        try:
            # Use the Flow API address as the base page
            website_url = "https://labs.google/fx/api/auth/providers"
            debug_logger.log_info(f"[BrowserCaptcha] Create shared resident tab slot={slot_id}, seed_project={project_id}")

            async with self._resident_lock:
                existing_tabs = [info.tab for info in self._resident_tabs.values() if info.tab]

            # Get or create a tab
            tabs = self.browser.tabs
            available_tab = None

            # Find unoccupied tabs
            for tab in tabs:
                if tab not in existing_tabs:
                    available_tab = tab
                    break

            if available_tab:
                tab = available_tab
                debug_logger.log_info("[BrowserCaptcha] Reuse unoccupied tabs")
                await self._tab_get(
                    tab,
                    website_url,
                    label=f"resident_tab_get:{slot_id}",
                )
            else:
                debug_logger.log_info("[BrowserCaptcha] Create new tab")
                tab = await self._browser_get(
                    website_url,
                    label=f"resident_browser_get:{slot_id}",
                    new_tab=True,
                )

            # Wait for the page to load (reduce waiting time)
            page_loaded = False
            for retry in range(10):  # Reduce to 10 times, maximum 5 seconds
                try:
                    await asyncio.sleep(0.5)
                    ready_state = await self._tab_evaluate(
                        tab,
                        "document.readyState",
                        label=f"resident_document_ready:{slot_id}",
                        timeout_seconds=2.0,
                    )
                    if ready_state == "complete":
                        page_loaded = True
                        debug_logger.log_info("[BrowserCaptcha] page loaded")
                        break
                except Exception as e:
                    debug_logger.log_warning(f"[BrowserCaptcha] Waiting for page exception: {e}, retry {retry + 1}/10...")
                    await asyncio.sleep(0.3)  # Reduce retry interval

            if not page_loaded:
                debug_logger.log_error(f"[BrowserCaptcha] Page loading timeout (slot={slot_id}, project={project_id})")
                await self._close_tab_quietly(tab)
                return None

            # Wait for reCAPTCHA to load
            recaptcha_ready = await self._wait_for_recaptcha(tab)

            if not recaptcha_ready:
                debug_logger.log_error(f"[BrowserCaptcha] reCAPTCHA loading failed (slot={slot_id}, project={project_id})")
                await self._close_tab_quietly(tab)
                return None

            # Create a resident information object
            resident_info = ResidentTabInfo(tab, slot_id, project_id=project_id)
            resident_info.recaptcha_ready = True

            debug_logger.log_info(f"[BrowserCaptcha] ✅ Shared resident tab page created successfully (slot={slot_id}, project={project_id})")
            return resident_info

        except Exception as e:
            debug_logger.log_error(f"[BrowserCaptcha] Exception when creating shared resident tab page (slot={slot_id}, project={project_id}): {e}")
            return None

    async def _close_resident_tab(self, slot_id: str):
        """Close the shared resident tab of the specified slot

        Args:
            slot_id: shared tab slot ID
        """
        async with self._resident_lock:
            resident_info = self._resident_tabs.pop(slot_id, None)
            self._forget_project_affinity_for_slot_locked(slot_id)
            self._resident_error_streaks.pop(slot_id, None)
            self._sync_compat_resident_state()

        if resident_info and resident_info.tab:
            try:
                await self._close_tab_quietly(resident_info.tab)
                debug_logger.log_info(f"[BrowserCaptcha] Shared resident tab slot={slot_id} has been closed")
            except Exception as e:
                debug_logger.log_warning(f"[BrowserCaptcha] Exception when closing tab: {e}")

    async def invalidate_token(self, project_id: str):
        """Called when an invalid token is detected to rebuild the most recently mapped shared tab of the current project.

        Args:
            project_id: project ID
        """
        debug_logger.log_warning(
            f"[BrowserCaptcha] Token is marked as invalid (project: {project_id}), only the corresponding tab page in the shared pool is rebuilt to avoid clearing the global browser state"
        )

        # Rebuild tab
        slot_id, resident_info = await self._rebuild_resident_tab(project_id, return_slot_key=True)
        if resident_info and slot_id:
            debug_logger.log_info(f"[BrowserCaptcha] ✅ Tab has been rebuilt (project: {project_id}, slot={slot_id})")
        else:
            debug_logger.log_error(f"[BrowserCaptcha] Tab rebuild failed (project: {project_id})")

    async def _get_token_legacy(self, project_id: str, action: str = "IMAGE_GENERATION") -> str | None:
        """Traditional mode to obtain reCAPTCHA token (each time a new tab is created)

        Args:
            project_id: Flow project ID
            action: reCAPTCHA action type (IMAGE_GENERATION or VIDEO_GENERATION)

        Returns:
            reCAPTCHA token string, returns None if acquisition fails
        """
        # Make sure the browser is started
        if not self._initialized or not self.browser:
            await self.initialize()

        start_time = time.time()
        tab = None

        async with self._legacy_lock:
            try:
                website_url = "https://labs.google/fx/api/auth/providers"
                debug_logger.log_info(
                    f"[BrowserCaptcha] [Legacy] Create a separate temporary tab page to perform verification to avoid polluting the resident/custom page: {website_url}"
                )
                tab = await self._browser_get(
                    website_url,
                    label=f"legacy_browser_get:{project_id}",
                    new_tab=True,
                )

                # Wait for page to fully load (increases wait time)
                debug_logger.log_info("[BrowserCaptcha] [Legacy] Waiting for page to load...")
                await tab.sleep(3)

                # Wait for page DOM to complete
                for _ in range(10):
                    ready_state = await self._tab_evaluate(
                        tab,
                        "document.readyState",
                        label=f"legacy_document_ready:{project_id}",
                        timeout_seconds=2.0,
                    )
                    if ready_state == "complete":
                        break
                    await tab.sleep(0.5)

                # Wait for reCAPTCHA to load
                recaptcha_ready = await self._wait_for_recaptcha(tab)

                if not recaptcha_ready:
                    debug_logger.log_error("[BrowserCaptcha] [Legacy] reCAPTCHA cannot be loaded")
                    return None

                # Execute reCAPTCHA
                debug_logger.log_info(f"[BrowserCaptcha] [Legacy] Perform reCAPTCHA verification (action: {action})...")
                token = await self._run_with_timeout(
                    self._execute_recaptcha_on_tab(tab, action),
                    timeout_seconds=self._solve_timeout_seconds,
                    label=f"legacy_solve:{project_id}:{action}",
                )

                duration_ms = (time.time() - start_time) * 1000

                if token:
                    self._last_fingerprint = await self._extract_tab_fingerprint(tab)
                    debug_logger.log_info(f"[BrowserCaptcha] [Legacy] ✅ Token obtained successfully (time taken {duration_ms:.0f}ms)")
                    return token

                debug_logger.log_error("[BrowserCaptcha] [Legacy] Token acquisition failed (returns null)")
                return None

            except Exception as e:
                debug_logger.log_error(f"[BrowserCaptcha] [Legacy] Exception in obtaining token: {str(e)}")
                return None
            finally:
                # Close the legacy temporary tab (but keep the browser)
                if tab:
                    await self._close_tab_quietly(tab)

    def get_last_fingerprint(self) -> dict[str, Any] | None:
        """Returns the browser fingerprint snapshot from the last coding session."""
        if not self._last_fingerprint:
            return None
        return dict(self._last_fingerprint)

    async def _clear_browser_cache(self):
        """Clear all browser caches"""
        if not self.browser:
            return

        try:
            debug_logger.log_info("[BrowserCaptcha] Start cleaning browser cache...")

            # Clear cache using Chrome DevTools Protocol
            # Clean all types of cached data
            await self._browser_send_command(
                "Network.clearBrowserCache",
                label="clear_browser_cache",
            )

            # Clear Cookies
            await self._browser_send_command(
                "Network.clearBrowserCookies",
                label="clear_browser_cookies",
            )

            # Clean storage data (localStorage, sessionStorage, IndexedDB, etc.)
            await self._browser_send_command(
                "Storage.clearDataForOrigin",
                {
                    "origin": "https://www.google.com",
                    "storageTypes": "all"
                },
                label="clear_browser_origin_storage",
            )

            debug_logger.log_info("[BrowserCaptcha] ✅ Browser cache has been cleared")

        except Exception as e:
            debug_logger.log_warning(f"[BrowserCaptcha] Exception when clearing cache: {e}")

    async def _shutdown_browser_runtime(self, cancel_idle_reaper: bool = False, reason: str = "shutdown"):
        if cancel_idle_reaper and self._idle_reaper_task and not self._idle_reaper_task.done():
            self._idle_reaper_task.cancel()
            try:
                await self._idle_reaper_task
            except asyncio.CancelledError:
                pass
            finally:
                self._idle_reaper_task = None

        async with self._browser_lock:
            try:
                await self._shutdown_browser_runtime_locked(reason=reason)
                debug_logger.log_info(f"[BrowserCaptcha] The browser running state has been cleaned ({reason})")
            except Exception as e:
                debug_logger.log_error(f"[BrowserCaptcha] Clean up browser running state exceptions ({reason}): {str(e)}")

    async def close(self):
        """Close browser"""
        self._closing = True
        await self._shutdown_browser_runtime(cancel_idle_reaper=True, reason="service_close")
        self.db = None
        if type(self)._instance is self:
            type(self)._instance = None
        if sys.platform.startswith("win"):
            gc.collect()
            await asyncio.sleep(0)
            await asyncio.sleep(0.1)

    async def open_login_window(self):
        """Open a login window for users to manually log in to Google"""
        await self.initialize()
        await self._browser_get(
            "https://accounts.google.com/",
            label="open_login_window",
            new_tab=True,
        )
        debug_logger.log_info("[BrowserCaptcha] Please log in to your account in the opened browser. Once the login is complete, there is no need to close the browser, the script will automatically use this status the next time it is run.")
        print("Please log in to your account in the browser you open. Once the login is complete, there is no need to close the browser, the script will automatically use this status the next time it is run.")

    # ========== Session Token Refresh ==========

    async def refresh_session_token(self, project_id: str) -> str | None:
        """Get the latest Session Token from the resident tab

        Reuse shared coding tabs by refreshing the page and extracting them from cookies
        __Secure-next-auth.session-token

        Args:
            project_id: project ID, used to locate the resident tab page

        Returns:
            New Session Token, returns None if acquisition fails
        """
        # Make sure the browser is initialized
        await self.initialize()

        start_time = time.time()
        debug_logger.log_info(f"[BrowserCaptcha] Start refreshing Session Token (project: {project_id})...")

        async with self._resident_lock:
            slot_id = self._resolve_affinity_slot_locked(project_id)
            resident_info = self._resident_tabs.get(slot_id) if slot_id else None

        if resident_info is None or not slot_id:
            slot_id, resident_info = await self._ensure_resident_tab(project_id, return_slot_key=True)

        if resident_info is None or not slot_id:
            debug_logger.log_warning(f"[BrowserCaptcha] Unable to get shared resident tab for project_id={project_id}")
            return None

        if not resident_info or not resident_info.tab:
            debug_logger.log_error("[BrowserCaptcha] Unable to get resident tab page")
            return None

        tab = resident_info.tab

        try:
            async with resident_info.solve_lock:
                # Refresh the page to get the latest cookies
                debug_logger.log_info("[BrowserCaptcha] Refresh resident tab to get latest cookies...")
                resident_info.recaptcha_ready = False
                await self._run_with_timeout(
                    self._tab_reload(
                        tab,
                        label=f"refresh_session_reload:{slot_id}",
                    ),
                    timeout_seconds=self._session_refresh_timeout_seconds,
                    label=f"refresh_session_reload_total:{slot_id}",
                )

                # Wait for the page to load
                for _i in range(30):
                    await asyncio.sleep(1)
                    try:
                        ready_state = await self._tab_evaluate(
                            tab,
                            "document.readyState",
                            label=f"refresh_session_ready_state:{slot_id}",
                            timeout_seconds=2.0,
                        )
                        if ready_state == "complete":
                            break
                    except Exception:
                        pass

                resident_info.recaptcha_ready = await self._wait_for_recaptcha(tab)
                if not resident_info.recaptcha_ready:
                    debug_logger.log_warning(
                        f"[BrowserCaptcha] reCAPTCHA is not ready after refreshing Session Token (slot={slot_id})"
                    )

                # Wait extra to make sure cookies are set
                await asyncio.sleep(2)

                # Extract __Secure-next-auth.session-token from cookies
                # nodriver can obtain cookies through browser
                session_token = None

                try:
                    # Get all cookies using nodriver's cookies API
                    cookies = await self._get_browser_cookies(
                        label=f"refresh_session_get_cookies:{slot_id}",
                    )

                    for cookie in cookies:
                        if cookie.name == "__Secure-next-auth.session-token":
                            session_token = cookie.value
                            break

                except Exception as e:
                    debug_logger.log_warning(f"[BrowserCaptcha] Failed to get via cookies API: {e}, trying to get from document.cookie...")

                    # Alternative: Retrieve via JavaScript (Note: HttpOnly cookies may not be retrieved this way)
                    try:
                        all_cookies = await self._tab_evaluate(
                            tab,
                            "document.cookie",
                            label=f"refresh_session_document_cookie:{slot_id}",
                        )
                        if all_cookies:
                            for part in all_cookies.split(";"):
                                part = part.strip()
                                if part.startswith("__Secure-next-auth.session-token="):
                                    session_token = part.split("=", 1)[1]
                                    break
                    except Exception as e2:
                        debug_logger.log_error(f"[BrowserCaptcha] Document.cookie acquisition failed: {e2}")

            duration_ms = (time.time() - start_time) * 1000

            if session_token:
                resident_info.last_used_at = time.time()
                self._remember_project_affinity(project_id, slot_id, resident_info)
                self._resident_error_streaks.pop(slot_id, None)
                debug_logger.log_info(f"[BrowserCaptcha] ✅ Session Token obtained successfully (taking {duration_ms:.0f}ms)")
                return session_token
            else:
                debug_logger.log_error("[BrowserCaptcha] ❌ __Secure-next-auth.session-token cookie not found")
                return None

        except Exception as e:
            debug_logger.log_error(f"[BrowserCaptcha] Refresh Session Token exception: {str(e)}")

            # The shared tab may have expired, try rebuilding it
            slot_id, resident_info = await self._rebuild_resident_tab(project_id, slot_id=slot_id, return_slot_key=True)
            if resident_info and slot_id:
                # Try to get it again after rebuilding
                try:
                    async with resident_info.solve_lock:
                        cookies = await self._get_browser_cookies(
                            label=f"refresh_session_get_cookies_after_rebuild:{slot_id}",
                        )
                    for cookie in cookies:
                        if cookie.name == "__Secure-next-auth.session-token":
                            resident_info.last_used_at = time.time()
                            self._remember_project_affinity(project_id, slot_id, resident_info)
                            self._resident_error_streaks.pop(slot_id, None)
                            debug_logger.log_info("[BrowserCaptcha] ✅ Session Token obtained successfully after reconstruction")
                            return cookie.value
                except Exception:
                    pass

            return None

    # ========== Status Query ==========

    def is_resident_mode_active(self) -> bool:
        """Check if any resident tabs are active"""
        return len(self._resident_tabs) > 0 or self._running

    def get_resident_count(self) -> int:
        """Get the current number of resident tabs"""
        return len(self._resident_tabs)

    def get_resident_project_ids(self) -> list[str]:
        """Get the slot_id list of all currently shared resident tabs."""
        return list(self._resident_tabs.keys())

    def get_resident_project_id(self) -> str | None:
        """Get the first slot_id in the current shared pool (backwards compatible)."""
        if self._resident_tabs:
            return next(iter(self._resident_tabs.keys()))
        return self.resident_project_id

    async def _get_custom_token_raw(
        self,
        website_url: str,
        website_key: str,
        action: str = "homepage",
        enterprise: bool = False,
    ) -> str | None:
        """Execute reCAPTCHA for any site, for use in scenarios such as score testing.

        Different from the normal legacy mode, the same resident tab page will be reused here to avoid cold starting a new tab every time.
        """
        await self.initialize()
        self._last_fingerprint = None

        cache_key = f"{website_url}|{website_key}|{1 if enterprise else 0}"
        warmup_seconds = float(getattr(config, "browser_score_test_warmup_seconds", 12) or 12)
        per_request_settle_seconds = float(
            getattr(config, "browser_score_test_settle_seconds", 2.5) or 2.5
        )
        max_retries = 2

        async with self._custom_lock:
            for attempt in range(max_retries):
                start_time = time.time()
                custom_info = self._custom_tabs.get(cache_key)
                tab = custom_info.get("tab") if isinstance(custom_info, dict) else None

                try:
                    if tab is None:
                        debug_logger.log_info(f"[BrowserCaptcha] [Custom] Create resident test tab: {website_url}")
                        tab = await self._browser_get(
                            website_url,
                            label="custom_browser_get",
                            new_tab=True,
                        )
                        custom_info = {
                            "tab": tab,
                            "recaptcha_ready": False,
                            "warmed_up": False,
                            "created_at": time.time(),
                        }
                        self._custom_tabs[cache_key] = custom_info

                    page_loaded = False
                    for _ in range(20):
                        ready_state = await self._tab_evaluate(
                            tab,
                            "document.readyState",
                            label="custom_document_ready",
                            timeout_seconds=2.0,
                        )
                        if ready_state == "complete":
                            page_loaded = True
                            break
                        await tab.sleep(0.5)

                    if not page_loaded:
                        raise RuntimeError("Custom page load timeout")

                    if not custom_info.get("recaptcha_ready"):
                        recaptcha_ready = await self._wait_for_custom_recaptcha(
                            tab=tab,
                            website_key=website_key,
                            enterprise=enterprise,
                        )
                        if not recaptcha_ready:
                            raise RuntimeError("Custom reCAPTCHA cannot be loaded")
                        custom_info["recaptcha_ready"] = True

                    with contextlib.suppress(Exception):
                        await self._tab_evaluate(tab, """
                            (() => {
                                try {
                                    const body = document.body || document.documentElement;
                                    const width = window.innerWidth || 1280;
                                    const height = window.innerHeight || 720;
                                    const x = Math.max(24, Math.floor(width * 0.38));
                                    const y = Math.max(24, Math.floor(height * 0.32));
                                    const moveEvent = new MouseEvent('mousemove', {
                                        bubbles: true,
                                        clientX: x,
                                        clientY: y
                                    });
                                    const overEvent = new MouseEvent('mouseover', {
                                        bubbles: true,
                                        clientX: x,
                                        clientY: y
                                    });
                                    window.focus();
                                    window.dispatchEvent(new Event('focus'));
                                    document.dispatchEvent(moveEvent);
                                    document.dispatchEvent(overEvent);
                                    if (body) {
                                        body.dispatchEvent(moveEvent);
                                        body.dispatchEvent(overEvent);
                                    }
                                    window.scrollTo(0, Math.min(320, document.body?.scrollHeight || 320));
                                } catch (e) {}
                            })()
                        """, label="custom_pre_warm_interaction", timeout_seconds=6.0)

                    if not custom_info.get("warmed_up"):
                        if warmup_seconds > 0:
                            debug_logger.log_info(
                                f"[BrowserCaptcha] [Custom] Warm up the test page for the first time {warmup_seconds:.1f}s before executing the token"
                            )
                            with contextlib.suppress(Exception):
                                await self._tab_evaluate(tab, """
                                    (() => {
                                        try {
                                            window.scrollTo(0, Math.min(240, document.body.scrollHeight || 240));
                                            window.dispatchEvent(new Event('mousemove'));
                                            window.dispatchEvent(new Event('focus'));
                                        } catch (e) {}
                                    })()
                                """, label="custom_warmup_interaction", timeout_seconds=6.0)
                            await tab.sleep(warmup_seconds)
                        custom_info["warmed_up"] = True
                    elif per_request_settle_seconds > 0:
                        debug_logger.log_info(
                            f"[BrowserCaptcha] [Custom] Reuse the test tab, wait an additional {per_request_settle_seconds:.1f}s before execution"
                        )
                        await tab.sleep(per_request_settle_seconds)

                    debug_logger.log_info(f"[BrowserCaptcha] [Custom] Use resident test tab to perform validation (action: {action})...")
                    token = await self._execute_custom_recaptcha_on_tab(
                        tab=tab,
                        website_key=website_key,
                        action=action,
                        enterprise=enterprise,
                    )

                    duration_ms = (time.time() - start_time) * 1000
                    if token:
                        extracted_fingerprint = await self._extract_tab_fingerprint(tab)
                        if not extracted_fingerprint:
                            try:
                                fallback_ua = await self._tab_evaluate(
                                    tab,
                                    "navigator.userAgent || ''",
                                    label="custom_fallback_ua",
                                )
                                fallback_lang = await self._tab_evaluate(
                                    tab,
                                    "navigator.language || ''",
                                    label="custom_fallback_lang",
                                )
                                extracted_fingerprint = {
                                    "user_agent": fallback_ua or "",
                                    "accept_language": fallback_lang or "",
                                    "proxy_url": None,
                                }
                            except Exception:
                                extracted_fingerprint = None
                        self._last_fingerprint = extracted_fingerprint
                        debug_logger.log_info(
                            f"[BrowserCaptcha] [Custom] ✅ Resident test tab Token obtained successfully (time consuming {duration_ms:.0f}ms)"
                        )
                        return token

                    raise RuntimeError("Failed to obtain custom token (returns null)")
                except Exception as e:
                    debug_logger.log_warning(
                        f"[BrowserCaptcha] [Custom] Attempt {attempt + 1}/{max_retries} failed: {str(e)}"
                    )
                    stale_info = self._custom_tabs.pop(cache_key, None)
                    stale_tab = stale_info.get("tab") if isinstance(stale_info, dict) else None
                    if stale_tab:
                        await self._close_tab_quietly(stale_tab)
                    if attempt >= max_retries - 1:
                        debug_logger.log_error(f"[BrowserCaptcha] [Custom] Exception in obtaining token: {str(e)}")
                        return None

            return None

    async def _get_custom_score_raw(
        self,
        website_url: str,
        website_key: str,
        verify_url: str,
        action: str = "homepage",
        enterprise: bool = False,
    ) -> dict[str, Any]:
        """Obtain the token and directly verify the page score in the same resident tab."""
        token_started_at = time.time()
        token = await self._get_custom_token_raw(
            website_url=website_url,
            website_key=website_key,
            action=action,
            enterprise=enterprise,
        )
        token_elapsed_ms = int((time.time() - token_started_at) * 1000)

        if not token:
            return {
                "token": None,
                "token_elapsed_ms": token_elapsed_ms,
                "verify_mode": "browser_page",
                "verify_elapsed_ms": 0,
                "verify_http_status": None,
                "verify_result": {},
            }

        cache_key = f"{website_url}|{website_key}|{1 if enterprise else 0}"
        async with self._custom_lock:
            custom_info = self._custom_tabs.get(cache_key)
            tab = custom_info.get("tab") if isinstance(custom_info, dict) else None
            if tab is None:
                raise RuntimeError("Page score test tab does not exist")
            verify_payload = await self._verify_score_on_tab(tab, token, verify_url)

        return {
            "token": token,
            "token_elapsed_ms": token_elapsed_ms,
            **verify_payload,
        }

    def _build_browser_ref(self, project_id: str) -> str:
        normalized = str(project_id or "").strip()
        if not normalized:
            normalized = "default"
        return f"personal:{normalized}"

    def _build_custom_browser_ref(
        self,
        *,
        website_url: str,
        website_key: str,
        enterprise: bool,
    ) -> str:
        signature = hashlib.sha1(
            "\n".join(
                [
                    str(website_url or "").strip(),
                    str(website_key or "").strip(),
                    "1" if enterprise else "0",
                ]
            ).encode("utf-8")
        ).hexdigest()[:16]
        return f"personal-custom:{signature}"

    def _parse_browser_ref(self, browser_ref: int | str | None) -> str:
        if browser_ref is None:
            return ""
        if isinstance(browser_ref, int):
            return str(browser_ref)
        raw = str(browser_ref).strip()
        if raw.startswith("personal:"):
            return raw.split(":", 1)[1].strip()
        return raw

    async def get_token(
        self,
        project_id: str,
        action: str = "IMAGE_GENERATION",
        token_id: int | None = None,
    ) -> TokenAcquireResult:
        _ = token_id
        self._stats["req_total"] += 1
        started_at = time.time()
        token = await self._get_token_raw(project_id=project_id, action=action)
        elapsed_ms = int((time.time() - started_at) * 1000)
        browser_ref = self._build_browser_ref(project_id)

        if token:
            self._stats["gen_ok"] += 1
        else:
            self._stats["gen_fail"] += 1

        return TokenAcquireResult(
            token=str(token or "").strip() or None,
            browser_ref=browser_ref,
            browser_id=browser_ref,
            fingerprint=self.get_last_fingerprint(),
            source="live",
            elapsed_ms=elapsed_ms,
            browser_epoch=0,
        )

    async def get_custom_token(
        self,
        website_url: str,
        website_key: str,
        action: str = "homepage",
        enterprise: bool = False,
        token_proxy_url: str | None = None,
        captcha_type: str = "recaptcha_v3",
        is_invisible: bool = True,
    ) -> TokenAcquireResult:
        _ = token_proxy_url
        _ = captcha_type
        _ = is_invisible
        self._stats["req_total"] += 1
        started_at = time.time()
        token = await self._get_custom_token_raw(
            website_url=website_url,
            website_key=website_key,
            action=action,
            enterprise=enterprise,
        )
        elapsed_ms = int((time.time() - started_at) * 1000)
        browser_ref = self._build_custom_browser_ref(
            website_url=website_url,
            website_key=website_key,
            enterprise=enterprise,
        )

        if token:
            self._stats["gen_ok"] += 1
        else:
            self._stats["gen_fail"] += 1

        return TokenAcquireResult(
            token=str(token or "").strip() or None,
            browser_ref=browser_ref,
            browser_id=browser_ref,
            fingerprint=self.get_last_fingerprint(),
            source="live",
            elapsed_ms=elapsed_ms,
            browser_epoch=0,
        )

    async def get_custom_score(
        self,
        website_url: str,
        website_key: str,
        verify_url: str,
        action: str = "homepage",
        enterprise: bool = False,
        token_proxy_url: str | None = None,
    ) -> tuple[dict[str, Any], str]:
        _ = token_proxy_url
        payload = await self._get_custom_score_raw(
            website_url=website_url,
            website_key=website_key,
            verify_url=verify_url,
            action=action,
            enterprise=enterprise,
        )
        browser_ref = self._build_custom_browser_ref(
            website_url=website_url,
            website_key=website_key,
            enterprise=enterprise,
        )
        return payload, browser_ref

    async def get_fingerprint(self, browser_ref: int | str | None) -> dict[str, Any] | None:
        _ = browser_ref
        return self.get_last_fingerprint()

    async def report_error(self, browser_ref: int | str | None = None, error_reason: str | None = None):
        project_id = self._parse_browser_ref(browser_ref)
        error_lower = str(error_reason or "").lower()
        has_recaptcha = "recaptcha" in error_lower
        should_report = has_recaptcha and (
            "evaluation failed" in error_lower
            or "verification failed" in error_lower
            or "Authentication failed" in str(error_reason or "")
            or "failed" in error_lower
        )
        if should_report:
            self._stats["api_403"] += 1
        if should_report and project_id:
            try:
                await self.report_flow_error(project_id, error_reason or "recaptcha_evaluation_failed")
            except Exception as e:
                debug_logger.log_warning(f"[BrowserCaptcha] Personal report_flow_error failed: {e}")

    async def report_request_finished(self, browser_ref: int | str | None = None):
        project_id = self._parse_browser_ref(browser_ref)
        debug_logger.log_info(
            f"[BrowserCaptcha] personal request finished; project={project_id or 'unknown'} resident_tabs={len(self._resident_tabs)}"
        )

    async def warmup_browser_slots(self):
        await self.reload_config()
        project_id = str(getattr(config, "browser_auto_warm_project_id", "") or "").strip()
        warmup_limit = max(1, int(getattr(config, "personal_max_resident_tabs", 1) or 1))
        project_ids = [project_id] if project_id else []
        await self.warmup_resident_tabs(project_ids, limit=warmup_limit)

    async def refresh_warmup_settings(self):
        await self.reload_config()
        await self.warmup_browser_slots()

    async def reload_browser_count(self):
        await self.reload_config()

    async def prime_token_pool(
        self,
        project_id: str,
        action: str = "IMAGE_GENERATION",
        token_id: int = None,
    ) -> dict[str, Any]:
        _ = action
        _ = token_id
        warmed_slots = await self.warmup_resident_tabs([project_id], limit=1)
        return {
            "project_id": project_id,
            "action": action,
            "current_depth": len(warmed_slots),
            "target_depth": max(1, int(getattr(config, "personal_max_resident_tabs", 1) or 1)),
            "pool_enabled": True,
        }

    def get_stats(self):
        busy_browser_count = sum(
            1
            for resident_info in self._resident_tabs.values()
            if resident_info is not None and resident_info.solve_lock.locked()
        )
        configured_browser_count = max(1, int(getattr(config, "personal_max_resident_tabs", 1) or 1))
        resident_count = len(self._resident_tabs)
        return {
            "total_solve_count": self._stats["gen_ok"],
            "total_error_count": self._stats["gen_fail"],
            "risk_403_count": self._stats["api_403"],
            "browser_count": resident_count,
            "configured_browser_count": configured_browser_count,
            "busy_browser_count": busy_browser_count,
            "idle_browser_count": max(configured_browser_count - busy_browser_count, 0),
            "standby_token_count": 0,
            "project_affinity_count": len(self._project_resident_affinity),
            "resident_tab_count": resident_count,
            "browsers": [],
        }
