from unittest.mock import AsyncMock, patch

import pytest

from google_flow.core.generator import ImageGenerator


@pytest.fixture
def mock_generator(mock_client_all_mocked, mock_session_manager):
    return ImageGenerator(
        client=mock_client_all_mocked,
        session=mock_session_manager,
        max_retries=2
    )

@pytest.mark.asyncio
async def test_ensure_access_token_cached(mock_generator):
    mock_generator.session.token.at = "cached-at"
    at = await mock_generator.ensure_access_token()
    assert at == "cached-at"
    mock_generator.client.st_to_at.assert_not_called()

@pytest.mark.asyncio
async def test_ensure_access_token_refresh(mock_generator):
    mock_generator.session.token.at = ""
    mock_generator.client.st_to_at.return_value = {
        "access_token": "new-at",
        "expires": "2026-06-14T22:00:00Z",
    }

    at = await mock_generator.ensure_access_token()
    assert at == "new-at"
    mock_generator.client.st_to_at.assert_called_once_with("mock-st-token")

@pytest.mark.asyncio
async def test_ensure_project_cached(mock_generator):
    mock_generator.session.token.project_id = "cached-project-id"
    proj = await mock_generator.ensure_project()
    assert proj == "cached-project-id"
    mock_generator.client.create_project.assert_not_called()

@pytest.mark.asyncio
async def test_ensure_project_creation(mock_generator):
    mock_generator.session.token.project_id = ""
    mock_generator.client.create_project.return_value = "new-project-id"

    proj = await mock_generator.ensure_project()
    assert proj == "new-project-id"
    mock_generator.client.create_project.assert_called_once_with("mock-st-token")

@pytest.mark.asyncio
@patch("google_flow.core.generator.download_image", new_callable=AsyncMock)
async def test_generate_simple(mock_download, mock_generator):
    mock_generator.session.token.at = "at-token"
    mock_generator.session.token.project_id = "project-id"
    mock_generator.client.generate_image.return_value = (
        {
            "media": [
                {
                    "name": "media-1",
                    "image": {
                        "generatedImage": {
                            "fifeUrl": "https://fife.google/image1"
                        }
                    }
                }
            ]
        },
        "session-1"
    )
    mock_download.return_value = "output/file.png"

    saved_path = await mock_generator.generate(
        prompt="red flower",
        model="gemini-3.1-flash-image-landscape",
        output_path="output/file.png"
    )

    assert saved_path == "output/file.png"
    mock_generator.client.generate_image.assert_called_once()
    mock_download.assert_called_once_with("https://fife.google/image1", "output/file.png")

@pytest.mark.asyncio
async def test_check_credits(mock_generator):
    mock_generator.session.token.at = "at-token"
    mock_generator.client.get_credits.return_value = {
        "credits": 50,
        "userPaygateTier": "PAYGATE_TIER_PAID",
    }

    info = await mock_generator.check_credits()
    assert info.credits == 50
    assert info.tier == "PAYGATE_TIER_PAID"


@pytest.mark.asyncio
async def test_generate_multiple_reference_images(mock_generator):
    mock_generator.session.token.at = "at-token"
    mock_generator.session.token.project_id = "project-id"
    mock_generator.client.upload_image = AsyncMock(side_effect=["media-id-1", "media-id-2"])
    mock_generator.client.generate_image.return_value = (
        {
            "media": [
                {
                    "name": "media-1",
                    "image": {
                        "generatedImage": {
                            "fifeUrl": "https://fife.google/image1"
                        }
                    }
                }
            ]
        },
        "session-1"
    )

    await mock_generator.generate(
        prompt="two cats",
        model="gemini-3.1-flash-image-landscape",
        reference_image=[b"image1", b"image2"],
        output_path=None
    )

    assert mock_generator.client.upload_image.call_count == 2
    mock_generator.client.upload_image.assert_any_call(
        at="at-token",
        image_bytes=b"image1",
        aspect_ratio="IMAGE_ASPECT_RATIO_LANDSCAPE",
        project_id="project-id",
    )
    mock_generator.client.upload_image.assert_any_call(
        at="at-token",
        image_bytes=b"image2",
        aspect_ratio="IMAGE_ASPECT_RATIO_LANDSCAPE",
        project_id="project-id",
    )

    mock_generator.client.generate_image.assert_called_once()
    _, kwargs = mock_generator.client.generate_image.call_args
    assert kwargs["image_inputs"] == [
        {"name": "media-id-1", "imageInputType": "IMAGE_INPUT_TYPE_REFERENCE"},
        {"name": "media-id-2", "imageInputType": "IMAGE_INPUT_TYPE_REFERENCE"},
    ]
