"""
Image utility helpers.

MIME-type detection, base64 saving, and download helpers extracted
from the old monolithic client module.
"""

from __future__ import annotations

import base64
import time
from pathlib import Path

from google_flow.logging import get_logger

logger = get_logger(__name__)

# ── MIME Detection ──────────────────────────────────────────────────

_MAGIC_BYTES = [
    (b"RIFF", 8, b"WEBP", "image/webp"),
    (b"\x89PNG", 0, None, "image/png"),
    (b"\xff\xd8\xff", 0, None, "image/jpeg"),
    (b"GIF87a", 0, None, "image/gif"),
    (b"GIF89a", 0, None, "image/gif"),
]


def detect_mime_type(data: bytes) -> str:
    """Detect image MIME type from magic bytes.

    Returns ``image/jpeg`` as a safe fallback when detection fails.
    """
    if len(data) < 12:
        return "image/jpeg"

    # WEBP is special: magic at offset 0 is RIFF, then WEBP at offset 8
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "image/webp"
    if data[:4] == b"\x89PNG":
        return "image/png"
    if data[:3] == b"\xff\xd8\xff":
        return "image/jpeg"
    if data[:6] in (b"GIF87a", b"GIF89a"):
        return "image/gif"

    return "image/jpeg"


def mime_to_extension(mime_type: str) -> str:
    """Map a MIME type to a file extension (without dot)."""
    mapping = {
        "image/png": "png",
        "image/jpeg": "jpg",
        "image/webp": "webp",
        "image/gif": "gif",
    }
    return mapping.get(mime_type, "png")


# ── File I/O ────────────────────────────────────────────────────────

def save_bytes(data: bytes, output_path: str | Path) -> Path:
    """Write raw bytes to *output_path*, creating parent directories."""
    path = Path(output_path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    logger.debug("Saved %d bytes → %s", len(data), path)
    return path.resolve()


def save_base64_image(encoded: str, output_path: str | Path) -> Path:
    """Decode a base64 (optionally data-URI prefixed) image and save it."""
    # Strip optional data:image/...;base64, prefix
    if "," in encoded and encoded.strip().startswith("data:image"):
        encoded = encoded.split(",", 1)[1]

    image_data = base64.b64decode(encoded)
    return save_bytes(image_data, output_path)


def generate_output_path(
    prefix: str = "flow",
    suffix: str = "",
    extension: str = "png",
    output_dir: str = "output",
) -> Path:
    """Generate a timestamped output path.

    Example: ``output/flow_1718400000_2k.png``
    """
    timestamp = int(time.time())
    parts = [prefix, str(timestamp)]
    if suffix:
        parts.append(suffix)
    filename = "_".join(parts) + f".{extension}"
    return Path(output_dir) / filename


# ── Download ────────────────────────────────────────────────────────

async def download_image(url: str, output_path: str | Path, timeout: int = 60) -> Path:
    """Download an image from *url* and save to *output_path*.

    Uses ``curl_cffi`` when available, falling back to ``aiohttp``.
    """
    try:
        from curl_cffi.requests import AsyncSession

        async with AsyncSession() as session:
            response = await session.get(url, timeout=timeout, impersonate="chrome110")
            image_data = response.content
    except ImportError:
        import aiohttp

        async with aiohttp.ClientSession() as session, session.get(
            url, timeout=aiohttp.ClientTimeout(total=timeout)
        ) as response:
            image_data = await response.read()

    return save_bytes(image_data, output_path)
