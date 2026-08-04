"""
Centralised constants for the google_flow package.

Keeping magic strings, URLs, and default values here avoids
scattering them across multiple modules and makes them trivially
auditable and updatable.
"""

from __future__ import annotations

# ── API URLs ────────────────────────────────────────────────────────

LABS_BASE_URL = "https://labs.google/fx/api"
API_BASE_URL = "https://aisandbox-pa.googleapis.com/v1"
FLOW_LOGIN_URL = "https://labs.google/fx/tools/flow"

# ── reCAPTCHA ───────────────────────────────────────────────────────

RECAPTCHA_SITE_KEY = "6LdsFiUsAAAAAIjVDZcuLhaHiDn5nnHVXVRQGeMV"
RECAPTCHA_DEFAULT_ACTION = "IMAGE_GENERATION"

# ── HTTP Defaults ───────────────────────────────────────────────────

DEFAULT_TIMEOUT = 120
DEFAULT_MAX_RETRIES = 3
DEFAULT_RETRY_BASE_DELAY = 1.0   # seconds
DEFAULT_RETRY_MAX_DELAY = 30.0   # seconds

# ── Browser Fingerprint Headers ─────────────────────────────────────

DEFAULT_BROWSER_HEADERS: dict[str, str] = {
    "sec-ch-ua-mobile": "?1",
    "sec-ch-ua-platform": '"Android"',
    "sec-fetch-dest": "empty",
    "sec-fetch-mode": "cors",
    "sec-fetch-site": "cross-site",
    "x-browser-channel": "stable",
    "x-browser-copyright": "Copyright 2026 Google LLC. All Rights reserved.",
    "x-browser-validation": "UujAs0GAwdnCJ9nvrswZ+O+oco0=",
    "x-browser-year": "2026",
    "x-client-data": "CJS2yQEIpLbJAQipncoBCNj9ygEIlKHLAQiFoM0BGP6lzwE=",
}

CHROME_VERSIONS: list[str] = [
    "130.0.0.0",
    "131.0.0.0",
    "132.0.0.0",
]

# ── Session Cookie Names ────────────────────────────────────────────

SESSION_COOKIE_NAMES: list[str] = [
    "__Secure-next-auth.session-token",
    "next-auth.session-token",
]

SESSION_COOKIE_CHUNK_PREFIXES: list[str] = [
    "__Secure-next-auth.session-token.",
    "next-auth.session-token.",
]

# ── Defaults ────────────────────────────────────────────────────────

DEFAULT_MODEL_ID = "gemini-3.1-flash-image-landscape"
DEFAULT_OUTPUT_DIR = "output"
DEFAULT_API_KEY = "flow-local-key"
API_KEY_ENV_VAR = "FLOW_API_KEY"

# ── Tool / Client Context ──────────────────────────────────────────

CLIENT_TOOL_NAME = "PINHOLE"

# ── Upscale Resolutions ────────────────────────────────────────────

UPSAMPLE_2K = "UPSAMPLE_IMAGE_RESOLUTION_2K"
UPSAMPLE_4K = "UPSAMPLE_IMAGE_RESOLUTION_4K"

# ── Captcha Methods ────────────────────────────────────────────────

CAPTCHA_DISABLED_VALUES = frozenset({"", "none", "off", "disabled"})
