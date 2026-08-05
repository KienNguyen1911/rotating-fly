"""Token Updater Configuration v3.1"""
import contextlib
import json
import os

from pydantic import BaseModel

from google_flow.utils.parsing import parse_bool, parse_int

PERSIST_KEYS = ("flow2api_url", "connection_token", "refresh_interval")


def _get_env(name: str) -> str | None:
    value = os.getenv(name)
    return value if value else None


def _load_persisted(path: str) -> dict:
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        return data if isinstance(data, dict) else {}
    except Exception:
        return {}


def _save_persisted(path: str, data: dict) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=True, indent=2)
    with contextlib.suppress(Exception):
        os.chmod(path, 0o600)


class Config(BaseModel):
    admin_password: str
    api_key: str
    flow2api_url: str
    connection_token: str
    refresh_interval: int
    enable_vnc: bool
    profiles_dir: str = "data/profiles"
    labs_url: str = "https://labs.google/fx/tools/flow"
    login_url: str = "https://labs.google/fx/api/auth/signin/google"
    session_cookie_name: str = "__Secure-next-auth.session-token"
    api_port: int
    db_path: str = "data/flow.db"
    session_ttl_minutes: int
    config_file: str

    def save(self) -> None:
        data = {key: getattr(self, key) for key in PERSIST_KEYS}
        _save_persisted(self.config_file, data)


def _build_config() -> Config:
    config_file = _get_env("CONFIG_FILE") or "data/updater_config.json"
    persisted = _load_persisted(config_file)

    flow2api_url = _get_env("FLOW2API_URL") or persisted.get("flow2api_url") or "http://127.0.0.1:8787"
    connection_token = _get_env("CONNECTION_TOKEN") or persisted.get("connection_token", "")
    refresh_interval = parse_int(_get_env("REFRESH_INTERVAL") or str(persisted.get("refresh_interval", 60)), 60)
    enable_vnc = parse_bool(_get_env("ENABLE_VNC"), default=False)

    return Config(
        admin_password=_get_env("ADMIN_PASSWORD") or "",
        api_key=_get_env("API_KEY") or "",
        flow2api_url=flow2api_url,
        connection_token=connection_token,
        refresh_interval=refresh_interval,
        enable_vnc=enable_vnc,
        api_port=parse_int(_get_env("API_PORT"), 8002),
        session_ttl_minutes=parse_int(_get_env("SESSION_TTL_MINUTES"), 1440),
        config_file=config_file,
    )


config = _build_config()
