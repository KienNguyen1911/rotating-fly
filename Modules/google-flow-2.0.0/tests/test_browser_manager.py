import os
import json
from unittest.mock import AsyncMock, MagicMock, patch
import pytest
from google_flow.token_updater.browser import BrowserManager

from google_flow.token_updater.config import config

@pytest.fixture
def mock_playwright():
    pw = MagicMock()
    pw.chromium = MagicMock()
    
    # Context mock
    context = AsyncMock()
    context.cookies = AsyncMock(return_value=[{"name": config.session_cookie_name, "value": "mock-sid-token-value"}])
    context.new_page = AsyncMock()
    context.close = AsyncMock()
    context.add_cookies = AsyncMock()
    
    # Page mock
    page = AsyncMock()
    page.url = "https://labs.google"
    page.locator = MagicMock(return_value=MagicMock(count=AsyncMock(return_value=0)))
    page.evaluate = AsyncMock()
    page.wait_for_load_state = AsyncMock()
    page.goto = AsyncMock()
    page.close = AsyncMock()
    
    context.new_page.return_value = page
    pw.chromium.launch_persistent_context = AsyncMock(return_value=context)
    return pw, context, page

@pytest.mark.asyncio
async def test_browser_manager_extract_token_with_profile_dir(tmp_path, mock_playwright):
    pw_mock, context_mock, page_mock = mock_playwright
    
    manager = BrowserManager()
    profile_dir = tmp_path / "custom_profile_dir"
    profile_dir.mkdir()
    
    # We patch playwright.start() and _get_proxy
    with patch("google_flow.token_updater.browser.async_playwright") as mock_ap, \
         patch.object(manager, "_get_proxy", new_callable=AsyncMock) as mock_proxy:
        
        mock_ap.return_value.start = AsyncMock(return_value=pw_mock)
        mock_proxy.return_value = None
        
        # Call extract_token with profile_dir
        token = await manager.extract_token(profile_dir=str(profile_dir))
        
        # Verify it started playwright and launched persistent context with profile_dir
        assert token == "mock-sid-token-value"
        pw_mock.chromium.launch_persistent_context.assert_called_once()
        args, kwargs = pw_mock.chromium.launch_persistent_context.call_args
        assert kwargs["user_data_dir"] == str(profile_dir)
        assert kwargs["headless"] is True

@pytest.mark.asyncio
async def test_browser_manager_peek_token_with_profile_dir(tmp_path, mock_playwright):
    pw_mock, context_mock, page_mock = mock_playwright
    
    manager = BrowserManager()
    profile_dir = tmp_path / "custom_profile_dir"
    profile_dir.mkdir()
    
    with patch("google_flow.token_updater.browser.async_playwright") as mock_ap, \
         patch.object(manager, "_get_proxy", new_callable=AsyncMock) as mock_proxy:
        
        mock_ap.return_value.start = AsyncMock(return_value=pw_mock)
        mock_proxy.return_value = None
        
        token = await manager.peek_token(profile_dir=str(profile_dir))
        assert token == "mock-sid-token-value"
        pw_mock.chromium.launch_persistent_context.assert_called_once()
        args, kwargs = pw_mock.chromium.launch_persistent_context.call_args
        assert kwargs["user_data_dir"] == str(profile_dir)

@pytest.mark.asyncio
async def test_browser_manager_import_export_delete_cookies(tmp_path, mock_playwright):
    pw_mock, context_mock, page_mock = mock_playwright
    
    manager = BrowserManager()
    profile_dir = tmp_path / "custom_profile_dir"
    profile_dir.mkdir()
    
    cookies_json = json.dumps([
        {
            "name": "mock-cookie",
            "value": "mock-value",
            "domain": ".google.com",
            "path": "/",
            "url": "https://.google.com"
        }
    ])
    
    with patch("google_flow.token_updater.browser.async_playwright") as mock_ap, \
         patch.object(manager, "_get_proxy", new_callable=AsyncMock) as mock_proxy:
        
        mock_ap.return_value.start = AsyncMock(return_value=pw_mock)
        mock_proxy.return_value = None
        
        # Test Import
        import_res = await manager.import_cookies(profile_dir=str(profile_dir), cookies_json=cookies_json)
        assert import_res["success"] is True
        context_mock.add_cookies.assert_called_once()
        
        # Test Export
        export_res = await manager.export_cookies(profile_dir=str(profile_dir))
        assert export_res["success"] is True
        assert export_res["cookie_count"] == 1
        
        # Test Delete
        assert os.path.exists(profile_dir)
        await manager.delete_profile_data(profile_dir=str(profile_dir))
        assert not os.path.exists(profile_dir)
