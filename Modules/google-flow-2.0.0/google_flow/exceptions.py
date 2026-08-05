"""
Flow CLI exception hierarchy.

All exceptions raised by google_flow inherit from :class:`FlowError`,
making it easy for callers to catch *any* library error with a single
``except FlowError`` block while still being able to handle specific
failure modes individually.
"""

from __future__ import annotations


class FlowError(Exception):
    """Base exception for all google_flow errors."""

    def __init__(self, message: str, *, detail: str | None = None) -> None:
        self.detail = detail
        super().__init__(message)


# ── Authentication ──────────────────────────────────────────────────

class FlowAuthError(FlowError):
    """Authentication or authorisation failure (HTTP 401/403)."""


class FlowTokenExpiredError(FlowAuthError):
    """The access token has expired and needs to be refreshed."""


# ── Rate Limiting ───────────────────────────────────────────────────

class FlowRateLimitError(FlowError):
    """The API returned HTTP 429 — too many requests."""

    def __init__(
        self,
        message: str = "Rate limit exceeded",
        *,
        retry_after: float | None = None,
        detail: str | None = None,
    ) -> None:
        self.retry_after = retry_after
        super().__init__(message, detail=detail)


# ── Server / Network ───────────────────────────────────────────────

class FlowServerError(FlowError):
    """Remote server error (HTTP 5xx)."""


class FlowNetworkError(FlowError):
    """Connection, DNS, or timeout error."""


class FlowTimeoutError(FlowNetworkError):
    """Request timed out."""


# ── Configuration ──────────────────────────────────────────────────

class FlowConfigError(FlowError):
    """Invalid or missing configuration."""


# ── Models ─────────────────────────────────────────────────────────

class FlowModelNotFoundError(FlowError):
    """The requested image model is not registered."""

    def __init__(self, model_id: str) -> None:
        self.model_id = model_id
        super().__init__(f"Unknown model: {model_id}")


# ── Captcha ────────────────────────────────────────────────────────

class FlowCaptchaError(FlowError):
    """reCAPTCHA token acquisition failed."""


# ── Image Generation ───────────────────────────────────────────────

class FlowGenerationError(FlowError):
    """Image generation failed after all retries."""


class FlowUpscaleError(FlowError):
    """Image upscaling (2K/4K) failed."""


class FlowUploadError(FlowError):
    """Reference image upload failed."""
