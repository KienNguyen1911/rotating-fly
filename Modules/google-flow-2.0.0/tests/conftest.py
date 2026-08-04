from unittest.mock import AsyncMock

import pytest

from google_flow.config import AppConfig
from google_flow.core.client import FlowClient


@pytest.fixture
def mock_config_paths(tmp_path, monkeypatch):
    config_dir = tmp_path / ".google-flow"
    config_dir.mkdir()
    config_path = config_dir / "config.toml"
    token_path = config_dir / "token.json"
    monkeypatch.setenv("FLOW_CONFIG", str(config_path))
    return config_path, token_path

@pytest.fixture
def mock_config(mock_config_paths, tmp_path):
    config_path, token_path = mock_config_paths

    # Write a default mock config
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("""
[flow]
labs_base_url = "https://labs.mock"
api_base_url = "https://api.mock"
timeout = 10
""")

    config = AppConfig(
        output_dir=str(tmp_path / "output"),
    )
    return config

@pytest.fixture
def mock_session_manager(mock_config):
    manager = mock_config.create_session_manager()
    manager.token.st = "mock-st-token"
    manager.token.at = "mock-at-token"
    manager.token.project_id = "mock-project-id"
    manager.token.at_expires = "9999999999"
    manager.save()
    return manager

@pytest.fixture
def mock_client():
    client = FlowClient(
        labs_base_url="https://labs.mock",
        api_base_url="https://api.mock",
        timeout=10,
    )
    client._request = AsyncMock()
    return client

@pytest.fixture
def mock_client_all_mocked():
    client = FlowClient(
        labs_base_url="https://labs.mock",
        api_base_url="https://api.mock",
        timeout=10,
    )
    client._request = AsyncMock()
    client.st_to_at = AsyncMock()
    client.create_project = AsyncMock()
    client.get_credits = AsyncMock()
    client.upload_image = AsyncMock()
    client.generate_image = AsyncMock()
    client.upsample_image = AsyncMock()
    return client
