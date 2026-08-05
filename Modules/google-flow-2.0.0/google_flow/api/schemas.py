"""
Pydantic request/response schemas for the OpenAI-compatible API.
"""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field

from google_flow.constants import DEFAULT_MODEL_ID


class ImageGenerationRequest(BaseModel):
    """Request body for ``POST /v1/images/generations``."""

    model: str = Field(default=DEFAULT_MODEL_ID)
    prompt: str
    size: str | None = Field(
        default=None, description="1024x1024 / 1024x1536 / 1536x1024"
    )
    aspect_ratio: str | None = Field(
        default=None, description="1:1 / 9:16 / 16:9 / 21:9"
    )
    quality: str | None = Field(
        default="standard", description="standard / hd / 2k / 4k"
    )
    response_format: str | None = Field(
        default="url", description="url / b64_json"
    )
    n: int = Field(default=1)


class ChatCompletionRequest(BaseModel):
    """Request body for ``POST /v1/chat/completions``."""

    model: str = Field(default=DEFAULT_MODEL_ID)
    messages: list[dict[str, Any]]
    size: str | None = None
    quality: str | None = None
    aspect_ratio: str | None = None
    response_format: str | None = "url"
    stream: bool | None = False
    n: int | None = 1

    model_config = {"extra": "allow"}
