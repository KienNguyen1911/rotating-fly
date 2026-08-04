"""
Extended OpenAI-compatible API routes.
"""

from __future__ import annotations

import base64
import json
import re
import time
from pathlib import Path
from typing import Any

from fastapi import APIRouter, Depends, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse

from google_flow.api.deps import verify_api_key
from google_flow.constants import DEFAULT_MODEL_ID
from google_flow.logging import get_logger
from google_flow.models.registry import IMAGE_MODELS, resolve_model
from google_flow.types import UpscaleResolution

from google_flow_ext.schemas import (
    ChatCompletionRequest,
    ImageGenerationRequest,
    ProjectCreateRequest,
    ProjectCreateResponse,
)
from google_flow_ext.sdk import ExtendedFlowSDK

logger = get_logger(__name__)

router = APIRouter(prefix="/v1", tags=["OpenAI Compatible API (Extended)"])

OUTPUT_ROOT = Path("output")
OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)





def _resolve_upscale(size: str | None, quality: str | None) -> str:
    """Determine upscale setting from quality/size."""
    if quality:
        res = UpscaleResolution.from_string(quality)
        if res != UpscaleResolution.NONE:
            return res.value

    if size and ("2k" in size.lower() or "4k" in size.lower()):
        res = UpscaleResolution.from_string(size)
        if res != UpscaleResolution.NONE:
            return res.value

    return "OFF"


def _build_output_path(prefix: str = "gen") -> Path:
    """Generate unique file path for output image."""
    ts = int(time.time() * 1000)
    return OUTPUT_ROOT / f"{prefix}_{ts}.png"


def _build_image_item(
    request: Request,
    path: Path,
    fmt: str,
    media_id: str | None = None,
    project_id: str | None = None,
) -> dict[str, Any]:
    """Format single image item for API response."""
    item: dict[str, Any] = {}
    if fmt == "b64_json":
        data = path.read_bytes()
        item["b64_json"] = base64.b64encode(data).decode("utf-8")
    else:
        filename = path.name
        base_url = str(request.base_url).rstrip("/")
        item["url"] = f"{base_url}/v1/files/{filename}"

    if media_id:
        item["media_id"] = media_id
    if project_id:
        item["project_id"] = project_id

    return item


@router.post(
    "/projects",
    response_model=ProjectCreateResponse,
    dependencies=[Depends(verify_api_key)],
)
async def create_project(payload: ProjectCreateRequest) -> ProjectCreateResponse:
    """Create a new project on Google Flow website."""
    async with ExtendedFlowSDK() as sdk:
        proj_id = await sdk.generator.create_new_project(title=payload.title)
        return ProjectCreateResponse(
            status="success",
            project_id=proj_id,
            title=payload.title,
            project_url=f"https://labs.google/fx/tools/flow/project/{proj_id}",
        )


@router.post("/images/generations", dependencies=[Depends(verify_api_key)])
async def generate_image(
    request: Request, payload: ImageGenerationRequest
) -> dict[str, Any]:
    """Generate an image from a text prompt."""
    if payload.n != 1:
        raise HTTPException(status_code=400, detail="Only n=1 is supported")

    model_id = resolve_model(payload.model, payload.size, payload.aspect_ratio)
    upscale = _resolve_upscale(payload.size, payload.quality)
    output_path = _build_output_path("gen")

    async with ExtendedFlowSDK() as sdk:
        try:
            saved_path = await sdk.generator.generate_ext(
                payload.prompt,
                model=model_id,
                reference_image=payload.reference_media_id,
                output_path=str(output_path),
                upscale=upscale,
                project_id=payload.project_id,
                project_title=payload.project_title,
            )
        except Exception as exc:
            logger.error("Image generation failed: %s", exc, exc_info=True)
            raise HTTPException(status_code=500, detail=str(exc)) from exc

        path = Path(saved_path)
        fmt = (payload.response_format or "url").lower()
        return {
            "created": int(time.time()),
            "data": [
                _build_image_item(
                    request,
                    path,
                    fmt,
                    media_id=sdk.generator.last_media_id,
                    project_id=sdk.generator.last_project_id,
                )
            ],
        }


@router.post("/images/edits", dependencies=[Depends(verify_api_key)])
async def edit_image(
    request: Request,
    image: UploadFile = File(...),
    prompt: str = Form(...),
    model: str = Form(default=DEFAULT_MODEL_ID),
    size: str | None = Form(default=None),
    aspect_ratio: str | None = Form(default=None),
    quality: str | None = Form(default="standard"),
    response_format: str | None = Form(default="url"),
    project_id: str | None = Form(default=None),
    project_title: str | None = Form(default=None),
    n: int = Form(default=1),
) -> dict[str, Any]:
    """Edit/transform an existing image file (Image-to-Image)."""
    if n != 1:
        raise HTTPException(status_code=400, detail="Only n=1 is supported")

    model_id = resolve_model(model, size, aspect_ratio)
    upscale = _resolve_upscale(size, quality)
    image_bytes = await image.read()
    if not image_bytes:
        raise HTTPException(status_code=400, detail="Uploaded image file is empty")

    output_path = _build_output_path("edit")
    async with ExtendedFlowSDK() as sdk:
        try:
            saved_path = await sdk.generator.generate_ext(
                prompt,
                model=model_id,
                reference_image=image_bytes,
                output_path=str(output_path),
                upscale=upscale,
                project_id=project_id,
                project_title=project_title,
            )
        except Exception as exc:
            logger.error("Image edit failed: %s", exc, exc_info=True)
            raise HTTPException(status_code=500, detail=str(exc)) from exc

        path = Path(saved_path)
        fmt = (response_format or "url").lower()
        return {
            "created": int(time.time()),
            "data": [
                _build_image_item(
                    request,
                    path,
                    fmt,
                    media_id=sdk.generator.last_media_id,
                    project_id=sdk.generator.last_project_id,
                )
            ],
        }


@router.get("/files/{filename}")
async def get_generated_file(filename: str) -> FileResponse:
    """Serve a previously generated image file."""
    path = OUTPUT_ROOT / filename
    if not path.exists():
        raise HTTPException(status_code=404, detail="File not found")
    return FileResponse(path)
