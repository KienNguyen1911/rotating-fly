"""Parsing utilities for configuration and user inputs."""
from __future__ import annotations

from typing import Any


def parse_bool(value: Any, default: bool = False) -> bool:
    """Parse any value as boolean, falling back to a default value."""
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return bool(value)
    text = str(value).strip().lower()
    if text in {"1", "true", "yes", "on"}:
        return True
    if text in {"0", "false", "no", "off"}:
        return False
    return default


def parse_bool_strict(value: Any) -> bool:
    """Parse any value as boolean strictly. Raises ValueError if invalid."""
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return bool(value)
    text = str(value).strip().lower()
    if text in {"1", "true", "yes", "on"}:
        return True
    if text in {"0", "false", "no", "off"}:
        return False
    raise ValueError(f"Value '{value}' is not a valid boolean")


def parse_int(value: Any, default: int) -> int:
    """Parse value as integer, falling back to a default value."""
    if value is None:
        return default
    try:
        return int(value)
    except (TypeError, ValueError):
        return default
