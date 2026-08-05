"""Log module"""
import logging
import os
from logging.handlers import RotatingFileHandler
from google_flow.logging import get_logger

# Get the unified namespace logger of google_flow
logger = get_logger("token_updater")

# Use environment variables or local relative paths as the log directory
log_dir = os.getenv("TOKEN_UPDATER_LOG_DIR", "data/logs")
#
# # Add file handler
# try:
#     os.makedirs(log_dir, exist_ok=True)
#     log_max_bytes = int(os.getenv("LOG_MAX_BYTES", "5242880"))
#     log_backup_count = int(os.getenv("LOG_BACKUP_COUNT", "3"))
#     file_handler = RotatingFileHandler(
#         os.path.join(log_dir, "token_updater.log"),
#         maxBytes=log_max_bytes,
#         backupCount=log_backup_count,
#         encoding="utf-8"
#     )
#     file_handler.setFormatter(logging.Formatter("%(asctime)s [%(levelname)s] %(message)s"))
#     logger.addHandler(file_handler)
# except Exception:
#     pass  # Ignore file log errors
