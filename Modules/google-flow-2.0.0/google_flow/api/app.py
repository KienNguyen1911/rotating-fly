"""
FastAPI application factory.

Creates and configures the app with all routers, middleware,
exception handlers, and lifespan management.
"""

from __future__ import annotations

import argparse
from contextlib import asynccontextmanager
from pathlib import Path
from typing import TYPE_CHECKING, Any

from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

from google_flow._version import __version__
from google_flow.api.routes import health, openai, setup
from google_flow.logging import get_logger, setup_logging

if TYPE_CHECKING:
    from collections.abc import AsyncIterator

logger = get_logger(__name__)


@asynccontextmanager
async def _lifespan(app: FastAPI) -> AsyncIterator[None]:
    """App lifespan: startup / shutdown hooks."""
    logger.info("Flow Image API v%s starting up", __version__)

    # ── Initialize token_updater ────────────────────────────────────
    from apscheduler.schedulers.asyncio import AsyncIOScheduler
    from apscheduler.triggers.interval import IntervalTrigger

    from google_flow.token_updater.browser import browser_manager
    from google_flow.token_updater.config import config as updater_config
    from google_flow.token_updater.database import profile_db
    from google_flow.token_updater.logger import logger as updater_logger
    from google_flow.token_updater.updater import token_syncer

    updater_logger.info("=" * 60)
    updater_logger.info("Flow2API Token Updater - Integrated Startup")
    updater_logger.info("Cookie Import Mode")
    updater_logger.info("=" * 60)

    await profile_db.init()
    updater_logger.info("Profile database initialized successfully.")

    scheduler = AsyncIOScheduler()
    SYNC_JOB_ID = "token_sync"

    async def scheduled_sync() -> None:
        updater_logger.info("=== Scheduled sync task triggered ===")
        profiles = await profile_db.get_active_profiles()
        if not profiles:
            updater_logger.warning("No active profile, skipping scheduled sync.")
            return
        await token_syncer.sync_all_profiles(source="scheduled")

    scheduler.add_job(
        scheduled_sync,
        trigger=IntervalTrigger(minutes=updater_config.refresh_interval),
        id=SYNC_JOB_ID,
        max_instances=1,
        coalesce=True,
        replace_existing=True,
    )
    scheduler.start()

    app.state.scheduler = scheduler
    app.state.sync_job_id = SYNC_JOB_ID
    updater_logger.info(f"Scheduled sync started: every {updater_config.refresh_interval} minutes")

    # ── Initialize captcha_service ──────────────────────────────────
    from google_flow.captcha_service.main import startup_captcha_service
    await startup_captcha_service()

    yield

    # Cleanup on shutdown
    logger.info("Flow Image API shutting down...")

    # ── Shutdown captcha_service ────────────────────────────────────
    from google_flow.captcha_service.main import shutdown_captcha_service
    await shutdown_captcha_service()

    # ── Shutdown token_updater ──────────────────────────────────────
    updater_logger.info("Flow2API Token Updater shutting down...")
    if scheduler.running:
        scheduler.shutdown()
    await browser_manager.stop()

    from google_flow.api.login_manager import login_manager
    await login_manager.close()

    logger.info("Flow Image API shut down")


def create_app() -> FastAPI:
    """Build and return a fully configured FastAPI application."""
    app = FastAPI(
        title="Flow Image OpenAI-Compatible API",
        version=__version__,
        lifespan=_lifespan,
    )

    # ── CORS ────────────────────────────────────────────────────────
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    # ── Core Routers ────────────────────────────────────────────────
    app.include_router(health.router)
    app.include_router(openai.router)
    app.include_router(setup.router)

    # ── Captcha Service Routers ─────────────────────────────────────
    from google_flow.captcha_service.api import (
        admin as captcha_admin,
    )
    from google_flow.captcha_service.api import (
        cluster as captcha_cluster,
    )
    from google_flow.captcha_service.api import (
        portal as captcha_portal,
    )
    from google_flow.captcha_service.api import (
        service as captcha_service_api,
    )
    from google_flow.captcha_service.api import (
        yescaptcha as captcha_yescaptcha,
    )
    app.include_router(captcha_service_api.router)
    app.include_router(captcha_admin.router)
    app.include_router(captcha_cluster.router)
    app.include_router(captcha_portal.router)
    app.include_router(captcha_yescaptcha.router)

    # ── Token Updater Router ────────────────────────────────────────
    from google_flow.token_updater.api import app as token_updater_app
    app.include_router(token_updater_app.router)

    # ── Static Directories ──────────────────────────────────────────
    static_dir = Path(__file__).resolve().parent / "static"
    if static_dir.exists():
        app.mount("/static", StaticFiles(directory=str(static_dir)), name="static")

    # ── Pages ───────────────────────────────────────────────────────
    @app.get("/", response_class=HTMLResponse, include_in_schema=False)
    async def index_page() -> FileResponse:
        """Serve the Token Updater dashboard."""
        return FileResponse(static_dir / "index.html")

    @app.get("/portal", response_class=HTMLResponse, include_in_schema=False)
    async def portal_page() -> FileResponse:
        """Serve the Captcha Portal."""
        from google_flow.captcha_service.core.config import config as captcha_config
        filename = "subnode.html" if captcha_config.cluster_role == "subnode" else "portal.html"
        return FileResponse(static_dir / filename)

    @app.get("/admin", response_class=HTMLResponse, include_in_schema=False)
    async def admin_page() -> FileResponse:
        """Serve the Captcha Admin panel."""
        return FileResponse(static_dir / "admin.html")

    @app.get("/subnode", response_class=HTMLResponse, include_in_schema=False)
    async def subnode_page() -> FileResponse:
        """Serve the Captcha Subnode panel."""
        return FileResponse(static_dir / "subnode.html")

    @app.get("/captcha", include_in_schema=False)
    async def captcha_root() -> dict[str, Any]:
        """Metadata for the Captcha Service."""
        from google_flow.captcha_service.core.config import config as captcha_config
        return {
            "service": "flow_captcha_service",
            "status": "ok",
            "node": captcha_config.node_name,
            "role": captcha_config.cluster_role,
            "portal": "/portal" if captcha_config.cluster_role != "subnode" else None,
            "public_page": "/" if captcha_config.cluster_role == "subnode" else "/portal",
            "admin": "/admin",
        }

    # ── Exception Handler ───────────────────────────────────────────
    @app.exception_handler(HTTPException)
    async def http_exception_handler(
        _: Request, exc: HTTPException
    ) -> JSONResponse:
        return JSONResponse(
            status_code=exc.status_code,
            content={"error": {"message": exc.detail}},
        )

    return app


# Module-level app instance for uvicorn reference
app = create_app()


def main() -> None:
    """CLI entry point for running the API server."""
    parser = argparse.ArgumentParser(
        description="Run OpenAI-compatible API wrapper for Flow Image CLI"
    )
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8787)
    parser.add_argument(
        "--debug", action="store_true", help="Enable debug logging"
    )
    args = parser.parse_args()

    if args.debug:
        import logging
        setup_logging(level=logging.DEBUG)

    import uvicorn
    uvicorn.run(
        "google_flow.api.app:app",
        host=args.host,
        port=args.port,
        reload=False,
    )


if __name__ == "__main__":
    main()
