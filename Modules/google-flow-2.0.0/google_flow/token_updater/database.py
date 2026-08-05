"""Profile database management"""
import asyncio
import os
from contextlib import asynccontextmanager
from datetime import datetime, timedelta
from typing import Any

import aiosqlite

from .config import config


class ProfileDB:
    """Profile database"""

    def __init__(self):
        self.db_path = config.db_path
        os.makedirs(os.path.dirname(self.db_path), exist_ok=True)
        self._write_lock = asyncio.Lock()

    @asynccontextmanager
    async def _connect(self):
        async with aiosqlite.connect(self.db_path, timeout=30.0) as db:
            await db.execute("PRAGMA busy_timeout = 30000")
            await db.execute("PRAGMA foreign_keys = ON")
            yield db

    @asynccontextmanager
    async def _write_connect(self):
        async with self._write_lock, self._connect() as db:
            yield db

    async def init(self):
        """Initialize database"""
        async with self._write_connect() as db:
            await db.execute("""
                CREATE TABLE IF NOT EXISTS profiles (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT UNIQUE NOT NULL,
                    email TEXT,
                    is_logged_in INTEGER DEFAULT 0,
                    is_active INTEGER DEFAULT 1,
                    last_token TEXT,
                    last_token_time TEXT,
                    last_check_time TEXT,
                    last_check_result TEXT,
                    last_sync_time TEXT,
                    last_sync_result TEXT,
                    sync_count INTEGER DEFAULT 0,
                    error_count INTEGER DEFAULT 0,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    remark TEXT,
                    login_account TEXT,
                    login_password TEXT,
                    proxy_url TEXT,
                    proxy_enabled INTEGER DEFAULT 0,
                    flow2api_url TEXT,
                    connection_token_override TEXT
                )
            """)

            # Check and add new columns
            cursor = await db.execute("PRAGMA table_info(profiles)")
            columns = [row[1] for row in await cursor.fetchall()]

            if 'proxy_url' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN proxy_url TEXT")
            if 'login_account' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN login_account TEXT")
            if 'login_password' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN login_password TEXT")
            if 'proxy_enabled' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN proxy_enabled INTEGER DEFAULT 0")
            if 'flow2api_url' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN flow2api_url TEXT")
            if 'connection_token_override' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN connection_token_override TEXT")
            if 'google_cookies' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN google_cookies TEXT")
            if 'last_check_time' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN last_check_time TEXT")
            if 'last_check_result' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN last_check_result TEXT")
            if 'login_method' not in columns:
                await db.execute("ALTER TABLE profiles ADD COLUMN login_method TEXT")

            await db.execute("""
                CREATE TABLE IF NOT EXISTS sync_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    profile_id INTEGER NOT NULL,
                    profile_name TEXT NOT NULL,
                    email TEXT,
                    target_url TEXT,
                    status TEXT NOT NULL,
                    action TEXT,
                    message TEXT,
                    created_at TEXT NOT NULL
                )
            """)
            await db.execute(
                "CREATE INDEX IF NOT EXISTS idx_sync_history_created_at ON sync_history(created_at)"
            )
            await db.execute(
                "CREATE INDEX IF NOT EXISTS idx_sync_history_profile_id ON sync_history(profile_id)"
            )

            await db.commit()

    async def add_profile(
        self,
        name: str,
        remark: str = "",
        login_account: str = "",
        login_password: str = "",
        proxy_url: str = "",
        flow2api_url: str = "",
        connection_token_override: str = "",
    ) -> int:
        """add profile"""
        async with self._write_connect() as db:
            cursor = await db.execute(
                """
                INSERT INTO profiles (
                    name,
                    remark,
                    login_account,
                    login_password,
                    proxy_url,
                    proxy_enabled,
                    flow2api_url,
                    connection_token_override,
                    created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    name,
                    remark,
                    login_account,
                    login_password,
                    proxy_url,
                    1 if proxy_url else 0,
                    flow2api_url,
                    connection_token_override,
                    datetime.now().isoformat(),
                )
            )
            await db.commit()
            return cursor.lastrowid

    async def get_all_profiles(self) -> list[dict[str, Any]]:
        """Get all profiles"""
        async with self._connect() as db:
            db.row_factory = aiosqlite.Row
            cursor = await db.execute("SELECT * FROM profiles ORDER BY id")
            rows = await cursor.fetchall()
            return [dict(row) for row in rows]

    async def get_profile(self, profile_id: int) -> dict[str, Any] | None:
        """Get a single profile"""
        async with self._connect() as db:
            db.row_factory = aiosqlite.Row
            cursor = await db.execute("SELECT * FROM profiles WHERE id = ?", (profile_id,))
            row = await cursor.fetchone()
            return dict(row) if row else None

    async def get_profile_by_name(self, name: str) -> dict[str, Any] | None:
        """Get profile by name"""
        async with self._connect() as db:
            db.row_factory = aiosqlite.Row
            cursor = await db.execute("SELECT * FROM profiles WHERE name = ?", (name,))
            row = await cursor.fetchone()
            return dict(row) if row else None

    async def update_profile(self, profile_id: int, **kwargs):
        """Update profile"""
        if not kwargs:
            return

        fields = ", ".join(f"{k} = ?" for k in kwargs)
        values = list(kwargs.values()) + [profile_id]

        async with self._write_connect() as db:
            await db.execute(f"UPDATE profiles SET {fields} WHERE id = ?", values)
            await db.commit()

    async def delete_profile(self, profile_id: int):
        """delete profile"""
        async with self._write_connect() as db:
            await db.execute("DELETE FROM profiles WHERE id = ?", (profile_id,))
            await db.execute("DELETE FROM sync_history WHERE profile_id = ?", (profile_id,))
            await db.commit()

    async def get_active_profiles(self) -> list[dict[str, Any]]:
        """Get all enabled profiles"""
        async with self._connect() as db:
            db.row_factory = aiosqlite.Row
            cursor = await db.execute(
                "SELECT * FROM profiles WHERE is_active = 1 ORDER BY id"
            )
            rows = await cursor.fetchall()
            return [dict(row) for row in rows]

    async def get_logged_in_profiles(self) -> list[dict[str, Any]]:
        """Get all logged in profiles"""
        async with self._connect() as db:
            db.row_factory = aiosqlite.Row
            cursor = await db.execute(
                "SELECT * FROM profiles WHERE is_logged_in = 1 AND is_active = 1 ORDER BY id"
            )
            rows = await cursor.fetchall()
            return [dict(row) for row in rows]

    async def record_sync_event(
        self,
        profile_id: int,
        profile_name: str,
        email: str | None,
        target_url: str,
        status: str,
        action: str = "",
        message: str = "",
    ) -> None:
        """Record synchronization history for dashboard charts and recent updates."""
        async with self._write_connect() as db:
            await db.execute(
                """
                INSERT INTO sync_history (
                    profile_id,
                    profile_name,
                    email,
                    target_url,
                    status,
                    action,
                    message,
                    created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    profile_id,
                    profile_name,
                    email,
                    target_url,
                    status,
                    action,
                    message,
                    datetime.now().isoformat(),
                ),
            )
            await db.commit()

    async def get_recent_sync_events(self, limit: int = 20) -> list[dict[str, Any]]:
        """Get recent synchronization events."""
        async with self._connect() as db:
            db.row_factory = aiosqlite.Row
            cursor = await db.execute(
                "SELECT * FROM sync_history ORDER BY id DESC LIMIT ?",
                (limit,),
            )
            rows = await cursor.fetchall()
            return [dict(row) for row in rows]

    async def get_sync_events_since(self, hours: int = 24) -> list[dict[str, Any]]:
        """Get sync events over a period of time."""
        since = (datetime.now() - timedelta(hours=hours)).isoformat()
        async with self._connect() as db:
            db.row_factory = aiosqlite.Row
            cursor = await db.execute(
                "SELECT * FROM sync_history WHERE created_at >= ? ORDER BY created_at ASC",
                (since,),
            )
            rows = await cursor.fetchall()
            return [dict(row) for row in rows]


# global instance
profile_db = ProfileDB()
