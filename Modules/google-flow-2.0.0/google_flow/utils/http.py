"""
HTTP transport helpers.

Provides a thin wrapper around ``curl_cffi`` (preferred) or
``aiohttp`` (fallback), including browser fingerprint generation
and the conditional import logic.
"""

from __future__ import annotations

import hashlib
import random

from google_flow.constants import CHROME_VERSIONS, DEFAULT_BROWSER_HEADERS
from google_flow.logging import get_logger

logger = get_logger(__name__)

# ── Conditional imports ─────────────────────────────────────────────

try:
    from curl_cffi.requests import AsyncSession as CurlAsyncSession

    HAS_CURL_CFFI = True
except ImportError:
    CurlAsyncSession = None  # type: ignore[assignment,misc]
    HAS_CURL_CFFI = False

try:
    import aiohttp

    HAS_AIOHTTP = True
except ImportError:
    aiohttp = None  # type: ignore[assignment]
    HAS_AIOHTTP = False


def get_http_backend() -> str:
    """Return the name of the active HTTP backend."""
    if HAS_CURL_CFFI:
        return "curl_cffi"
    if HAS_AIOHTTP:
        return "aiohttp"
    raise ImportError(
        "No HTTP backend available.  Install either curl-cffi or aiohttp."
    )


# ── User-Agent Generation ──────────────────────────────────────────

_ua_cache: dict[str, str] = {}


def generate_user_agent(account_id: str | None = None) -> str:
    """Generate a deterministic User-Agent string seeded by *account_id*."""
    key = account_id or f"random_{random.randint(1, 999999)}"
    if key in _ua_cache:
        return _ua_cache[key]

    seed = int(hashlib.md5(key.encode()).hexdigest()[:8], 16)
    rng = random.Random(seed)
    chrome = rng.choice(CHROME_VERSIONS)
    ua = (
        f"Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        f"AppleWebKit/537.36 (KHTML, like Gecko) "
        f"Chrome/{chrome} Safari/537.36"
    )
    _ua_cache[key] = ua
    return ua


def build_headers(
    *,
    account_id: str | None = None,
    extra: dict[str, str] | None = None,
) -> dict[str, str]:
    """Build a complete header dict with fingerprint and Content-Type."""
    headers: dict[str, str] = {
        "Content-Type": "application/json",
        "User-Agent": generate_user_agent(account_id),
    }
    headers.update(DEFAULT_BROWSER_HEADERS)
    if extra:
        headers.update(extra)
    return headers
