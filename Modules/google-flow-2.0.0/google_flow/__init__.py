"""
Flow CLI — Professional image generation library for Google Flow.

Usage::

    from google_flow import FlowClient, ImageGenerator

    async with FlowClient() as client:
        ...
"""

from google_flow._version import __version__

# Configuration
from google_flow.config import AppConfig, get_config, set_config

# Core classes — the primary public API
from google_flow.core.client import FlowClient
from google_flow.core.generator import ImageGenerator
from google_flow.core.session import SessionManager
from google_flow.core.sdk import FlowSDK
from google_flow.captcha import (
    CaptchaProvider,
    NullCaptchaProvider,
    PlaywrightCaptchaProvider,
    InProcessCaptchaProvider,
)

# Exceptions
from google_flow.exceptions import (
    FlowAuthError,
    FlowCaptchaError,
    FlowConfigError,
    FlowError,
    FlowGenerationError,
    FlowModelNotFoundError,
    FlowNetworkError,
    FlowRateLimitError,
    FlowServerError,
    FlowTimeoutError,
    FlowTokenExpiredError,
    FlowUploadError,
    FlowUpscaleError,
)

# Model registry
from google_flow.models.registry import (
    IMAGE_MODELS,
    get_model_config,
    list_model_ids,
    list_models,
    resolve_model,
)

# Typed data models
from google_flow.types import (
    AspectRatio,
    CaptchaMethod,
    CreditsInfo,
    GenerationResult,
    ModelConfig,
    Orientation,
    TokenInfo,
    UpscaleResolution,
)

__all__ = [
    # Version
    "__version__",
    # Core
    "FlowClient",
    "ImageGenerator",
    "SessionManager",
    "FlowSDK",
    "CaptchaProvider",
    "NullCaptchaProvider",
    "PlaywrightCaptchaProvider",
    "InProcessCaptchaProvider",
    # Config
    "AppConfig",
    "get_config",
    "set_config",
    # Types
    "AspectRatio",
    "CaptchaMethod",
    "CreditsInfo",
    "GenerationResult",
    "ModelConfig",
    "Orientation",
    "TokenInfo",
    "UpscaleResolution",
    # Exceptions
    "FlowAuthError",
    "FlowCaptchaError",
    "FlowConfigError",
    "FlowError",
    "FlowGenerationError",
    "FlowModelNotFoundError",
    "FlowNetworkError",
    "FlowRateLimitError",
    "FlowServerError",
    "FlowTimeoutError",
    "FlowTokenExpiredError",
    "FlowUploadError",
    "FlowUpscaleError",
    # Models
    "IMAGE_MODELS",
    "get_model_config",
    "list_model_ids",
    "list_models",
    "resolve_model",
]
