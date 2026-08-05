"""
FastAPI Application Entrypoint for Extended Google Flow API.
Serves Skeuomorphic Web Console & OpenAI Compatible API.
"""

from __future__ import annotations

from contextlib import asynccontextmanager
from pathlib import Path
from typing import AsyncIterator

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse
from fastapi.staticfiles import StaticFiles

from google_flow.api.routes import health, setup
from google_flow.logging import get_logger

from google_flow_ext.api.routes import openai_ext

logger = get_logger(__name__)

STATIC_DIR = Path(__file__).resolve().parent.parent / "static"


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    """Lifespan context manager for startup and shutdown events."""
    logger.info("Starting Google Flow Extended API server with Skeuomorphic UI …")
    yield
    logger.info("Stopping Google Flow Extended API server …")


def create_app() -> FastAPI:
    """Create and configure the extended FastAPI application."""
    app = FastAPI(
        title="Google Flow Extended API",
        description="OpenAI-compatible image generation API extending Google Flow core",
        version="2.0.0-ext",
        lifespan=lifespan,
    )

    # CORS
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    # Mount Skeuomorphic Static Assets
    if STATIC_DIR.exists():
        app.mount("/ext-static", StaticFiles(directory=str(STATIC_DIR)), name="ext-static")

    # Skeuomorphic HTML Page Routes (Overriding flat UI routes)
    @app.get("/", response_class=HTMLResponse, include_in_schema=False)
    async def index_page() -> FileResponse:
        return FileResponse(STATIC_DIR / "index.html")

    @app.get("/setup", response_class=HTMLResponse, include_in_schema=False)
    async def setup_page() -> FileResponse:
        return FileResponse(STATIC_DIR / "setup.html")

    @app.get("/admin", response_class=HTMLResponse, include_in_schema=False)
    async def admin_page() -> FileResponse:
        return FileResponse(STATIC_DIR / "admin.html")

    @app.get("/portal", response_class=HTMLResponse, include_in_schema=False)
    async def portal_page() -> FileResponse:
        return FileResponse(STATIC_DIR / "portal.html")

    # Include original base routes & setup API endpoints (/setup/status, /setup/finalize, etc.)
    app.include_router(health.router)
    app.include_router(setup.router)

    # Include extended routes (/v1/images/generations, /v1/images/edits, /v1/projects)
    app.include_router(openai_ext.router)

    return app


app = create_app()
