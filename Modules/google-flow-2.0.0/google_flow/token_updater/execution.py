"""Single browser execution gate."""
import asyncio
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from datetime import datetime
from typing import Any

_ACTION_LABELS = {
    "auto_login": "Automatic login",
    "launch_browser": "Start browser login",
    "close_browser": "Close browser",
    "check_login": "Check login status",
    "import_cookies": "Import session data",
    "export_cookies": "Export cookies",
    "extract_token": "Extract session token",
    "sync_profile": "Sync accounts",
    "sync_all": "Sync all accounts",
    "delete_profile": "Delete account",
}


class ExecutionGate:
    """Unified serialization requires exclusive browser operation."""

    def __init__(self) -> None:
        self._lock = asyncio.Lock()
        self._current: dict[str, Any] | None = None

    @asynccontextmanager
    async def hold(
        self,
        action: str,
        *,
        profile_id: int | None = None,
        profile_name: str = "",
        source: str = "manual",
    ) -> AsyncIterator[dict[str, Any]]:
        await self._lock.acquire()
        self._current = {
            "action": action,
            "label": _ACTION_LABELS.get(action, action),
            "profile_id": profile_id,
            "profile_name": profile_name or "",
            "source": source,
            "started_at": datetime.now().isoformat(),
        }
        try:
            yield self._current
        finally:
            self._current = None
            self._lock.release()

    def get_status(self) -> dict[str, Any]:
        current = dict(self._current) if self._current else None
        return {
            "busy": current is not None,
            "current": current,
        }


execution_gate = ExecutionGate()
