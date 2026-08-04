from unittest.mock import MagicMock, patch
import pytest

from google_flow.captcha_service.services.browser_captcha import (
    _ensure_browser_installed,
    _is_chrome_installed,
)


def test_is_chrome_installed_paths() -> None:
    # Test standard path detection
    with patch("os.path.exists", return_value=True):
        assert _is_chrome_installed() is True


def test_is_chrome_installed_detect_script() -> None:
    # Test path detection fails but fallback launch detection script succeeds
    with patch("os.path.exists", return_value=False), \
         patch("subprocess.run") as mock_run:
        mock_run.return_value = MagicMock(returncode=0, stdout="chrome_installed")
        assert _is_chrome_installed() is True
        mock_run.assert_called_once()
        args, kwargs = mock_run.call_args
        assert "channel='chrome'" in args[0][2]


@patch("google_flow.captcha_service.services.browser_captcha._run_playwright_install")
@patch("google_flow.captcha_service.services.browser_captcha._is_chrome_installed")
def test_ensure_browser_installed_chrome(mock_is_chrome: MagicMock, mock_install: MagicMock) -> None:
    # If Chrome is already installed, should return True immediately without installing
    mock_is_chrome.return_value = True
    assert _ensure_browser_installed("chrome") is True
    mock_install.assert_not_called()

    # If Chrome is not installed, it should call playwright install for chrome
    mock_is_chrome.return_value = False
    mock_install.return_value = True
    assert _ensure_browser_installed("chrome") is True
    mock_install.assert_called_with(browser_type="chrome", use_mirror=False)


@patch("google_flow.captcha_service.services.browser_captcha._run_playwright_install")
@patch("subprocess.run")
def test_ensure_browser_installed_chromium(mock_run: MagicMock, mock_install: MagicMock) -> None:
    # If Chromium is not installed, should attempt to install it
    mock_run.return_value = MagicMock(returncode=1, stdout="")
    mock_install.return_value = True
    assert _ensure_browser_installed("chromium") is True
    mock_install.assert_called_with(browser_type="chromium", use_mirror=False)


@patch("google_flow.captcha_service.services.browser_captcha._run_playwright_install")
@patch("google_flow.captcha_service.services.browser_captcha._is_chrome_installed")
def test_ensure_browser_installed_default(mock_is_chrome: MagicMock, mock_install: MagicMock) -> None:
    # By default, it should check and install chrome
    mock_is_chrome.return_value = True
    assert _ensure_browser_installed() is True
    mock_is_chrome.assert_called_once()
    mock_install.assert_not_called()

