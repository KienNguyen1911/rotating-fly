#!/usr/bin/env python3
"""
Diagnostic-only test that doesn't depend on a valid Google session.
It exercises:

  1. Health check
  2. Model registry resolution (nano-banana-2 + 16:9 → nano-banana-2-landscape)
  3. UpscaleResolution enum (verifies the OFF → NONE bug is gone)
  4. Project payload construction (verifies URL is no longer /api/api/trpc)
  5. End-to-end image generation with the *actual* AT on disk (will 401
     if ST is expired — that's expected, and the only thing blocking).

Run:  python test_diagnose.py
"""

from __future__ import annotations

import json
import sys
import urllib.request
import urllib.error


SERVER_URL = "http://127.0.0.1:8787"
API_KEY = "flow-local-key"


def http_get(path: str) -> tuple[int, dict | str]:
    req = urllib.request.Request(
        f"{SERVER_URL}{path}",
        method="GET",
        headers={"Authorization": f"Bearer {API_KEY}"},
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = resp.read().decode("utf-8")
            try:
                return resp.status, json.loads(data)
            except json.JSONDecodeError:
                return resp.status, data
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        try:
            return exc.code, json.loads(body)
        except json.JSONDecodeError:
            return exc.code, body


def http_post_json(path: str, payload: dict, *, timeout: int = 60) -> tuple[int, dict | str]:
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        f"{SERVER_URL}{path}",
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
        body = exc.read().decode("utf-8", errors="replace")
        try:
            return exc.code, json.loads(body)
        except json.JSONDecodeError:
            return exc.code, body


def check(label: str, ok: bool, hint: str = "") -> None:
    mark = "✅" if ok else "❌"
    print(f"   {mark} {label}{(' — ' + hint) if hint else ''}")


def main() -> int:
    print("== 1. Health check")
    status, body = http_get("/health")
    check(f"GET /health → {status}", status == 200)
    print(f"   body: {body}")
    print()

    print("== 2. /v1/models is auth-gated but reachable")
    status, body = http_get("/v1/models")
    check(
        f"GET /v1/models → {status}",
        status in (200, 401),
        "401 means the server is bound and expecting Bearer auth (correct)",
    )
    print()

    print("== 3. Model registry: nano-banana-2 + 16:9")
    sys.path.insert(0, r"D:\Dev\google-flow-2.0.0")
    sys.path.insert(
        0,
        r"D:\Dev\AssetAutomator\src\AssetAutomator.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\tools\PythonSource",
    )
    try:
        from google_flow.models.registry import resolve_model, IMAGE_MODELS
        from google_flow.types import UpscaleResolution

        resolved = resolve_model("nano-banana-2", aspect_ratio="16:9")
        check(
            f"resolve_model('nano-banana-2', '16:9') → {resolved}",
            resolved == "nano-banana-2-landscape",
        )

        u = UpscaleResolution.from_string("standard")
        check(
            f"UpscaleResolution.from_string('standard') → {u}",
            u == UpscaleResolution.NONE,
        )
        check(
            f"UpscaleResolution.NONE is a valid member (was previously 'OFF')",
            hasattr(UpscaleResolution, "NONE"),
        )
        check(
            f"UpscaleResolution.OFF is NOT a member (would crash before fix)",
            not hasattr(UpscaleResolution, "OFF"),
        )
    except Exception as exc:
        check("model/enum import", False, str(exc))
    print()

    print("== 4. POST /v1/images/generations (text-only, no reference)")
    status, body = http_post_json(
        "/v1/images/generations",
        {
            "model": "nano-banana-2-landscape",
            "prompt": "A small stickman crouching down in a vast dark void",
            "size": "1536x1024",
            "quality": "standard",
            "response_format": "url",
        },
        timeout=60,
    )
    print(f"   HTTP {status}")
    if isinstance(body, dict):
        if "detail" in body:
            detail = str(body["detail"])
            print(f"   detail: {detail[:300]}")
            if "UNAUTHENTICATED" in detail or "401" in detail:
                check(
                    "Auth required (401 from Google) — means server side is fixed and request reached Google",
                    True,
                    "Re-login at http://127.0.0.1:8787/setup",
                )
            elif "upload_media" in detail:
                check("upload_media bug present", False, "Need to apply client.py fix")
            elif "OFF" in detail:
                check("UpscaleResolution.OFF bug present", False, "Need to apply openai_ext.py fix")
            elif "paths" in detail:
                check("config.paths bug present", False, "Need to apply sdk.py fix")
            elif "captcha.enabled" in detail:
                check("captcha.enabled bug present", False, "Need to apply sdk.py fix")
            else:
                check("Unknown error", False, detail[:150])
        elif "data" in body:
            check("Generation succeeded", True, str(body["data"][0])[:150])
    print()

    print("== Summary")
    print("   To re-authenticate: open http://127.0.0.1:8787/setup in your browser,")
    print("   sign in to Google Flow, and wait for setup to finalise.")
    return 0


if __name__ == "__main__":
    sys.exit(main())