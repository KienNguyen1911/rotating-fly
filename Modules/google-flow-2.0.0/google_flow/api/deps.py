"""
FastAPI dependency injection helpers.

Provides authentication verification and shared service instances.
"""

from __future__ import annotations

import os
import secrets

from fastapi import Header, HTTPException

from google_flow.constants import API_KEY_ENV_VAR, DEFAULT_API_KEY


def get_api_key() -> str:
    """Read the expected API key from environment or use default."""
    return os.environ.get(API_KEY_ENV_VAR, DEFAULT_API_KEY).strip()


def verify_api_key(
    authorization: str | None = Header(default=None),
) -> None:
    """FastAPI dependency that validates Bearer token auth."""
    expected = get_api_key()
    if not authorization or not authorization.startswith("Bearer "):
        raise HTTPException(status_code=401, detail="Missing Bearer token")
    provided = authorization.split(" ", 1)[1].strip()
    if not secrets.compare_digest(provided, expected):
        raise HTTPException(status_code=401, detail="Invalid API key")
