#!/usr/bin/env python3
"""
Test script that replicates what FlowLocalImageGenProvider.ProcessSingleItemAsync
does for the project at D:\\Project-AA\\Project_20260803_214109.

Walks through project.json, picks the first item, uploads the @character
reference image, and tries to POST /v1/images/generations with the
same payload the WinUI provider sends.

Logs every request body, response status, and response body so we can see
exactly why each step fails.
"""

from __future__ import annotations

import base64
import io
import json
import sys
import time
from pathlib import Path

import urllib.request
import urllib.error
import urllib.parse


SERVER_URL = "http://127.0.0.1:8787"
API_KEY = "flow-local-key"
PROJECT_DIR = Path(r"D:\Project-AA\Project_20260803_214109")
PROJECT_JSON = PROJECT_DIR / "project.json"
OUTPUT_DIR = PROJECT_DIR / "Images"


def http_post_json(path: str, payload: dict, *, timeout: int = 120) -> tuple[int, dict | str]:
    url = f"{SERVER_URL}{path}"
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=body,
        method="POST",
        headers={
            "Authorization": f"Bearer {API_KEY}",
            "Content-Type": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read().decode("utf-8")
            try:
                return resp.status, json.loads(data)
            except json.JSONDecodeError:
                return resp.status, data
    except urllib.error.HTTPError as exc:
        body_text = exc.read().decode("utf-8", errors="replace")
        try:
            return exc.code, json.loads(body_text)
        except json.JSONDecodeError:
            return exc.code, body_text


def http_post_multipart(path: str, fields: dict, file_field: tuple[str, bytes, str], *, timeout: int = 180) -> tuple[int, dict | str]:
    """file_field = (field_name, file_bytes, file_name)."""
    url = f"{SERVER_URL}{path}"
    boundary = "----TestBoundary7MA4YWxkTrZu0gW"
    crlf = b"\r\n"
    parts: list[bytes] = []
    for k, v in fields.items():
        parts.append(f"--{boundary}".encode())
        parts.append(f'Content-Disposition: form-data; name="{k}"'.encode())
        parts.append(b"")
        parts.append(str(v).encode())
    field_name, file_bytes, file_name = file_field
    parts.append(f"--{boundary}".encode())
    parts.append(
        f'Content-Disposition: form-data; name="{field_name}"; filename="{file_name}"'.encode()
    )
    parts.append(b"Content-Type: image/png")
    parts.append(b"")
    parts.append(file_bytes)
    parts.append(f"--{boundary}--".encode())
    body = crlf.join(parts)

    req = urllib.request.Request(
        url,
        data=body,
        method="POST",
        headers={
            "Authorization": f"Bearer {API_KEY}",
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "Content-Length": str(len(body)),
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read().decode("utf-8")
            try:
                return resp.status, json.loads(data)
            except json.JSONDecodeError:
                return resp.status, data
    except urllib.error.HTTPError as exc:
        body_text = exc.read().decode("utf-8", errors="replace")
        try:
            return exc.code, json.loads(body_text)
        except json.JSONDecodeError:
            return exc.code, body_text


def main() -> int:
    if not PROJECT_JSON.exists():
        print(f"ERROR: project.json not found at {PROJECT_JSON}")
        return 2

    print(f"== Loading project: {PROJECT_JSON}")
    project = json.loads(PROJECT_JSON.read_text(encoding="utf-8"))
    items = project.get("Items", [])
    print(f"   Scenes: {len(items)}")
    print(f"   FlowProjectId: {project.get('FlowProjectId')}")
    print(f"   Reference images: {project.get('RefImagePaths')}")

    if not items:
        print("ERROR: no scenes in project")
        return 3

    # Pick first non-failed item; we want to exercise the path that the WinUI
    # UI exercises when the user presses "Generate batch".
    target = None
    for it in items:
        if (it.get("Status") or "").lower() not in ("done",):
            target = it
            break
    if target is None:
        target = items[0]
    print()
    print(f"== Test scene #{target['Index']}: {target.get('SceneTitle')}")
    print(f"   Prompt (first 120 chars): {target['Prompt'][:120]}...")
    print(f"   Model: {target.get('Model')}")
    print(f"   AspectRatio: {target.get('AspectRatio')}")
    print(f"   Upscale: {target.get('Upscale')}")

    reference_image_path = None
    ref_paths = project.get("RefImagePaths") or []
    for p in ref_paths:
        if p and Path(p).exists():
            reference_image_path = Path(p)
            break
    print(f"   Reference image: {reference_image_path}")

    model = target.get("Model") or "nano-banana-2"
    aspect = target.get("AspectRatio") or "16:9"

    # Mirror FormatModelName in FlowLocalImageGenProvider.cs:
    model = model.replace("_", "-")
    if aspect == "16:9":
        model_with_orient = model + "-landscape"
    elif aspect in ("9:16", "3:4"):
        model_with_orient = model + "-portrait"
    elif aspect == "1:1":
        model_with_orient = model + "-square"
    elif aspect == "21:9":
        model_with_orient = model + "-ultrawide"
    else:
        model_with_orient = model + "-landscape"
    size = "1536x1024" if aspect in ("16:9", "21:9") else ("1024x1024" if aspect == "1:1" else "1024x1536")

    print(f"   Resolved model: {model_with_orient}")
    print(f"   Size: {size}")

    flow_project_id = None

    # ── Step 1: try create_project (matches WinUI's pre-flight) ──
    print()
    print("== Step 1: POST /v1/projects")
    status, resp = http_post_json("/v1/projects", {"title": project.get("ProjectName", "Test Project")})
    print(f"   HTTP {status}")
    print(f"   Response: {json.dumps(resp, ensure_ascii=False)[:400]}")
    if status == 200 and isinstance(resp, dict):
        flow_project_id = resp.get("project_id")
        print(f"   ✅ flow_project_id = {flow_project_id}")
    else:
        print(f"   ⚠️ create_project failed; will fall back to project_id-less path")

    # ── Step 2: upload reference image (matches Step 1 in ProcessSingleItemAsync) ──
    media_id = None
    if reference_image_path:
        print()
        print(f"== Step 2: POST /v1/images/edits (upload reference image)")
        file_bytes = reference_image_path.read_bytes()
        print(f"   file: {reference_image_path.name} ({len(file_bytes):,} bytes)")
        fields = {
            "model": model_with_orient,
            "prompt": target["Prompt"],
            "size": size,
            "quality": "standard",
            "response_format": "url",
        }
        if flow_project_id:
            fields["project_id"] = flow_project_id
        status, resp = http_post_multipart(
            "/v1/images/edits",
            fields,
            ("image", file_bytes, reference_image_path.name),
        )
        print(f"   HTTP {status}")
        print(f"   Response (first 500 chars): {json.dumps(resp, ensure_ascii=False)[:500]}")
        if status == 200 and isinstance(resp, dict):
            data = resp.get("data") or []
            if data and isinstance(data, list):
                first = data[0]
                media_id = first.get("media_id") or first.get("mediaId")
                print(f"   ✅ media_id = {media_id}")
        if status != 200:
            print(f"   ❌ upload failed; aborting the rest of the test.")
            return 4

    # ── Step 3: generate image (matches Step 2 in ProcessSingleItemAsync) ──
    print()
    print(f"== Step 3: POST /v1/images/generations")
    payload = {
        "model": model_with_orient,
        "prompt": target["Prompt"],
        "size": size,
        "quality": "standard",
        "response_format": "url",
    }
    if media_id:
        payload["reference_media_id"] = media_id
    if flow_project_id:
        payload["project_id"] = flow_project_id
    else:
        payload["project_title"] = project.get("ProjectName", "Test Project")

    print(f"   Payload keys: {list(payload.keys())}")
    status, resp = http_post_json("/v1/images/generations", payload, timeout=300)
    print(f"   HTTP {status}")
    if isinstance(resp, dict):
        print(f"   Response: {json.dumps(resp, ensure_ascii=False)[:800]}")
    else:
        print(f"   Response (raw, first 500 chars): {str(resp)[:500]}")

    if status == 200:
        data = resp.get("data") or []
        if data:
            url = data[0].get("url")
            print()
            print(f"✅ Generation succeeded. URL: {url}")
            return 0

    print()
    print(f"❌ Generation failed (HTTP {status}).")
    return 1


if __name__ == "__main__":
    sys.exit(main())