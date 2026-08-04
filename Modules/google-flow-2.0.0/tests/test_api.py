import os
from unittest.mock import ANY, AsyncMock, MagicMock, patch

from fastapi.testclient import TestClient

from google_flow.api.app import app

client = TestClient(app)

def test_health_endpoint():
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}

def test_list_models_unauthorized():
    response = client.get("/v1/models")
    assert response.status_code == 401

def test_list_models_authorized(monkeypatch):
    monkeypatch.setenv("FLOW_API_KEY", "test-key")
    response = client.get("/v1/models", headers={"Authorization": "Bearer test-key"})
    assert response.status_code == 200
    data = response.json()
    assert "data" in data
    assert any(m["id"] == "gemini-3.1-flash-image-landscape" for m in data["data"])

@patch("google_flow.api.routes.openai.ImageGenerator")
def test_generate_image_endpoint(mock_generator_class, monkeypatch):
    monkeypatch.setenv("FLOW_API_KEY", "test-key")

    # Setup mock generator instance
    mock_generator = MagicMock()
    mock_generator.generate = AsyncMock(return_value="output/test_image.png")
    mock_generator.client = MagicMock()
    mock_generator.client.__aenter__ = AsyncMock(return_value=mock_generator.client)
    mock_generator.client.__aexit__ = AsyncMock(return_value=None)
    mock_generator_class.return_value = mock_generator

    with patch("google_flow.api.routes.openai.Path") as mock_path_class:
        mock_path = MagicMock()
        mock_path.name = "test_image.png"
        mock_path_class.return_value = mock_path

        response = client.post(
            "/v1/images/generations",
            headers={"Authorization": "Bearer test-key"},
            json={
                "prompt": "beautiful cat",
                "model": "gemini-3.1-flash-image-landscape",
                "size": "1024x768"
            }
        )

    assert response.status_code == 200
    res_data = response.json()
    assert "data" in res_data
    assert "url" in res_data["data"][0]
    assert res_data["data"][0]["url"].endswith("/v1/files/test_image.png")

@patch("google_flow.api.routes.openai.ImageGenerator")
def test_chat_completions_endpoint(mock_generator_class, monkeypatch):
    monkeypatch.setenv("FLOW_API_KEY", "test-key")

    mock_generator = MagicMock()
    mock_generator.generate = AsyncMock(return_value="output/chat_image.png")
    mock_generator.client = MagicMock()
    mock_generator.client.__aenter__ = AsyncMock(return_value=mock_generator.client)
    mock_generator.client.__aexit__ = AsyncMock(return_value=None)
    mock_generator_class.return_value = mock_generator

    with patch("google_flow.api.routes.openai.Path") as mock_path_class:
        mock_path = MagicMock()
        mock_path.name = "chat_image.png"
        mock_path_class.return_value = mock_path

        response = client.post(
            "/v1/chat/completions",
            headers={"Authorization": "Bearer test-key"},
            json={
                "model": "gemini-3.1-flash-image",
                "messages": [
                    {"role": "user", "content": "paint a blue car\npreferred size: 1024x1024"}
                ]
            }
        )

    assert response.status_code == 200
    res_data = response.json()
    assert "choices" in res_data
    assert res_data["choices"][0]["message"]["role"] == "assistant"
    # Ensure correct prompt extract and parameters
    mock_generator.generate.assert_called_once_with(
        "paint a blue car",
        model="gemini-3.1-flash-image-square",
        output_path=ANY,
        upscale="none",
        reference_image=None
    )


def test_unified_routes():
    from google_flow.token_updater.database import profile_db
    print("\nTEST DEBUG - db_path:", os.path.abspath(profile_db.db_path))
    print("TEST DEBUG - cwd:", os.getcwd())
    print("TEST DEBUG - file exists:", os.path.exists(profile_db.db_path))
    # Wrap in TestClient context manager to run startup lifespan (database init)
    with TestClient(app) as local_client:
        # 1. Test root path serves the Token Updater dashboard index.html
        response = local_client.get("/")
        assert response.status_code == 200
        assert "Flow2API" in response.text or "static/app.js" in response.text

        # 2. Test Captcha Portal Page
        response = local_client.get("/portal")
        assert response.status_code == 200
        assert "portal.js" in response.text

        # 3. Test Captcha Admin Panel Page
        response = local_client.get("/admin")
        assert response.status_code == 200
        assert "admin.html" in response.text or "flow_captcha_service" in response.text

        # 4. Test Captcha metadata root
        response = local_client.get("/captcha")
        assert response.status_code == 200
        data = response.json()
        assert data["service"] == "flow_captcha_service"
        assert "status" in data

        # 5. Test Token Updater Profiles list endpoint (accessible anonymously when no ADMIN_PASSWORD is set)
        response = local_client.get("/api/profiles")
        assert response.status_code == 200
        assert isinstance(response.json(), list)

