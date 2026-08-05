"""Token Updater entrypoint v3.3 (lightweight)."""

import uvicorn
from apscheduler.schedulers.asyncio import AsyncIOScheduler
from apscheduler.triggers.interval import IntervalTrigger

from .api import app
from .browser import browser_manager
from .config import config
from .database import profile_db
from .logger import logger
from .updater import token_syncer

scheduler = AsyncIOScheduler()
SYNC_JOB_ID = "token_sync"


async def scheduled_sync():
    """Scheduled synchronization tasks"""
    logger.info("=== Scheduled synchronization task trigger ===")

    profiles = await profile_db.get_active_profiles()
    if not profiles:
        logger.warning("There is no active Profile, skip this synchronization")
        return

    await token_syncer.sync_all_profiles(source="scheduled")


async def startup():
    """Initialize on startup"""
    logger.info("=" * 60)
    logger.info("Flow2API Token Updater v3.2 - Lightweight version")
    logger.info("Cookie import mode")
    logger.info("=" * 60)

    await profile_db.init()
    logger.info("Database initialization completed")

    scheduler.add_job(
        scheduled_sync,
        trigger=IntervalTrigger(minutes=config.refresh_interval),
        id=SYNC_JOB_ID,
        max_instances=1,
        coalesce=True,
        replace_existing=True,
    )
    scheduler.start()

    app.state.scheduler = scheduler
    app.state.sync_job_id = SYNC_JOB_ID

    logger.info(f"The scheduled task has been started: executed every {config.refresh_interval} minutes")
    logger.info(f"Flow2API URL: {config.flow2api_url}")
    logger.info(f"API port: {config.api_port}")
    logger.info("")
    logger.info("Management interface: http://localhost:8002")
    logger.info("")


async def shutdown():
    """Clean up on shutdown"""
    logger.info("Closing...")
    if scheduler.running:
        scheduler.shutdown()
    await browser_manager.stop()


@app.on_event("startup")
async def on_startup():
    await startup()


@app.on_event("shutdown")
async def on_shutdown():
    await shutdown()


def main():
    """main function"""
    uvicorn.run(
        app,
        host="0.0.0.0",
        port=config.api_port,
        log_level="info",
    )


if __name__ == "__main__":
    main()
