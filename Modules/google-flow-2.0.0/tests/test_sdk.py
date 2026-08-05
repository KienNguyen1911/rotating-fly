from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from google_flow.core.sdk import FlowSDK
from google_flow.types import CreditsInfo


@pytest.fixture
def mock_sdk_generator():
    gen = MagicMock()
    gen.generate = AsyncMock(return_value="output/result.png")
    gen.check_credits = AsyncMock(return_value=CreditsInfo(credits=42, tier="PAYGATE_TIER_PAID"))
    return gen


@pytest.mark.asyncio
async def test_sdk_context_manager(mock_config_paths):
    config_path, token_path = mock_config_paths

    # Write config file to avoid loading user home
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("[flow]\ntimeout=50\n")

    async with FlowSDK(st_token="some-st", config_path=str(config_path)) as sdk:
        assert sdk.st_token == "some-st"
        assert sdk._session is not None
        assert sdk._client is not None
        assert sdk._generator is not None
        assert sdk._session.token.st == "some-st"


@pytest.mark.asyncio
async def test_sdk_generate_and_credits(mock_config_paths, mock_sdk_generator):
    config_path, token_path = mock_config_paths
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("[flow]\ntimeout=50\n")

    # Mock the ImageGenerator instantiation inside SDK
    with patch("google_flow.core.generator.ImageGenerator", return_value=mock_sdk_generator):
        async with FlowSDK(st_token="some-st", config_path=str(config_path)) as sdk:
            res = await sdk.generate("a cat", model="gemini-3.1-flash-image-landscape")
            assert res == "output/result.png"
            mock_sdk_generator.generate.assert_called_once_with(
                prompt="a cat",
                model="gemini-3.1-flash-image-landscape",
                reference_image=None,
                output_path=None,
                upscale="none",
            )

            credits_info = await sdk.check_credits()
            assert credits_info.credits == 42
            assert credits_info.tier == "PAYGATE_TIER_PAID"


@pytest.mark.asyncio
async def test_sdk_select_profile(mock_config_paths):
    config_path, token_path = mock_config_paths
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("[flow]\ntimeout=50\n")

    mock_profile = {
        "id": 1,
        "name": "test-profile",
        "connection_token_override": "override-st-token",
    }

    mock_db = MagicMock()
    mock_db.init = AsyncMock()
    mock_db.get_profile_by_name = AsyncMock(return_value=mock_profile)

    with patch("google_flow.token_updater.database.ProfileDB", return_value=mock_db):
        async with FlowSDK(st_token="initial-st", config_path=str(config_path)) as sdk:
            await sdk.select_profile("test-profile")
            assert sdk._session.token.st == "override-st-token"
            assert sdk._session.token.at == ""  # Cleared AT so it refreshes
            mock_db.get_profile_by_name.assert_called_once_with("test-profile")


@pytest.mark.asyncio
async def test_sdk_db_path_override(mock_config_paths):
    config_path, token_path = mock_config_paths
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("[flow]\ntimeout=50\n")

    mock_db = MagicMock()
    mock_db.init = AsyncMock()
    mock_db.get_all_profiles = AsyncMock(return_value=[])

    with patch("google_flow.token_updater.database.ProfileDB", return_value=mock_db):
        async with FlowSDK(
            st_token="initial-st",
            config_path=str(config_path),
            db_path="custom/path/to/flow.db",
        ) as sdk:
            await sdk.list_profiles()
            from google_flow.token_updater.config import config as updater_config
            assert updater_config.db_path == "custom/path/to/flow.db"


@pytest.mark.asyncio
async def test_sdk_select_profile_dir(mock_config_paths, tmp_path):
    config_path, token_path = mock_config_paths
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("[flow]\ntimeout=50\n")

    profile_dir = tmp_path / "dummy_profile"
    profile_dir.mkdir()

    mock_browser_manager = MagicMock()
    mock_browser_manager.start = AsyncMock()
    mock_browser_manager.stop = AsyncMock()
    mock_browser_manager.extract_token = AsyncMock(return_value="dir-extracted-st-token")

    with patch("google_flow.token_updater.browser.BrowserManager", return_value=mock_browser_manager):
        async with FlowSDK(st_token="initial-st", config_path=str(config_path)) as sdk:
            await sdk.select_profile_dir(str(profile_dir))
            assert sdk._session.token.st == "dir-extracted-st-token"
            assert sdk._session.token.at == ""  # Cleared AT so it refreshes
            mock_browser_manager.extract_token.assert_called_once_with(profile_dir=str(profile_dir))


@pytest.mark.asyncio
async def test_sdk_select_profile_auto_detects_path(mock_config_paths, tmp_path):
    config_path, token_path = mock_config_paths
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("[flow]\ntimeout=50\n")

    profile_dir = tmp_path / "dummy_profile"
    profile_dir.mkdir()

    mock_browser_manager = MagicMock()
    mock_browser_manager.start = AsyncMock()
    mock_browser_manager.stop = AsyncMock()
    mock_browser_manager.extract_token = AsyncMock(return_value="dir-extracted-st-token-2")

    with patch("google_flow.token_updater.browser.BrowserManager", return_value=mock_browser_manager):
        async with FlowSDK(st_token="initial-st", config_path=str(config_path)) as sdk:
            # Pass directory path
            await sdk.select_profile(str(profile_dir))
            assert sdk._session.token.st == "dir-extracted-st-token-2"
            mock_browser_manager.extract_token.assert_called_once_with(profile_dir=str(profile_dir))


@pytest.mark.asyncio
async def test_sdk_is_profile_dir_logged_in(mock_config_paths, tmp_path):
    config_path, token_path = mock_config_paths
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("[flow]\ntimeout=50\n")

    profile_dir = tmp_path / "dummy_profile"
    profile_dir.mkdir()

    mock_browser_manager = MagicMock()
    mock_browser_manager.start = AsyncMock()
    mock_browser_manager.stop = AsyncMock()
    mock_browser_manager.check_login_status = AsyncMock(return_value={"success": True, "is_logged_in": True})

    with patch("google_flow.token_updater.browser.BrowserManager", return_value=mock_browser_manager):
        async with FlowSDK(st_token="initial-st", config_path=str(config_path)) as sdk:
            logged_in = await sdk.is_profile_dir_logged_in(str(profile_dir))
            assert logged_in is True
            mock_browser_manager.check_login_status.assert_called_once_with(profile_dir=str(profile_dir))
