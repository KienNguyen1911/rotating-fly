"""
Setup and login management routes.

Provides ``/setup``, ``/setup/status``, ``/setup/finalize``, etc.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

from fastapi import APIRouter, Request
from fastapi.responses import HTMLResponse

from google_flow.api.deps import get_api_key
from google_flow.api.login_manager import login_manager
from google_flow.config import get_config
from google_flow.constants import FLOW_LOGIN_URL
from google_flow.core.client import FlowClient
from google_flow.core.generator import ImageGenerator
from google_flow.logging import get_logger
from google_flow.utils.network import check_host_connectivity

logger = get_logger(__name__)

router = APIRouter(prefix="/setup", tags=["setup"])

_TEMPLATE_PATH = Path(__file__).resolve().parent.parent / "templates" / "setup.html"


def _build_api_info(base_url: str) -> dict[str, Any]:
    return {
        "url": f"{base_url}/v1",
        "api_key": get_api_key(),
        "recommended_models": ["nano banana2", "nano banana pro"],
        "notes": [
            "يرجى إبقاء هذا الكمبيوتر قيد التشغيل وخدمة API تعمل.",
            "يرجى الحفاظ على حالة تسجيل الدخول إلى Google Flow على هذا الكمبيوتر.",
            f"مثال كامل لواجهة OpenAI Chat: {base_url}/v1/chat/completions",
            "لتنسيقات الواجهات الأخرى، يرجى مراجعة ملف README-ar.md",
        ],
    }


def _clear_flow_state() -> None:
    config = get_config()
    session = config.create_session_manager()
    session.clear()


async def _finalize_flow_setup(base_url: str) -> dict[str, Any]:
    st = await login_manager.extract_st()
    config = get_config()
    session = config.create_session_manager()
    session.token.st = st
    session.token.at = ""
    session.token.at_expires = ""
    session.token.project_id = ""
    session.token.user_paygate_tier = "PAYGATE_TIER_NOT_PAID"
    session.save()

    reachable, network_error = check_host_connectivity("labs.google", 443, timeout=8.0)
    if not reachable:
        return {
            "success": False,
            "error_type": "network_unreachable",
            "message": "This computer cannot reach labs.google:443. "
            "Please check proxy, VPN, firewall, DNS, or network routing first.",
            "detail": network_error,
            "api": _build_api_info(base_url),
        }

    client = FlowClient(
        labs_base_url=config.flow.labs_base_url,
        api_base_url=config.flow.api_base_url,
        timeout=config.flow.timeout,
    )
    generator = ImageGenerator(
        client=client,
        session=session,
        max_retries=config.flow.max_retries,
    )

    try:
        async with client:
            credits_info = await generator.check_credits()
            await generator.ensure_project()
        await login_manager.close()
    except Exception as exc:
        detail = str(exc)
        error_type = "flow_request_failed"
        lowered = detail.lower()
        if "failed to connect" in lowered or "could not connect" in lowered:
            error_type = "network_unreachable"
        elif "timeout" in lowered:
            error_type = "network_timeout"

        return {
            "success": False,
            "error_type": error_type,
            "message": "Flow setup failed while requesting Google Flow. "
            "Please verify this computer can access labs.google and retry.",
            "detail": detail,
            "api": _build_api_info(base_url),
        }

    return {
        "success": True,
        "credits": credits_info.credits,
        "tier": credits_info.tier,
        "api": _build_api_info(base_url),
    }


# ── Routes ──────────────────────────────────────────────────────────

@router.get("", response_class=HTMLResponse)
async def setup_page() -> HTMLResponse:
    """Serve the setup UI page."""
    html = _TEMPLATE_PATH.read_text(encoding="utf-8")
    return HTMLResponse(content=html)


@router.get("/status")
async def setup_status(request: Request) -> dict[str, Any]:
    config = get_config()
    session = config.create_session_manager()
    live_login = await login_manager.has_st_cookie()
    persisted_login = bool(
        session.token.st or session.token.at or session.token.project_id
    )
    return {
        "browser_open": await login_manager.is_open(),
        "browser_visible": await login_manager.is_window_visible(),
        "login_detected": live_login or persisted_login,
        "live_login_detected": live_login,
        "persisted_login_detected": persisted_login,
        "has_st": session.token.has_session,
        "has_at": session.token.has_access_token,
        "project_ready": session.token.has_project,
        "login_error": await login_manager.get_cookie_error(),
        "api": _build_api_info(str(request.base_url).rstrip("/")),
    }


@router.post("/open-login")
async def setup_open_login() -> dict[str, Any]:
    await login_manager.open(FLOW_LOGIN_URL)
    return {
        "success": True,
        "message": "Flow login browser opened. Please complete Google login there.",
    }


@router.post("/relogin")
async def setup_relogin() -> dict[str, Any]:
    _clear_flow_state()
    await login_manager.reopen_fresh(FLOW_LOGIN_URL)
    return {
        "success": True,
        "message": "Previous Google Flow login session has been cleared. "
        "Please sign in again in the reopened browser.",
    }


@router.post("/toggle-browser")
async def setup_toggle_browser() -> dict[str, Any]:
    if not await login_manager.is_open():
        return {"success": False, "message": "Login browser is not open yet."}

    visible = await login_manager.is_window_visible()
    changed = await login_manager.set_window_visible(not visible)
    if not changed:
        return {
            "success": False,
            "message": "Could not change browser window state. "
            "Please bring it to front manually.",
        }
    return {
        "success": True,
        "visible": not visible,
        "message": "Browser window is now visible."
        if not visible
        else "Browser window has been hidden.",
    }


@router.post("/finalize")
async def setup_finalize(request: Request) -> dict[str, Any]:
    return await _finalize_flow_setup(str(request.base_url).rstrip("/"))


@router.post("/reset")
async def setup_reset() -> dict[str, Any]:
    _clear_flow_state()
    await login_manager.close()
    return {
        "success": True,
        "message": "Flow state has been reset. You can log in again.",
    }
