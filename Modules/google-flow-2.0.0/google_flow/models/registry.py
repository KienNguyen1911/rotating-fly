"""
Image model registry.

Provides a typed, validated catalogue of supported models and
helper functions for resolving model names with orientation/size
parameters.
"""

from __future__ import annotations

from google_flow.constants import DEFAULT_MODEL_ID
from google_flow.exceptions import FlowModelNotFoundError
from google_flow.logging import get_logger
from google_flow.types import AspectRatio, ModelConfig

logger = get_logger(__name__)

# ── Model Catalogue ─────────────────────────────────────────────────

IMAGE_MODELS: dict[str, ModelConfig] = {
    # Gemini 2.5 Flash
    "gemini-2.5-flash-image-landscape": ModelConfig(
        model_name="GEM_PIX",
        aspect_ratio=AspectRatio.LANDSCAPE,
        description="Gemini 2.5 Flash - landscape",
    ),
    "gemini-2.5-flash-image-portrait": ModelConfig(
        model_name="GEM_PIX",
        aspect_ratio=AspectRatio.PORTRAIT,
        description="Gemini 2.5 Flash - portrait",
    ),
    # Gemini 3.0 Pro
    "gemini-3.0-pro-image-landscape": ModelConfig(
        model_name="GEM_PIX_2",
        aspect_ratio=AspectRatio.LANDSCAPE,
        description="Gemini 3.0 Pro - landscape",
    ),
    "gemini-3.0-pro-image-portrait": ModelConfig(
        model_name="GEM_PIX_2",
        aspect_ratio=AspectRatio.PORTRAIT,
        description="Gemini 3.0 Pro - portrait",
    ),
    "gemini-3.0-pro-image-square": ModelConfig(
        model_name="GEM_PIX_2",
        aspect_ratio=AspectRatio.SQUARE,
        description="Gemini 3.0 Pro - square",
    ),
    "gemini-3.0-pro-image-four-three": ModelConfig(
        model_name="GEM_PIX_2",
        aspect_ratio=AspectRatio.LANDSCAPE_FOUR_THREE,
        description="Gemini 3.0 Pro - 4:3 landscape",
    ),
    "gemini-3.0-pro-image-three-four": ModelConfig(
        model_name="GEM_PIX_2",
        aspect_ratio=AspectRatio.PORTRAIT_THREE_FOUR,
        description="Gemini 3.0 Pro - 3:4 portrait",
    ),
    # Imagen 4.0
    "imagen-4.0-generate-preview-landscape": ModelConfig(
        model_name="IMAGEN_3_5",
        aspect_ratio=AspectRatio.LANDSCAPE,
        description="Imagen 4.0 - landscape",
    ),
    "imagen-4.0-generate-preview-portrait": ModelConfig(
        model_name="IMAGEN_3_5",
        aspect_ratio=AspectRatio.PORTRAIT,
        description="Imagen 4.0 - portrait",
    ),
    # Gemini 3.1 Flash
    "gemini-3.1-flash-image-landscape": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.LANDSCAPE,
        description="Gemini 3.1 Flash - landscape",
    ),
    "gemini-3.1-flash-image-portrait": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.PORTRAIT,
        description="Gemini 3.1 Flash - portrait",
    ),
    "gemini-3.1-flash-image-square": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.SQUARE,
        description="Gemini 3.1 Flash - square",
    ),
    "gemini-3.1-flash-image-four-three": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.LANDSCAPE_FOUR_THREE,
        description="Gemini 3.1 Flash - 4:3 landscape",
    ),
    "gemini-3.1-flash-image-three-four": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.PORTRAIT_THREE_FOUR,
        description="Gemini 3.1 Flash - 3:4 portrait",
    ),
    # Nano Banana 2
    "nano-banana-2-landscape": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.LANDSCAPE,
        description="Nano Banana 2 - landscape",
    ),
    "nano-banana-2-portrait": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.PORTRAIT,
        description="Nano Banana 2 - portrait",
    ),
    "nano-banana-2-square": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.SQUARE,
        description="Nano Banana 2 - square",
    ),
    "nano-banana-2-ultrawide": ModelConfig(
        model_name="NARWHAL",
        aspect_ratio=AspectRatio.LANDSCAPE,
        description="Nano Banana 2 - 21:9 ultrawide",
    ),
    # Nano Banana Pro
    "nano-banana-pro-landscape": ModelConfig(
        model_name="GEM_PIX_2",
        aspect_ratio=AspectRatio.LANDSCAPE,
        description="Nano Banana Pro - landscape",
    ),
    "nano-banana-pro-portrait": ModelConfig(
        model_name="GEM_PIX_2",
        aspect_ratio=AspectRatio.PORTRAIT,
        description="Nano Banana Pro - portrait",
    ),
    "nano-banana-pro-square": ModelConfig(
        model_name="GEM_PIX_2",
        aspect_ratio=AspectRatio.SQUARE,
        description="Nano Banana Pro - square",
    ),
}

DEFAULT_MODEL = DEFAULT_MODEL_ID

# ── Model Family Variants (for orientation-based resolution) ────────

_MODEL_FAMILIES: dict[str, dict[str, str]] = {
    "gemini-3.1-flash-image": {
        "landscape": "gemini-3.1-flash-image-landscape",
        "portrait": "gemini-3.1-flash-image-portrait",
        "square": "gemini-3.1-flash-image-square",
    },
    "gemini-3.0-pro-image": {
        "landscape": "gemini-3.0-pro-image-landscape",
        "portrait": "gemini-3.0-pro-image-portrait",
        "square": "gemini-3.0-pro-image-square",
    },
    "imagen-4.0-generate-preview": {
        "landscape": "imagen-4.0-generate-preview-landscape",
        "portrait": "imagen-4.0-generate-preview-portrait",
        "square": "imagen-4.0-generate-preview-landscape",
    },
    "nano-banana-2": {
        "landscape": "nano-banana-2-landscape",
        "portrait": "nano-banana-2-portrait",
        "square": "nano-banana-2-square",
        "ultrawide": "nano-banana-2-ultrawide",
    },
    "nano-banana-pro": {
        "landscape": "nano-banana-pro-landscape",
        "portrait": "nano-banana-pro-portrait",
        "square": "nano-banana-pro-square",
    },
}

_FAMILY_ALIASES: dict[str, str] = {
    "nanobanana2": "nano-banana-2",
    "nanobananatwo": "nano-banana-2",
    "nanobananapro": "nano-banana-pro",
}


# ── Public API ──────────────────────────────────────────────────────

def get_model_config(model_id: str) -> ModelConfig:
    """Return the configuration for *model_id*.

    Raises :class:`FlowModelNotFoundError` if the model is unknown.
    """
    if model_id not in IMAGE_MODELS:
        raise FlowModelNotFoundError(model_id)
    return IMAGE_MODELS[model_id]


def list_models() -> dict[str, ModelConfig]:
    """Return a copy of the full model catalogue."""
    return dict(IMAGE_MODELS)


def list_model_ids() -> list[str]:
    """Return all registered model IDs."""
    return list(IMAGE_MODELS.keys())


def print_models() -> None:
    """Print a human-readable model listing to stdout."""
    print("\nAvailable image generation models:")
    print("-" * 60)
    for model_id, cfg in IMAGE_MODELS.items():
        default_mark = " (default)" if model_id == DEFAULT_MODEL else ""
        print(f"  {model_id}{default_mark}")
        print(f"    - {cfg.description}")
    print("-" * 60)


# ── Orientation-based Resolution ────────────────────────────────────

def _normalize_size(size: str | None) -> str | None:
    """Map a WxH size string to an orientation keyword."""
    if not size:
        return None
    value = size.strip().lower().replace(" ", "")
    if value in {"1k", "2k", "4k"}:
        return None
    mapping = {
        "1024x1024": "square",
        "1024x1536": "portrait",
        "1536x1024": "landscape",
        "1024x768": "landscape",
        "768x1024": "portrait",
        "2048x2048": "square",
        "2048x3072": "portrait",
        "3072x2048": "landscape",
        "4096x4096": "square",
        "4096x6144": "portrait",
        "6144x4096": "landscape",
    }
    return mapping.get(value)


def _normalize_aspect_ratio(aspect_ratio: str | None) -> str | None:
    """Map a ratio string (e.g. ``16:9``) to an orientation keyword."""
    if not aspect_ratio:
        return None
    value = aspect_ratio.strip().lower().replace(" ", "")
    mapping = {
        "1:1": "square",
        "16:9": "landscape",
        "9:16": "portrait",
        "4:3": "landscape",
        "3:4": "portrait",
        "21:9": "ultrawide",
        "9:21": "portrait",
    }
    return mapping.get(value)


def detect_orientation(
    size: str | None = None,
    aspect_ratio: str | None = None,
) -> str:
    """Determine orientation from size/ratio, defaulting to ``landscape``."""
    return _normalize_aspect_ratio(aspect_ratio) or _normalize_size(size) or "landscape"


def get_model_family(requested: str) -> dict[str, str] | None:
    """Return the orientation→model-id mapping for a model family."""
    normalised = "".join(ch for ch in requested.lower() if ch.isalnum())
    requested = _FAMILY_ALIASES.get(normalised, requested)

    if requested in _MODEL_FAMILIES:
        return _MODEL_FAMILIES[requested]

    # Check if it's an exact model ID that belongs to a family
    for family_map in _MODEL_FAMILIES.values():
        if requested in family_map.values():
            return family_map

    return None


def resolve_model(
    model: str | None = None,
    size: str | None = None,
    aspect_ratio: str | None = None,
) -> str:
    """Resolve a model name + size/ratio into a concrete model ID.

    Raises :class:`FlowModelNotFoundError` on unknown models.
    """
    requested = (model or DEFAULT_MODEL).strip()
    orientation = detect_orientation(size, aspect_ratio)

    family = get_model_family(requested)
    if family:
        if orientation == "ultrawide" and "ultrawide" not in family:
            raise FlowModelNotFoundError(
                f"{requested} (does not support 21:9)"
            )
        return family.get(orientation, family.get("landscape", requested))

    if requested in IMAGE_MODELS:
        if orientation == "ultrawide":
            raise FlowModelNotFoundError(
                f"{requested} (does not support 21:9)"
            )
        return requested

    raise FlowModelNotFoundError(requested)
