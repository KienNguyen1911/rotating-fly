"""
OpenAI-compatible API routes.

Provides ``/v1/models``, ``/v1/images/generations``,
``/v1/images/edits``, ``/v1/chat/completions``, and ``/v1/files/*``.
"""

from __future__ import annotations

import base64
import json
import re
import tempfile
import time
import uuid
from pathlib import Path
from typing import Any

from fastapi import APIRouter, Depends, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse

from google_flow.api.deps import verify_api_key
from google_flow.api.schemas import ChatCompletionRequest, ImageGenerationRequest  # noqa: TC001
from google_flow.config import get_config
from google_flow.core.client import FlowClient
from google_flow.core.generator import ImageGenerator
from google_flow.logging import get_logger
from google_flow.models.registry import (
    DEFAULT_MODEL,
    IMAGE_MODELS,
    resolve_model,
)

logger = get_logger(__name__)

router = APIRouter(prefix="/v1", tags=["openai"])

OUTPUT_ROOT = Path(tempfile.gettempdir()) / "flow-image-api"
DEBUG_ROOT = OUTPUT_ROOT / "_debug"


# ── Helpers ─────────────────────────────────────────────────────────

def _build_output_path(tag: str = "image") -> Path:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    filename = f"{tag}_{int(time.time())}_{uuid.uuid4().hex[:8]}.png"
    return OUTPUT_ROOT / filename


def _resolve_upscale(size: str | None, quality: str | None) -> str:
    for raw in (quality, size):
        if not raw:
            continue
        value = str(raw).strip().lower()
        if value in {"4k"}:
            return "4k"
        if value in {"2k", "hd"}:
            return "2k"
        if value in {"1k", "standard", "default", "original"}:
            return "none"
    return "none"


def _encode_image(path: Path) -> str:
    return base64.b64encode(path.read_bytes()).decode("ascii")


def _build_image_item(
    request: Request, path: Path, response_format: str
) -> dict[str, str]:
    if response_format == "b64_json":
        return {"b64_json": _encode_image(path)}
    return {
        "url": str(request.base_url).rstrip("/") + f"/v1/files/{path.name}"
    }


def _build_chat_response(
    request: Request, model_id: str, path: Path, response_format: str
) -> dict[str, Any]:
    image_item = _build_image_item(request, path, response_format)
    message_content = image_item.get("url") or image_item.get("b64_json") or ""
    return {
        "id": f"chatcmpl-{uuid.uuid4().hex}",
        "object": "chat.completion",
        "created": int(time.time()),
        "model": model_id,
        "choices": [
            {
                "index": 0,
                "message": {"role": "assistant", "content": message_content},
                "finish_reason": "stop",
            }
        ],
        "usage": {
            "prompt_tokens": 0,
            "completion_tokens": 0,
            "total_tokens": 0,
        },
        "data": [image_item],
    }


def _save_debug_payload(name: str, payload: Any) -> None:
    DEBUG_ROOT.mkdir(parents=True, exist_ok=True)
    path = DEBUG_ROOT / name
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2, ensure_ascii=False)


def _extract_prompt(messages: list[dict[str, Any]]) -> str:
    text_parts: list[str] = []
    for msg in messages:
        if msg.get("role") != "user":
            continue
        content = msg.get("content")
        if isinstance(content, str):
            text_parts.append(content)
        elif isinstance(content, list):
            for item in content:
                if not isinstance(item, dict):
                    continue
                itype = (item.get("type") or "").lower()
                if itype in {"text", "input_text"} and item.get("text"):
                    text_parts.append(str(item["text"]))
    prompt = "\n".join(p.strip() for p in text_parts if str(p).strip()).strip()
    if not prompt:
        raise HTTPException(
            status_code=400, detail="No user prompt found in messages"
        )
    return prompt


def _extract_preferred_value(prompt: str, label: str) -> str | None:
    patterns = [
        rf"{label}\s*:\s*([^\n\.]+)",
        rf"{label}\s*-\s*([^\n\.]+)",
    ]
    for pattern in patterns:
        match = re.search(pattern, prompt, flags=re.IGNORECASE)
        if match:
            value = match.group(1).strip()
            if value:
                return value
    return None


def _strip_preference_lines(prompt: str) -> str:
    cleaned = re.sub(
        r"(?im)^\s*preferred\s+(size|aspect\s*ratio)\s*[:\-]\s*[^\n]+\s*$",
        "",
        prompt,
    )
    return re.sub(r"\n{3,}", "\n\n", cleaned).strip()


def _extract_reference_image(
    messages: list[dict[str, Any]],
) -> bytes | None:
    for msg in messages:
        if msg.get("role") != "user":
            continue
        content = msg.get("content")
        if not isinstance(content, list):
            continue
        for item in content:
            if not isinstance(item, dict):
                continue
            itype = (item.get("type") or "").lower()
            if itype in {"image_url", "input_image"}:
                image_url = item.get("image_url")
                if isinstance(image_url, dict):
                    image_url = image_url.get("url")
                if (
                    isinstance(image_url, str)
                    and image_url.startswith("data:")
                    and "," in image_url
                ):
                    return base64.b64decode(image_url.split(",", 1)[1])
    return None


def _create_generator() -> ImageGenerator:
    """Build a ready-to-use ImageGenerator from global config."""
    config = get_config()
    session = config.create_session_manager()
    client = FlowClient(
        labs_base_url=config.flow.labs_base_url,
        api_base_url=config.flow.api_base_url,
        timeout=config.flow.timeout,
    )
    captcha = config.create_captcha_provider(session.token.st)
    return ImageGenerator(
        client=client,
        session=session,
        captcha_provider=captcha.get_token,
        max_retries=config.flow.max_retries,
    )


# ── Routes ──────────────────────────────────────────────────────────

@router.get("/models", dependencies=[Depends(verify_api_key)])
async def list_models() -> dict[str, Any]:
    """List available models (OpenAI-compatible)."""
    data = [
        {"id": mid, "object": "model", "owned_by": "flow-local"}
        for mid in IMAGE_MODELS
    ]
    return {"object": "list", "data": data}


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

    generator = _create_generator()
    try:
        async with generator.client:
            saved_path = await generator.generate(
                payload.prompt,
                model=model_id,
                output_path=str(output_path),
                upscale=upscale,
            )
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    path = Path(saved_path)
    fmt = (payload.response_format or "url").lower()
    return {
        "created": int(time.time()),
        "data": [_build_image_item(request, path, fmt)],
    }


@router.post("/images/edits", dependencies=[Depends(verify_api_key)])
async def edit_image(
    request: Request,
    image: UploadFile = File(...),
    prompt: str = Form(...),
    model: str = Form(default=DEFAULT_MODEL),
    size: str | None = Form(default=None),
    aspect_ratio: str | None = Form(default=None),
    quality: str | None = Form(default="standard"),
    response_format: str | None = Form(default="url"),
    n: int = Form(default=1),
) -> dict[str, Any]:
    """Edit/transform an existing image."""
    if n != 1:
        raise HTTPException(status_code=400, detail="Only n=1 is supported")

    model_id = resolve_model(model, size, aspect_ratio)
    upscale = _resolve_upscale(size, quality)
    image_bytes = await image.read()
    if not image_bytes:
        raise HTTPException(status_code=400, detail="Image file is empty")

    output_path = _build_output_path("edit")
    generator = _create_generator()
    try:
        async with generator.client:
            saved_path = await generator.generate(
                prompt,
                model=model_id,
                reference_image=image_bytes,
                output_path=str(output_path),
                upscale=upscale,
            )
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    path = Path(saved_path)
    fmt = (response_format or "url").lower()
    return {
        "created": int(time.time()),
        "data": [_build_image_item(request, path, fmt)],
    }


@router.post("/chat/completions", dependencies=[Depends(verify_api_key)])
async def chat_completions(
    request: Request, payload: ChatCompletionRequest
) -> dict[str, Any]:
    """Chat completions endpoint (image generation via chat messages)."""
    if payload.stream:
        raise HTTPException(
            status_code=400, detail="stream=true is not supported"
        )
    if payload.n not in (None, 1):
        raise HTTPException(status_code=400, detail="Only n=1 is supported")

    payload_dict = payload.model_dump()
    _save_debug_payload("latest_chat_request.json", payload_dict)

    prompt = _extract_prompt(payload.messages)
    inferred_size = _extract_preferred_value(prompt, "preferred size")
    inferred_ratio = _extract_preferred_value(prompt, "preferred aspect ratio")
    prompt = _strip_preference_lines(prompt)
    reference_image = _extract_reference_image(payload.messages)
    size = payload.size or inferred_size
    aspect_ratio = payload.aspect_ratio or inferred_ratio
    model_id = resolve_model(payload.model, size, aspect_ratio)
    upscale = _resolve_upscale(size, payload.quality)
    output_path = _build_output_path("chat")

    generator = _create_generator()
    try:
        async with generator.client:
            saved_path = await generator.generate(
                prompt,
                model=model_id,
                reference_image=reference_image,
                output_path=str(output_path),
                upscale=upscale,
            )
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    path = Path(saved_path)
    fmt = (payload.response_format or "url").lower()
    return _build_chat_response(request, model_id, path, fmt)


@router.get("/files/{filename}")
async def get_generated_file(filename: str) -> FileResponse:
    """Serve a previously generated image file."""
    path = OUTPUT_ROOT / filename
    if not path.exists():
        raise HTTPException(status_code=404, detail="File not found")
    return FileResponse(path)
