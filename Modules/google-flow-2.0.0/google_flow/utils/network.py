"""
Network connectivity helpers.
"""

from __future__ import annotations

import socket

from google_flow.logging import get_logger

logger = get_logger(__name__)


def check_host_connectivity(
    host: str,
    port: int = 443,
    timeout: float = 8.0,
) -> tuple[bool, str]:
    """Test TCP connectivity to *host:port*.

    Returns ``(reachable, error_message)``.
    """
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True, ""
    except Exception as exc:
        logger.debug("Connectivity check failed for %s:%d — %s", host, port, exc)
        return False, str(exc)
