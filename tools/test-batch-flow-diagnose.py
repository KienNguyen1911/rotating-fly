"""
End-to-end diagnostic test for Flow Local batch image-gen pipeline.

Usage:
    python Tools/test-batch-flow-diagnose.py                # run all tests
    python Tools/test-batch-flow-diagnose.py --case 3       # only run test #3
    python Tools/test-batch-flow-diagnose.py --no-color     # plain output

What it does:
    Calls 127.0.0.1:8787 directly with requests, mimicking the exact request
    shapes that FlowLocalImageGenProvider.cs sends from the WinUI client.
    Prints the *full* response (status + headers + body) so you can read the
    Python server's error message verbatim, including "Cannot set property…".

Requires:
    pip install requests rich
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Any

try:
    import requests
except ImportError:
    print("ERROR: 'requests' not installed. Run: pip install requests")
    sys.exit(1)

DEFAULT_BASE_URL = "http://127.0.0.1:8787"
DEFAULT_API_KEY = "flow-local-key"
DEFAULT_MODEL = "nano-banana-2-landscape"
DEFAULT_SIZE = "1536x1024"
DEFAULT_QUALITY = "standard"
DEFAULT_PROMPT = "A tiny red apple on a white plate, studio lighting, photorealistic"

# ANSI colours (disabled with --no-color)
GREEN = "\033[32m" if sys.stdout.isatty() else ""
RED = "\033[31m" if sys.stdout.isatty() else ""
YELLOW = "\033[33m" if sys.stdout.isatty() else ""
CYAN = "\033[36m" if sys.stdout.isatty() else ""
RESET = "\033[0m" if sys.stdout.isatty() else ""


def section(title: str) -> None:
    bar = "=" * 70
    print(f"\n{CYAN}{bar}\n  {title}\n{bar}{RESET}")


def info(msg: str) -> None:
    print(f"  {YELLOW}[INFO]{RESET} {msg}")


def pass_(msg: str) -> None:
    print(f"  {GREEN}[PASS]{RESET} {msg}")


def fail(msg: str) -> None:
    print(f"  {RED}[FAIL]{RESET} {msg}")


def pretty_json(payload: Any) -> str:
    try:
        return json.dumps(payload, indent=2, ensure_ascii=False)
    except Exception:
        return repr(payload)


class FlowApi:
    def __init__(self, base: str, api_key: str, timeout: int = 600):
        # base is e.g. http://127.0.0.1:8787  (no /v1)
        self.base = base.rstrip("/")
        self.v1 = f"{self.base}/v1"
        self.api_key = api_key
        self.timeout = timeout
        self.last_project_id: str | None = None
        self.last_reference_media_id: str | None = None

    def _headers(self) -> dict[str, str]:
        return {
            "Authorization": f"Bearer {self.api_key}",
            "Content-Type": "application/json",
            "Accept": "application/json",
        }

    def post(self, path: str, body: dict[str, Any]) -> requests.Response:
        url = f"{self.v1}{path}"
        info(f"POST {url}")
        info(f"body: {json.dumps(body, ensure_ascii=False)[:300]}")
        try:
            return requests.post(url, json=body, headers=self._headers(), timeout=self.timeout)
        except requests.exceptions.ConnectionError as exc:
            fail(f"Connection refused: {exc}")
            info("Is the Python server running? Start with:")
            info("    cd Modules/google-flow-2.0.0 && python start-flow-ext.py")
            raise SystemExit(1)

    def get(self, path: str) -> requests.Response:
        url = f"{self.v1}{path}"
        info(f"GET {url}")
        return requests.get(url, headers=self._headers(), timeout=30)

    def print_full(self, label: str, resp: requests.Response) -> None:
        print(f"  {label}: HTTP {resp.status_code}")
        ct = resp.headers.get("content-type", "")
        if "json" in ct.lower():
            try:
                payload = resp.json()
                print(pretty_json(payload))
            except Exception:
                print(resp.text[:2000])
        else:
            print(resp.text[:2000])


def preflight(api: FlowApi) -> bool:
    section("Pre-flight: server reachability")
    try:
        r = requests.get(f"{api.base}/health", timeout=5)
    except requests.exceptions.ConnectionError as exc:
        fail(f"Cannot reach {api.base}")
        info(f"error: {exc}")
        info("Start the server first: cd Modules/google-flow-2.0.0 && python start-flow-ext.py")
        return False
    info(f"GET {api.base}/health -> {r.status_code}")
    print(r.text[:500])
    if r.status_code != 200:
        fail("Server health check failed")
        return False
    pass_("Server reachable")
    return True


# ── Test cases ───────────────────────────────────────────────────────────────

def case_1_create_project(api: FlowApi) -> bool:
    section("Test 1: POST /v1/projects  (mimics FlowLocalImageGenProvider.CreateProjectAsync)")
    r = api.post("/projects", {"title": "Diagnostic Batch Project"})
    api.print_full("create_project", r)
    if r.status_code == 200:
        try:
            api.last_project_id = r.json().get("project_id")
            info(f"captured project_id = {api.last_project_id}")
        except Exception:
            pass
        return True
    fail("create_project returned non-200")
    return False


def case_2_text_only(api: FlowApi, prompt: str, model: str, size: str, quality: str) -> bool:
    section("Test 2: POST /v1/images/generations  (text-only, no project)")
    body = {
        "model": model,
        "prompt": prompt,
        "size": size,
        "quality": quality,
        "response_format": "url",
    }
    r = api.post("/images/generations", body)
    api.print_full("generate", r)
    return r.status_code == 200


def case_3_with_project(api: FlowApi, prompt: str, model: str, size: str, quality: str) -> bool:
    section("Test 3: POST /v1/images/generations  (with project_id)")
    if not api.last_project_id:
        info("no captured project_id; running case 1 first")
        case_1_create_project(api)
    body = {
        "model": model,
        "prompt": prompt,
        "size": size,
        "quality": quality,
        "response_format": "url",
        "project_id": api.last_project_id,
    }
    r = api.post("/images/generations", body)
    api.print_full("generate (with project)", r)
    return r.status_code == 200


def case_4_with_reference(api: FlowApi, prompt: str, model: str, size: str, quality: str) -> bool:
    section("Test 4: POST /v1/images/generations  (with reference_media_id)")
    if not api.last_reference_media_id:
        info("no captured reference_media_id (run an /images/edits first); skipping")
        return True
    body = {
        "model": model,
        "prompt": prompt,
        "size": size,
        "quality": quality,
        "response_format": "url",
        "reference_media_id": api.last_reference_media_id,
    }
    r = api.post("/images/generations", body)
    api.print_full("generate (with ref)", r)
    return r.status_code == 200


def case_5_raw_model(api: FlowApi, prompt: str, size: str, quality: str) -> bool:
    section("Test 5: POST /v1/images/generations  (raw model name without aspect suffix)")
    body = {
        "model": "nano-banana-2",
        "prompt": prompt,
        "size": size,
        "quality": quality,
        "response_format": "url",
    }
    r = api.post("/images/generations", body)
    api.print_full("generate (raw model)", r)
    return r.status_code == 200


def case_6_quality_hd(api: FlowApi, prompt: str, model: str, size: str) -> bool:
    section("Test 6: POST /v1/images/generations  (quality=hd / 2K upscale)")
    body = {
        "model": model,
        "prompt": prompt,
        "size": size,
        "quality": "hd",
        "response_format": "url",
    }
    r = api.post("/images/generations", body)
    api.print_full("generate (hd)", r)
    return r.status_code == 200


def case_7_size_2k(api: FlowApi, prompt: str, model: str) -> bool:
    section("Test 7: POST /v1/images/generations  (size=2k string)")
    body = {
        "model": model,
        "prompt": prompt,
        "size": "2k",
        "quality": "standard",
        "response_format": "url",
    }
    r = api.post("/images/generations", body)
    api.print_full("generate (size=2k)", r)
    return r.status_code == 200


# ── Entry point ──────────────────────────────────────────────────────────────

def main() -> int:
    p = argparse.ArgumentParser(description="Diagnose Flow Local batch image-gen pipeline")
    p.add_argument("--base", default=DEFAULT_BASE_URL, help=f"Server base URL (default {DEFAULT_BASE_URL})")
    p.add_argument("--api-key", default=DEFAULT_API_KEY, help="Bearer token")
    p.add_argument("--case", type=int, default=0, help="Run only this case id (0 = all)")
    p.add_argument("--prompt", default=DEFAULT_PROMPT)
    p.add_argument("--model", default=DEFAULT_MODEL)
    p.add_argument("--size", default=DEFAULT_SIZE)
    p.add_argument("--quality", default=DEFAULT_QUALITY)
    p.add_argument("--timeout", type=int, default=600, help="HTTP timeout in seconds")
    p.add_argument("--no-color", action="store_true")
    args = p.parse_args()

    global GREEN, RED, YELLOW, CYAN, RESET
    if args.no_color:
        GREEN = RED = YELLOW = CYAN = RESET = ""

    api = FlowApi(args.base, args.api_key, args.timeout)
    info(f"Base URL: {api.base}")
    info(f"API key : {args.api_key[:8]}...")

    if not preflight(api):
        return 1

    cases = [
        (1, lambda: case_1_create_project(api)),
        (2, lambda: case_2_text_only(api, args.prompt, args.model, args.size, args.quality)),
        (3, lambda: case_3_with_project(api, args.prompt, args.model, args.size, args.quality)),
        (4, lambda: case_4_with_reference(api, args.prompt, args.model, args.size, args.quality)),
        (5, lambda: case_5_raw_model(api, args.prompt, args.size, args.quality)),
        (6, lambda: case_6_quality_hd(api, args.prompt, args.model, args.size)),
        (7, lambda: case_7_size_2k(api, args.prompt, args.model)),
    ]

    if args.case > 0:
        cases = [c for c in cases if c[0] == args.case]
        if not cases:
            fail(f"Unknown case id {args.case}")
            return 1

    results: list[tuple[int, bool]] = []
    for cid, fn in cases:
        try:
            ok = bool(fn())
        except SystemExit:
            raise
        except Exception as exc:
            fail(f"case {cid} raised: {exc!r}")
            ok = False
        results.append((cid, ok))

    section("Summary")
    for cid, ok in results:
        marker = f"{GREEN}PASS{RESET}" if ok else f"{RED}FAIL{RESET}"
        print(f"  Case {cid}: {marker}")

    info("Also inspect the Python server terminal for the full traceback.")
    return 0 if all(ok for _, ok in results) else 2


if __name__ == "__main__":
    sys.exit(main())