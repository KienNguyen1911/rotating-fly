"""
Extended Login Session Manager (ExtendedLoginSessionManager).

Extends google_flow.api.login_manager.LoginSessionManager with robust
fallback cookie extraction without modifying the base repository.
"""

from __future__ import annotations

from typing import Any

from google_flow.api.login_manager import (
    SESSION_COOKIE_CHUNK_PREFIXES,
    SESSION_COOKIE_NAMES,
    LoginSessionManager,
)


class ExtendedLoginSessionManager(LoginSessionManager):
    """Extended LoginSessionManager with enhanced cookie extraction."""

    def _extract_st_from_cookie_list(
        self, cookies: list[dict[str, Any]]
    ) -> str | None:
        token = super()._extract_st_from_cookie_list(cookies)
        if token:
            return token

        # Fallback match: any cookie with 'session-token' in name
        for cookie in cookies:
            cname = str(cookie.get("name") or "").lower()
            cvalue = str(cookie.get("value") or "")
            if "session-token" in cname and len(cvalue) > 20:
                return cvalue

        return None


extended_login_manager = ExtendedLoginSessionManager()
