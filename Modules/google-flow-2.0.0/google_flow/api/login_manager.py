"""
Login session manager for the setup UI.

Manages a persistent Playwright browser context for Google Flow
login and session-token extraction.
"""

from __future__ import annotations

import asyncio
import ctypes
import os
import shutil
from pathlib import Path
from typing import Any

from fastapi import HTTPException

from google_flow.constants import (
    SESSION_COOKIE_CHUNK_PREFIXES,
    SESSION_COOKIE_NAMES,
)
from google_flow.logging import get_logger

logger = get_logger(__name__)

try:
    from playwright.async_api import async_playwright

    HAS_PLAYWRIGHT = True
except Exception:
    HAS_PLAYWRIGHT = False

PROFILE_ROOT = Path.home() / ".google-flow" / "browser-profile"

# ── Windows Window Management ───────────────────────────────────────

if os.name == "nt":
    from ctypes import wintypes

    SW_HIDE = 0
    SW_SHOW = 5
    SW_MINIMIZE = 6
    SW_RESTORE = 9


def set_windows_browser_visibility(visible: bool) -> bool:
    """Show/hide/minimise the browser window on Windows."""
    if os.name != "nt":
        return False

    user32 = ctypes.windll.user32  # type: ignore[attr-defined]
    titles: list[tuple[int, str]] = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
    def enum_proc(hwnd: Any, _lparam: Any) -> bool:
        length = user32.GetWindowTextLengthW(hwnd)
        if length <= 0:
            return True
        buffer = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buffer, length + 1)
        title = buffer.value.strip()
        if not title:
            return True
        lowered = title.lower()
        if "flow" in lowered and any(
            kw in lowered for kw in ("chrome", "edge", "google")
        ):
            titles.append((hwnd, title))
        return True

    user32.EnumWindows(enum_proc, 0)
    if not titles:
        return False

    changed = False
    for hwnd, _ in titles:
        if visible:
            user32.ShowWindow(hwnd, SW_RESTORE)
            user32.ShowWindow(hwnd, SW_SHOW)
            user32.SetForegroundWindow(hwnd)
        else:
            user32.ShowWindow(hwnd, SW_MINIMIZE)
        changed = True
    return changed


# ── Login Manager ───────────────────────────────────────────────────

class LoginSessionManager:
    """Manages a persistent Playwright browser for Google login."""

    def __init__(self) -> None:
        self._lock = asyncio.Lock()
        self._playwright: Any | None = None
        self._context: Any | None = None
        self._page: Any | None = None
        self._last_cookie_error = ""
        self._window_visible = True

    async def open(self, flow_url: str) -> None:
        """Open (or reuse) a browser at *flow_url*."""
        async with self._lock:
            if self._context is not None:
                if self._page is None:
                    self._page = (
                        self._context.pages[0]
                        if self._context.pages
                        else await self._context.new_page()
                    )
                await self._page.goto(
                    flow_url, wait_until="domcontentloaded", timeout=60000
                )
                self._window_visible = True
                await self._set_window_visibility(True)
                return

            PROFILE_ROOT.mkdir(parents=True, exist_ok=True)
            self._playwright = await async_playwright().start()
            self._context = (
                await self._playwright.chromium.launch_persistent_context(
                    user_data_dir=str(PROFILE_ROOT),
                    headless=False,
                    channel="chrome",
                    args=[
                        "--disable-blink-features=AutomationControlled",
                        "--no-default-browser-check",
                        "--disable-dev-shm-usage",
                    ],
                    viewport={"width": 1440, "height": 900},
                )
            )
            self._page = (
                self._context.pages[0]
                if self._context.pages
                else await self._context.new_page()
            )
            await self._page.goto(
                flow_url, wait_until="domcontentloaded", timeout=60000
            )
            self._window_visible = True

    async def _set_window_visibility(self, visible: bool) -> bool:
        try:
            changed = set_windows_browser_visibility(visible)
            if changed:
                self._window_visible = visible
            return changed
        except Exception:
            return False

    # ── Cookie Extraction ───────────────────────────────────────────

    def _extract_st_from_cookie_list(
        self, cookies: list[dict[str, Any]]
    ) -> str | None:
        """Extract session token from cookie list."""
        # Exact match
        for name in SESSION_COOKIE_NAMES:
            for cookie in cookies:
                if cookie.get("name") == name and cookie.get("value"):
                    return str(cookie["value"])

        # Chunked cookies
        for prefix in SESSION_COOKIE_CHUNK_PREFIXES:
            chunks: list[tuple[int, str]] = []
            for cookie in cookies:
                cname = str(cookie.get("name") or "")
                cvalue = str(cookie.get("value") or "")
                if not cvalue or not cname.startswith(prefix):
                    continue
                suffix = cname[len(prefix) :]
                if suffix.isdigit():
                    chunks.append((int(suffix), cvalue))
            if chunks:
                chunks.sort(key=lambda item: item[0])
                return "".join(v for _, v in chunks)

        return None

    async def _read_flow_session_token(self) -> str | None:
        """Try multiple cookie queries to find the session token."""
        if self._context is None:
            return None

        last_error = None
        queries = [
            (),
            ("https://labs.google/",),
            ("https://labs.google/fx/tools/flow",),
            ("https://labs.google/fx", "https://labs.google/"),
        ]
        for query in queries:
            try:
                cookies = await self._context.cookies(*query)
                token = self._extract_st_from_cookie_list(cookies)
                if token:
                    self._last_cookie_error = ""
                    return token
            except Exception as exc:
                last_error = exc

        if last_error is not None:
            self._last_cookie_error = str(last_error)
        return None

    async def extract_st(self) -> str:
        """Extract the session token, raising on failure."""
        async with self._lock:
            if self._context is None:
                raise HTTPException(
                    status_code=400, detail="Login browser is not open"
                )
            token = await self._read_flow_session_token()
            if token:
                return token
            raise HTTPException(
                status_code=400,
                detail="Flow session token not found. "
                "Please confirm Google Flow login is complete.",
            )

    async def has_st_cookie(self) -> bool:
        async with self._lock:
            if self._context is None:
                return False
            try:
                return bool(await self._read_flow_session_token())
            except Exception as exc:
                self._last_cookie_error = str(exc)
                return False

    async def get_cookie_error(self) -> str:
        async with self._lock:
            return self._last_cookie_error

    # ── Window Visibility ───────────────────────────────────────────

    async def set_window_visible(self, visible: bool) -> bool:
        async with self._lock:
            return await self._set_window_visibility(visible)

    async def is_window_visible(self) -> bool:
        async with self._lock:
            return self._window_visible if self._context is not None else False

    # ── Lifecycle ───────────────────────────────────────────────────

    async def reopen_fresh(self, flow_url: str) -> None:
        """Close the current browser (deleting profile) and reopen."""
        await self.close(delete_profile=True)
        await self.open(flow_url)

    async def close(self, delete_profile: bool = False) -> None:
        """Close the browser and optionally delete the profile."""
        async with self._lock:
            if self._context is not None:
                await self._context.close()
                self._context = None
                self._page = None
            if self._playwright is not None:
                await self._playwright.stop()
                self._playwright = None
            self._last_cookie_error = ""
            self._window_visible = True
        if delete_profile and PROFILE_ROOT.exists():
            shutil.rmtree(PROFILE_ROOT, ignore_errors=True)

    async def is_open(self) -> bool:
        async with self._lock:
            return self._context is not None


# Module-level singleton
login_manager = LoginSessionManager()
