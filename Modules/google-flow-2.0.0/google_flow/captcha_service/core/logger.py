from __future__ import annotations

import logging
from typing import Any

from google_flow.logging import get_logger
from .config import config


class DebugLogger:
    def __init__(self):
        self._logger = get_logger("captcha_service")
        self._logger.setLevel(getattr(logging, config.log_level, logging.INFO))

    def log_info(self, message: str, *args: Any):
        self._logger.info(message, *args)

    def log_warning(self, message: str, *args: Any):
        self._logger.warning(message, *args)

    def log_error(self, message: str, *args: Any):
        self._logger.error(message, *args)

    def log_debug(self, message: str, *args: Any):
        self._logger.debug(message, *args)

    def refresh_level(self):
        self._logger.setLevel(getattr(logging, config.log_level, logging.INFO))


debug_logger = DebugLogger()

