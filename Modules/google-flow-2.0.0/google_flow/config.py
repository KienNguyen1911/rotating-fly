"""
Configuration management.

Uses Pydantic ``BaseModel`` for validated, typed configuration with
support for TOML files and environment variable overrides.
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

if sys.version_info >= (3, 11):
    import tomllib
else:
    import importlib
    tomllib = importlib.import_module("tomli")

from pydantic import BaseModel, Field

from google_flow.captcha.base import CaptchaProvider, NullCaptchaProvider
from google_flow.captcha.playwright_provider import PlaywrightCaptchaProvider
from google_flow.constants import (
    API_BASE_URL,
    CAPTCHA_DISABLED_VALUES,
    DEFAULT_OUTPUT_DIR,
    LABS_BASE_URL,
)
from google_flow.core.session import SessionManager
from google_flow.logging import get_logger

logger = get_logger(__name__)


# ── Config Sections ─────────────────────────────────────────────────

class FlowConfig(BaseModel):
    """Flow API connection settings."""

    labs_base_url: str = LABS_BASE_URL
    api_base_url: str = API_BASE_URL
    timeout: int = 120
    max_retries: int = 3


class CaptchaConfig(BaseModel):
    """Captcha acquisition settings."""

    method: str = "personal"
    personal_headless: bool = False
    personal_timeout: int = 90
    personal_settle_seconds: float = 2.0


# ── Main App Config ─────────────────────────────────────────────────

class AppConfig(BaseModel):
    """Root application configuration.

    Loaded from ``~/.google-flow/config.toml`` by default, with
    environment variable overrides via ``FLOW_*`` prefix.
    """

    flow: FlowConfig = Field(default_factory=FlowConfig)
    captcha: CaptchaConfig = Field(default_factory=CaptchaConfig)
    output_dir: str = DEFAULT_OUTPUT_DIR
    debug: bool = False

    # ── Loading ─────────────────────────────────────────────────────

    @classmethod
    def load(cls, config_path: str | None = None) -> AppConfig:
        """Load configuration from TOML file and environment.

        Lookup order:
        1. Explicit *config_path*
        2. ``FLOW_CONFIG`` environment variable
        3. ``~/.google-flow/config.toml``

        Environment overrides (highest priority):
        - ``FLOW_TIMEOUT`` → ``flow.timeout``
        - ``FLOW_MAX_RETRIES`` → ``flow.max_retries``
        - ``FLOW_OUTPUT_DIR`` → ``output_dir``
        - ``FLOW_DEBUG`` → ``debug``
        """
        config = cls()

        if config_path is None:
            config_path = os.environ.get(
                "FLOW_CONFIG",
                str(Path.home() / ".google-flow" / "config.toml"),
            )

        config_file = Path(config_path)
        if config_file.exists():
            try:
                with open(config_file, "rb") as f:
                    data = tomllib.load(f)

                # Flow section
                if "flow" in data:
                    for key, value in data["flow"].items():
                        if hasattr(config.flow, key):
                            setattr(config.flow, key, value)

                # Captcha section
                if "captcha" in data:
                    for key, value in data["captcha"].items():
                        if hasattr(config.captcha, key):
                            setattr(config.captcha, key, value)

                # Output dir
                if "output" in data and isinstance(data["output"], dict):
                    if "output_dir" in data["output"]:
                        config.output_dir = data["output"]["output_dir"]
                elif "output_dir" in data:
                    config.output_dir = data["output_dir"]

                # Debug
                if "debug" in data and isinstance(data["debug"], dict):
                    if "enabled" in data["debug"]:
                        config.debug = bool(data["debug"]["enabled"])
                elif "debug" in data:
                    config.debug = bool(data["debug"])

                logger.debug("Loaded config from %s", config_file)
            except Exception as exc:
                logger.warning("Failed to load config file: %s", exc)

        # Environment variable overrides
        if env_timeout := os.environ.get("FLOW_TIMEOUT"):
            config.flow.timeout = int(env_timeout)
        if env_retries := os.environ.get("FLOW_MAX_RETRIES"):
            config.flow.max_retries = int(env_retries)
        if env_output := os.environ.get("FLOW_OUTPUT_DIR"):
            config.output_dir = env_output
        if os.environ.get("FLOW_DEBUG", "").lower() in {"1", "true", "yes"}:
            config.debug = True

        return config

    # ── Factory Helpers ─────────────────────────────────────────────

    def create_session_manager(self, config_path: str | None = None) -> SessionManager:
        """Build a :class:`SessionManager` with the correct token path."""
        if config_path is None:
            config_path = os.environ.get(
                "FLOW_CONFIG",
                str(Path.home() / ".google-flow" / "config.toml"),
            )
        token_path = Path(config_path).parent / "token.json"
        return SessionManager.load(token_path)

    def create_captcha_provider(self, st_token: str = "") -> CaptchaProvider:
        """Build the appropriate captcha provider based on config."""
        method = (self.captcha.method or "").strip().lower()
        if method in CAPTCHA_DISABLED_VALUES:
            return NullCaptchaProvider()
        if method == "personal":
            return PlaywrightCaptchaProvider(
                st_token=st_token or None,
                headless=self.captcha.personal_headless,
                timeout_seconds=self.captcha.personal_timeout,
                settle_seconds=self.captcha.personal_settle_seconds,
            )
        logger.warning("Unknown captcha method %r, disabling", method)
        return NullCaptchaProvider()


# ── Convenience ─────────────────────────────────────────────────────

_global_config: AppConfig | None = None


def get_config() -> AppConfig:
    """Return the global :class:`AppConfig` singleton (lazy-loaded).

    .. note::
        Prefer dependency injection over this global accessor
        in new code.
    """
    global _global_config
    if _global_config is None:
        _global_config = AppConfig.load()
    return _global_config


def set_config(config: AppConfig) -> None:
    """Set the global AppConfig instance."""
    global _global_config
    _global_config = config


def reset_config() -> None:
    """Reset the global config (mainly for testing)."""
    global _global_config
    _global_config = None
