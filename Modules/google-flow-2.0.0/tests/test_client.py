
import pytest


@pytest.mark.asyncio
async def test_st_to_at(mock_client):
    mock_client._request.return_value = {
        "access_token": "mock-access-token",
        "expires": "2026-06-14T22:00:00Z",
    }

    res = await mock_client.st_to_at("st-token")
    assert res["access_token"] == "mock-access-token"
    mock_client._request.assert_called_once_with(
        "GET",
        "https://labs.mock/auth/session",
        st_token="st-token"
    )

@pytest.mark.asyncio
async def test_create_project(mock_client):
    mock_client._request.return_value = {
        "result": {
            "data": {
                "json": {
                    "result": {
                        "projectId": "new-project-id"
                    }
                }
            }
        }
    }

    project_id = await mock_client.create_project("st-token", "My Project")
    assert project_id == "new-project-id"
    mock_client._request.assert_called_once()
    args, kwargs = mock_client._request.call_args
    assert kwargs["st_token"] == "st-token"
    assert kwargs["json_data"]["json"]["projectTitle"] == "My Project"

@pytest.mark.asyncio
async def test_get_credits(mock_client):
    mock_client._request.return_value = {
        "credits": 120,
        "userPaygateTier": "PAYGATE_TIER_PAID",
    }

    res = await mock_client.get_credits("at-token")
    assert res["credits"] == 120
    mock_client._request.assert_called_once_with(
        "GET",
        "https://api.mock/credits",
        at_token="at-token"
    )

@pytest.mark.asyncio
async def test_upload_image(mock_client):
    mock_client._request.return_value = {
        "media": {
            "name": "uploaded-media-id"
        }
    }

    # Tiny dummy PNG file bytes
    dummy_png = b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15\xc4\x89\x00\x00\x00\nIDATx\x9cc\x00\x01\x00\x00\x05\x00\x01\r\n-\xb4\x00\x00\x00\x00IEND\xaeB`\x82"

    media_id = await mock_client.upload_image("at-token", dummy_png, "IMAGE_ASPECT_RATIO_SQUARE")
    assert media_id == "uploaded-media-id"
    mock_client._request.assert_called_once()
    args, kwargs = mock_client._request.call_args
    assert kwargs["at_token"] == "at-token"
    assert kwargs["json_data"]["mimeType"] == "image/png"

@pytest.mark.asyncio
async def test_generate_image(mock_client):
    mock_client._request.return_value = {"job": "done"}

    res, sid = await mock_client.generate_image(
        at="at-token",
        project_id="proj-id",
        prompt="beautiful tree",
        model_name="NARWHAL",
        aspect_ratio="IMAGE_ASPECT_RATIO_LANDSCAPE"
    )
    assert res["job"] == "done"
    assert len(sid) > 0
    mock_client._request.assert_called_once()
    args, kwargs = mock_client._request.call_args
    assert kwargs["at_token"] == "at-token"
    assert "batchGenerateImages" in args[1]


@pytest.mark.asyncio
async def test_upload_image_cached(mock_client):
    mock_client._request.return_value = {
        "media": {
            "name": "uploaded-media-id"
        }
    }

    # Tiny dummy PNG file bytes
    dummy_png = b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15\xc4\x89\x00\x00\x00\nIDATx\x9cc\x00\x01\x00\x00\x05\x00\x01\r\n-\xb4\x00\x00\x00\x00IEND\xaeB`\x82"

    # First upload
    media_id_1 = await mock_client.upload_image("at-token", dummy_png, "IMAGE_ASPECT_RATIO_SQUARE", project_id="proj-1")
    assert media_id_1 == "uploaded-media-id"
    assert mock_client._request.call_count == 1

    # Second upload with same image and same project should hit cache (no new request)
    media_id_2 = await mock_client.upload_image("at-token", dummy_png, "IMAGE_ASPECT_RATIO_SQUARE", project_id="proj-1")
    assert media_id_2 == "uploaded-media-id"
    assert mock_client._request.call_count == 1

    # Third upload with different project should upload again
    mock_client._request.return_value = {
        "media": {
            "name": "uploaded-media-id-proj-2"
        }
    }
    media_id_3 = await mock_client.upload_image("at-token", dummy_png, "IMAGE_ASPECT_RATIO_SQUARE", project_id="proj-2")
    assert media_id_3 == "uploaded-media-id-proj-2"
    assert mock_client._request.call_count == 2

