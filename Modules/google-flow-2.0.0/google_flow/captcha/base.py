"""
Abstract captcha provider interface.
"""

from __future__ import annotations

from abc import ABC, abstractmethod


class CaptchaProvider(ABC):
    """Base class for captcha token providers."""

    @abstractmethod
    async def get_token(
        self,
        project_id: str,
        action: str = "IMAGE_GENERATION",
    ) -> str | None:
        """Acquire a reCAPTCHA token, or return *None* to skip."""
        ...


class NullCaptchaProvider(CaptchaProvider):
    """Provider that always returns *None* (captcha disabled)."""

    async def get_token(
        self,
        project_id: str,
        action: str = "IMAGE_GENERATION",
    ) -> None:
        return None
