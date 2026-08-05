"""
Structured logging configuration for google_flow.

Provides a pre-configured logger with coloured console output,
optional JSON mode, and consistent formatting across the library.
"""

from __future__ import annotations

import logging
import sys
from typing import Any

_CONFIGURED = False

# Log format constants
_CONSOLE_FORMAT = "%(asctime)s │ %(levelname)-7s │ %(name)s │ %(message)s"
_CONSOLE_DATE_FORMAT = "%H:%M:%S"
_JSON_FORMAT = (
    '{"time":"%(asctime)s","level":"%(levelname)s",'
    '"logger":"%(name)s","msg":"%(message)s"}'
)


class _ColouredFormatter(logging.Formatter):
    """Formatter that adds ANSI colours to console output."""

    _COLOURS = {
        logging.DEBUG: "\033[90m",     # grey
        logging.INFO: "\033[36m",      # cyan
        logging.WARNING: "\033[33m",   # yellow
        logging.ERROR: "\033[31m",     # red
        logging.CRITICAL: "\033[1;31m",  # bold red
    }
    _RESET = "\033[0m"

    def format(self, record: logging.LogRecord) -> str:
        colour = self._COLOURS.get(record.levelno, "")
        record.levelname = f"{colour}{record.levelname}{self._RESET}"
        return super().format(record)


def setup_logging(
    *,
    level: int | str = logging.INFO,
    json_output: bool = False,
    stream: Any | None = None,
) -> None:
    """Configure the ``google_flow`` root logger.

    Safe to call multiple times — subsequent calls are no-ops unless
    you need to reconfigure (set ``force=True`` in the future).

    Parameters
    ----------
    level:
        Minimum log level (``DEBUG``, ``INFO``, …).
    json_output:
        If *True*, emit structured JSON lines instead of coloured text.
    stream:
        Output stream; defaults to *stderr*.
    """
    global _CONFIGURED
    if _CONFIGURED:
        return
    _CONFIGURED = True

    root = logging.getLogger("google_flow")
    root.setLevel(level)
    root.propagate = False

    handler = logging.StreamHandler(stream or sys.stderr)
    handler.setLevel(level)

    if json_output:
        handler.setFormatter(logging.Formatter(_JSON_FORMAT))
    else:
        handler.setFormatter(
            _ColouredFormatter(_CONSOLE_FORMAT, datefmt=_CONSOLE_DATE_FORMAT)
        )

    root.addHandler(handler)


def get_logger(name: str) -> logging.Logger:
    """Return a child logger under the ``google_flow`` namespace.

    Usage::

        from google_flow.logging import get_logger
        logger = get_logger(__name__)
        logger.info("Something happened")
    """
    # Ensure logging is configured at least with defaults
    setup_logging()
    return logging.getLogger(f"google_flow.{name}")
