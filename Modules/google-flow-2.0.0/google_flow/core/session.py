"""
Session and token lifecycle management.

Handles ST → AT conversion, AT refresh, project creation, and
token persistence — all extracted from the old FlowClient to
separate *authentication* concerns from *API call* concerns.
"""

from __future__ import annotations

import json
from typing import TYPE_CHECKING, Any

from google_flow.exceptions import FlowAuthError
from google_flow.logging import get_logger
from google_flow.types import TokenInfo

if TYPE_CHECKING:
    from pathlib import Path

logger = get_logger(__name__)


class SessionManager:
    """Manages authentication tokens and project lifecycle.

    This class owns the :class:`TokenInfo` state and is responsible
    for refreshing the access token when it expires.

    Parameters
    ----------
    token:
        Current token state.
    token_path:
        Path to the ``token.json`` file for persistence.
    """

    def __init__(
        self,
        token: TokenInfo,
        token_path: Path,
    ) -> None:
        self.token = token
        self.token_path = token_path

    # ── Persistence ─────────────────────────────────────────────────

    def save(self) -> None:
        """Persist current token state to disk."""
        self.token_path.parent.mkdir(parents=True, exist_ok=True)
        with open(self.token_path, "w", encoding="utf-8") as fh:
            json.dump(self.token.model_dump(), fh, indent=2, ensure_ascii=False)
        logger.debug("Token saved → %s", self.token_path)

    @classmethod
    def load(cls, token_path: Path) -> SessionManager:
        """Load token state from disk."""
        token = TokenInfo()
        if token_path.exists():
            try:
                with open(token_path, encoding="utf-8") as fh:
                    data = json.load(fh)
                token = TokenInfo.model_validate(data)
            except Exception as exc:
                logger.warning("Failed to load token file: %s", exc)
        return cls(token=token, token_path=token_path)

    # ── Token Operations ────────────────────────────────────────────

    def require_session_token(self) -> str:
        """Return the ST or raise :class:`FlowAuthError`."""
        if not self.token.has_session:
            raise FlowAuthError(
                "Session Token (ST) not configured.  "
                "Run 'google-flow login --st <token>' first."
            )
        return self.token.st

    @property
    def access_token(self) -> str | None:
        """Return the AT if available."""
        return self.token.at or None

    def update_from_session_response(self, data: dict[str, Any]) -> str:
        """Update token state from an ST→AT API response.

        Returns the new access token.
        """
        self.token.at = data.get("access_token", "")
        self.token.at_expires = data.get("expires", "")
        if "user" in data:
            self.token.user_paygate_tier = data["user"].get(
                "userPaygateTier", self.token.user_paygate_tier
            )
        if not self.token.at:
            raise FlowAuthError("Failed to obtain Access Token from session response")
        self.save()
        return self.token.at

    def update_project(self, project_id: str) -> None:
        """Set the project ID and persist."""
        self.token.project_id = project_id
        self.save()

    def clear(self) -> None:
        """Reset all token state."""
        self.token = TokenInfo()
        self.save()
