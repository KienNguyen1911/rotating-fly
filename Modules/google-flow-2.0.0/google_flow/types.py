"""
Typed data models and enumerations for google_flow.

All public-facing data structures are Pydantic models so they get
automatic validation, serialisation, and IDE autocompletion.
"""

from __future__ import annotations

from enum import Enum

from pydantic import BaseModel, Field

# ── Enumerations ────────────────────────────────────────────────────

class AspectRatio(str, Enum):
    """Supported image aspect ratios."""

    LANDSCAPE = "IMAGE_ASPECT_RATIO_LANDSCAPE"
    PORTRAIT = "IMAGE_ASPECT_RATIO_PORTRAIT"
    SQUARE = "IMAGE_ASPECT_RATIO_SQUARE"
    LANDSCAPE_FOUR_THREE = "IMAGE_ASPECT_RATIO_LANDSCAPE_FOUR_THREE"
    PORTRAIT_THREE_FOUR = "IMAGE_ASPECT_RATIO_PORTRAIT_THREE_FOUR"


class UpscaleResolution(str, Enum):
    """Upscale target resolutions."""

    NONE = "none"
    HD = "2k"       # alias for 2K
    TWO_K = "2k"
    FOUR_K = "4k"

    @classmethod
    def from_string(cls, value: str) -> UpscaleResolution:
        """Parse user-supplied strings like 'hd', '2k', '4k', 'none'."""
        normalised = value.strip().lower()
        if normalised in {"hd", "2k"}:
            return cls.TWO_K
        if normalised == "4k":
            return cls.FOUR_K
        return cls.NONE

    @property
    def api_value(self) -> str | None:
        """Return the API-level resolution string, or *None* for no upscale."""
        if self.value == "2k":
            return "UPSAMPLE_IMAGE_RESOLUTION_2K"
        if self.value == "4k":
            return "UPSAMPLE_IMAGE_RESOLUTION_4K"
        return None


class CaptchaMethod(str, Enum):
    """How to acquire reCAPTCHA tokens."""

    DISABLED = "disabled"
    PERSONAL = "personal"

    @classmethod
    def from_string(cls, value: str) -> CaptchaMethod:
        normalised = value.strip().lower()
        if normalised in {"", "none", "off", "disabled"}:
            return cls.DISABLED
        if normalised == "personal":
            return cls.PERSONAL
        raise ValueError(f"Unsupported captcha method: {value!r}")


class Orientation(str, Enum):
    """Simplified orientation for model resolution."""

    LANDSCAPE = "landscape"
    PORTRAIT = "portrait"
    SQUARE = "square"
    FOUR_THREE = "four-three"
    THREE_FOUR = "three-four"
    ULTRAWIDE = "ultrawide"


# ── Model Configuration ────────────────────────────────────────────

class ModelConfig(BaseModel):
    """Configuration for a single image generation model."""

    model_name: str = Field(..., description="Internal API model identifier (e.g. GEM_PIX_2)")
    aspect_ratio: AspectRatio = Field(..., description="Image aspect ratio")
    description: str = Field(..., description="Human-readable description")


# ── Generation Results ──────────────────────────────────────────────

class GenerationResult(BaseModel):
    """Result of an image generation request."""

    image_url: str | None = Field(None, description="FIFE URL of the generated image")
    media_id: str | None = Field(None, description="Media ID for upscaling")
    session_id: str = Field(..., description="Session ID used for the request")
    saved_path: str | None = Field(None, description="Local path if saved to disk")

    @property
    def is_saved(self) -> bool:
        return self.saved_path is not None


class CreditsInfo(BaseModel):
    """Account credit information."""

    credits: int = Field(0, description="Remaining credits")
    tier: str = Field("PAYGATE_TIER_NOT_PAID", description="User paygate tier")


class TokenInfo(BaseModel):
    """Authentication token state."""

    st: str = Field("", description="Session Token")
    at: str = Field("", description="Access Token")
    at_expires: str = Field("", description="AT expiry timestamp")
    project_id: str = Field("", description="Flow project ID")
    user_paygate_tier: str = Field("PAYGATE_TIER_NOT_PAID", description="Paygate tier")

    @property
    def has_session(self) -> bool:
        return bool(self.st)

    @property
    def has_access_token(self) -> bool:
        return bool(self.at)

    @property
    def has_project(self) -> bool:
        return bool(self.project_id)
