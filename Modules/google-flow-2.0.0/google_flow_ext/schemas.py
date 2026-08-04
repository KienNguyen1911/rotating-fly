"""
Pydantic request/response schemas for the extended OpenAI-compatible API.
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
    reference_media_id: Any | None = Field(
        default=None, description="Optional media ID string or list of media IDs to use as reference image(s)"
    )
    project_id: str | None = Field(
        default=None, description="Optional Google Flow website project ID"
    )
    project_title: str | None = Field(
        default=None, description="Optional project title to create/target a project on Google Flow website"
    )
    n: int = Field(default=1)


class ProjectCreateRequest(BaseModel):
    """Request body for ``POST /v1/projects``."""

    title: str = Field(default="Flow CLI Project", description="Title of the project on Google Flow website")


class ProjectCreateResponse(BaseModel):
    """Response body for ``POST /v1/projects``."""

    status: str = "success"
    project_id: str
    title: str
    project_url: str


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
