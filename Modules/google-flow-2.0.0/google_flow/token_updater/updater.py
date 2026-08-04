"""Token sync service."""
import asyncio
from collections import defaultdict
from datetime import datetime, timedelta
from typing import Any

import httpx

from .browser import browser_manager
from .config import config
from .database import profile_db
from .events import dashboard_events
from .execution import execution_gate
from .logger import logger


class TokenSyncer:
    """Token synchronizer."""

    def __init__(self):
        self._total_sync_count = 0
        self._total_error_count = 0
        self._last_batch_time: datetime | None = None
        self._sync_lock = asyncio.Lock()

    def _normalize_email(self, email: str | None) -> str:
        return (email or "").strip().lower()

    def _parse_time(self, value: Any) -> datetime | None:
        if not value or not isinstance(value, str):
            return None
        try:
            return datetime.fromisoformat(value)
        except ValueError:
            return None

    def _is_sync_overdue(self, profile: dict[str, Any], now: datetime | None = None) -> bool:
        """Profiles that have exceeded the refresh interval or have never been synchronized still need to be fully synchronized."""
        last_sync_time = self._parse_time(profile.get("last_sync_time"))
        if not last_sync_time:
            return True

        current_time = now or datetime.now()
        interval_minutes = max(1, int(config.refresh_interval or 60))
        return current_time - last_sync_time >= timedelta(minutes=interval_minutes)

    def _should_sync_profile(
        self,
        profile: dict[str, Any],
        token_lookup: dict[str, dict[str, Any]],
        now: datetime | None = None,
    ) -> tuple[bool, str]:
        email = self._normalize_email(profile.get("email"))
        if not email:
            return True, "Mailbox not recognized, unable to accurately check upstream status"

        token_info = token_lookup.get(email)
        if not token_info:
            return True, "The Token record does not exist on the target side"

        if not token_info.get("is_active", True):
            return True, "The target token has been deactivated"

        if token_info.get("needs_refresh"):
            return True, "The target determines that it needs to be refreshed"

        if self._is_sync_overdue(profile, now=now):
            return True, f"It has been more than {config.refresh_interval} minutes since the last sync"

        return False, "The target token status is normal."

    def _resolve_target(self, profile: dict[str, Any]) -> tuple[str, str]:
        """Profile-level configuration is preferred, otherwise it falls back to global defaults."""
        flow2api_url = (profile.get("flow2api_url") or config.flow2api_url or "").strip().rstrip("/")
        connection_token = (
            profile.get("connection_token_override") or config.connection_token or ""
        ).strip()
        return flow2api_url, connection_token

    async def _record_sync_result(
        self,
        profile: dict[str, Any],
        target_url: str,
        success: bool | None = None,
        action: str = "",
        message: str = "",
        email: str | None = None,
        status: str | None = None,
    ) -> None:
        event_status = status or ("success" if success else "error")
        await profile_db.record_sync_event(
            profile_id=profile["id"],
            profile_name=profile["name"],
            email=email or profile.get("email"),
            target_url=target_url,
            status=event_status,
            action=action,
            message=message,
        )
        await dashboard_events.publish(
            "sync_result",
            {
                "profile_id": profile["id"],
                "profile_name": profile["name"],
                "status": event_status,
                "target_url": target_url,
                "action": action,
                "message": message,
                "email": email or profile.get("email"),
            },
        )

    async def _update_profile_check_result(
        self,
        profile_id: int,
        result: str,
        checked_at: str | None = None,
        **extra_fields: Any,
    ) -> str:
        timestamp = checked_at or datetime.now().isoformat()
        await profile_db.update_profile(
            profile_id,
            last_check_time=timestamp,
            last_check_result=result,
            **extra_fields,
        )
        return timestamp

    async def _check_tokens_status(
        self,
        flow2api_url: str,
        connection_token: str,
        emails: list[str] | None = None,
    ) -> dict[str, Any]:
        """Query the Token status from the specified Flow2API."""
        if not connection_token:
            return {"success": False, "error": "CONNECTION_TOKEN not configured"}
        if not flow2api_url:
            return {"success": False, "error": "Flow2API address not configured"}

        url = f"{flow2api_url}/api/plugin/check-tokens"

        try:
            async with httpx.AsyncClient(timeout=30) as client:
                payload = {}
                if emails:
                    payload["emails"] = emails

                response = await client.post(
                    url,
                    json=payload,
                    headers={
                        "Content-Type": "application/json",
                        "Authorization": f"Bearer {connection_token}",
                    },
                )

                if response.status_code != 200:
                    return {"success": False, "error": f"HTTP {response.status_code}"}

                data = response.json()
                tokens = data.get("tokens", [])
                return {
                    "success": True,
                    "tokens": tokens,
                }
        except Exception as exc:
            return {"success": False, "error": str(exc)}

    async def sync_profile(self, profile_id: int, *, source: str = "manual") -> dict[str, Any]:
        profile = await profile_db.get_profile(profile_id)
        profile_name = profile.get("name", "") if profile else ""
        async with self._sync_lock, execution_gate.hold(
            "sync_profile",
            profile_id=profile_id,
            profile_name=profile_name,
            source=source,
        ):
            return await self._sync_profile(profile_id)

    async def _sync_profile(self, profile_id: int) -> dict[str, Any]:
        """Synchronize a single Profile."""
        profile = await profile_db.get_profile(profile_id)
        if not profile:
            return {"success": False, "error": "Profile does not exist"}

        flow2api_url, connection_token = self._resolve_target(profile)
        if not flow2api_url or not connection_token:
            error = "The complete Flow2API address or connection token is not configured"
            await self._update_profile_check_result(
                profile_id,
                f"failed: {error}",
                last_sync_time=datetime.now().isoformat(),
                last_sync_result=f"failed: {error}",
                error_count=profile.get("error_count", 0) + 1,
            )
            self._total_error_count += 1
            await self._record_sync_result(profile, flow2api_url, False, message=error)
            return {"success": False, "error": error, "target_url": flow2api_url}

        logger.info(f"[{profile['name']}] Start synchronization -> {flow2api_url}")

        # Priority protocol refresh (used if google_cookies is available)
        token = None
        google_cookies = profile.get("google_cookies")
        if google_cookies:
            from .protocol_login import protocol_loginer
            proxy_url = profile.get("proxy_url") if profile.get("proxy_enabled") else None
            logger.info(f"[{profile['name']}] Protocol refresh...")
            login_result = await protocol_loginer.login(google_cookies, proxy=proxy_url, email=profile.get("email"))
            if login_result.get("success"):
                token = login_result["session_token"]
                logger.info(f"[{profile['name']}] protocol refreshed successfully")
            else:
                logger.warning(f"[{profile['name']}] Protocol refresh failed: {login_result.get('error')}, return to the browser")
                await profile_db.update_profile(profile_id, google_cookies=None)

        if not token:
            token = await browser_manager.extract_token(profile_id)
        if not token:
            error = "Unable to withdraw Token, please log in first"
            await self._update_profile_check_result(
                profile_id,
                last_sync_time=datetime.now().isoformat(),
                result="failed: no token",
                last_sync_result="failed: no token",
                error_count=profile.get("error_count", 0) + 1,
            )
            self._total_error_count += 1
            await self._record_sync_result(profile, flow2api_url, False, message=error)
            return {"success": False, "error": error, "target_url": flow2api_url}

        logger.info(f"[{profile['name']}] extracts Token: {token[:20]}...{token[-10:]}")
        result = await self._push_to_flow2api(token, flow2api_url, connection_token)

        if result["success"]:
            success_result = f"success: {result.get('action', 'synced')}"
            await self._update_profile_check_result(
                profile_id,
                success_result,
                email=result.get("email", profile.get("email")),
                last_sync_time=datetime.now().isoformat(),
                last_sync_result=success_result,
                sync_count=profile.get("sync_count", 0) + 1,
            )
            self._total_sync_count += 1
            logger.info(f"[{profile['name']}] synchronization successful")
            await self._record_sync_result(
                profile,
                flow2api_url,
                True,
                action=result.get("action", "synced"),
                message=result.get("message", ""),
                email=result.get("email"),
            )
        else:
            error_result = f"failed: {result.get('error', 'unknown')}"
            await self._update_profile_check_result(
                profile_id,
                error_result,
                last_sync_time=datetime.now().isoformat(),
                last_sync_result=error_result,
                error_count=profile.get("error_count", 0) + 1,
            )
            self._total_error_count += 1
            logger.error(f"[{profile['name']}] Synchronization failed: {result.get('error')}")
            await self._record_sync_result(
                profile,
                flow2api_url,
                False,
                message=result.get("error", "unknown"),
            )

        return {**result, "target_url": flow2api_url}

    async def sync_all_profiles(self, *, source: str = "manual") -> dict[str, Any]:
        """Synchronize all active Profiles (smart mode: group refresh by target address)."""
        async with self._sync_lock:
            async with execution_gate.hold("sync_all", source=source):
                logger.info("=" * 40)
                logger.info("Start smart sync...")

                self._last_batch_time = datetime.now()
                profiles = await profile_db.get_active_profiles()

                if not profiles:
                    result = {"success": True, "total": 0, "synced": 0, "skipped": 0, "results": []}
                    await dashboard_events.publish("sync_batch", result)
                    return result

                grouped_profiles: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
                invalid_profiles: list[dict[str, Any]] = []

                for profile in profiles:
                    flow2api_url, connection_token = self._resolve_target(profile)
                    if not flow2api_url or not connection_token:
                        invalid_profiles.append(profile)
                        continue
                    grouped_profiles[(flow2api_url, connection_token)].append(profile)

                results: list[dict[str, Any]] = []
                success_count = 0
                error_count = 0
                skipped_count = 0
                now = datetime.now()

                for profile in invalid_profiles:
                    flow2api_url, _ = self._resolve_target(profile)
                    error = "The complete Flow2API address or connection token is not configured"
                    await self._update_profile_check_result(
                        profile["id"],
                        f"failed: {error}",
                        last_sync_time=datetime.now().isoformat(),
                        last_sync_result=f"failed: {error}",
                        error_count=profile.get("error_count", 0) + 1,
                    )
                    self._total_error_count += 1
                    await self._record_sync_result(profile, flow2api_url, False, message=error)
                    results.append(
                        {
                            "profile_id": profile["id"],
                            "profile_name": profile["name"],
                            "success": False,
                            "error": error,
                            "target_url": flow2api_url,
                        }
                    )
                    error_count += 1

                for (flow2api_url, connection_token), target_profiles in grouped_profiles.items():
                    profile_emails = [profile["email"] for profile in target_profiles if profile.get("email")]
                    check_result = await self._check_tokens_status(
                        flow2api_url,
                        connection_token,
                        profile_emails or None,
                    )

                    if not check_result["success"]:
                        logger.warning(
                            f"[{flow2api_url}] Unable to query token status: {check_result.get('error')}, fall back to full synchronization of the target"
                        )
                        group_result = await self._sync_profiles_force(target_profiles)
                        results.extend(group_result["results"])
                        success_count += group_result["success_count"]
                        error_count += group_result["error_count"]
                        continue

                    token_lookup = {
                        self._normalize_email(token.get("email")): token
                        for token in check_result.get("tokens", [])
                        if self._normalize_email(token.get("email"))
                    }

                    for profile in target_profiles:
                        should_sync, reason = self._should_sync_profile(profile, token_lookup, now=now)
                        if should_sync:
                            logger.info(f"[{profile['name']}] satisfies synchronization conditions: {reason}")
                            result = await self._sync_profile(profile["id"])
                            results.append(
                                {
                                    "profile_id": profile["id"],
                                    "profile_name": profile["name"],
                                    **result,
                                }
                            )
                            if result["success"]:
                                success_count += 1
                            else:
                                error_count += 1
                        else:
                            skipped_count += 1
                            logger.info(f"[{profile['name']}] {reason}, skip")
                            await self._update_profile_check_result(
                                profile["id"],
                                f"skipped: {reason}",
                                checked_at=now.isoformat(),
                            )
                            await self._record_sync_result(
                                profile,
                                flow2api_url,
                                action="skipped",
                                message=reason,
                                status="skipped",
                            )

                logger.info(
                    f"Smart synchronization completion: success {success_count}, failure {error_count}, skip {skipped_count}"
                )

                result = {
                    "success": True,
                    "total": len(profiles),
                    "synced": success_count + error_count,
                    "success_count": success_count,
                    "error_count": error_count,
                    "skipped": skipped_count,
                    "results": results,
                }
                await dashboard_events.publish("sync_batch", result)
                return result

    async def _sync_profiles_force(self, profiles: list[dict[str, Any]]) -> dict[str, Any]:
        """Force synchronization of the specified Profile list."""
        results = []
        success_count = 0
        error_count = 0

        for profile in profiles:
            result = await self._sync_profile(profile["id"])
            results.append(
                {
                    "profile_id": profile["id"],
                    "profile_name": profile["name"],
                    **result,
                }
            )
            if result["success"]:
                success_count += 1
            else:
                error_count += 1

        return {
            "results": results,
            "success_count": success_count,
            "error_count": error_count,
        }

    async def _sync_all_profiles_force(self) -> dict[str, Any]:
        """Force synchronization of all profiles (no checking for expiration status)."""
        profiles = await profile_db.get_active_profiles()
        group_result = await self._sync_profiles_force(profiles)

        logger.info(
            f"Force synchronization completed: success {group_result['success_count']}, failure {group_result['error_count']}"
        )

        return {
            "success": True,
            "total": len(profiles),
            "success_count": group_result["success_count"],
            "error_count": group_result["error_count"],
            "results": group_result["results"],
        }

    async def _push_to_flow2api(
        self,
        session_token: str,
        flow2api_url: str,
        connection_token: str,
    ) -> dict[str, Any]:
        """Push to the specified Flow2API."""
        if not connection_token:
            return {"success": False, "error": "CONNECTION_TOKEN not configured"}
        if not flow2api_url:
            return {"success": False, "error": "Flow2API address not configured"}

        url = f"{flow2api_url}/api/plugin/update-token"

        try:
            async with httpx.AsyncClient(timeout=30) as client:
                response = await client.post(
                    url,
                    json={"session_token": session_token},
                    headers={
                        "Content-Type": "application/json",
                        "Authorization": f"Bearer {connection_token}",
                    },
                )

                if response.status_code != 200:
                    return {"success": False, "error": f"HTTP {response.status_code}"}

                data = response.json()
                message = data.get("message", "")
                email = None
                if " for " in message:
                    email = message.split(" for ")[-1]

                return {
                    "success": True,
                    "action": data.get("action"),
                    "message": message,
                    "email": email,
                }
        except Exception as exc:
            return {"success": False, "error": str(exc)}

    def get_status(self) -> dict[str, Any]:
        return {
            "total_sync_count": self._total_sync_count,
            "total_error_count": self._total_error_count,
            "last_batch_time": self._last_batch_time.isoformat() if self._last_batch_time else None,
            "flow2api_url": config.flow2api_url,
            "has_connection_token": bool(config.connection_token),
            "refresh_interval_minutes": config.refresh_interval,
        }


token_syncer = TokenSyncer()
