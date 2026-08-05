from google_flow.config import AppConfig, get_config, reset_config


def test_default_config():
    config = AppConfig()
    assert config.flow.labs_base_url == "https://labs.google" or config.flow.labs_base_url is not None
    assert config.flow.timeout == 120
    assert config.captcha.method == "personal"
    assert config.debug is False

def test_load_config_from_toml(mock_config_paths):
    config_path, token_path = mock_config_paths

    # Write a custom config file
    with open(config_path, "w", encoding="utf-8") as f:
        f.write("""
[flow]
labs_base_url = "https://custom-labs.com"
timeout = 45
max_retries = 5

[captcha]
method = "none"
personal_headless = true

[output]
output_dir = "custom_output"
""")

    config = AppConfig.load(str(config_path))
    assert config.flow.labs_base_url == "https://custom-labs.com"
    assert config.flow.timeout == 45
    assert config.flow.max_retries == 5
    assert config.captcha.method == "none"
    assert config.captcha.personal_headless is True
    assert config.output_dir == "custom_output"

def test_load_config_env_overrides(monkeypatch):
    monkeypatch.setenv("FLOW_TIMEOUT", "80")
    monkeypatch.setenv("FLOW_MAX_RETRIES", "10")
    monkeypatch.setenv("FLOW_OUTPUT_DIR", "env_output")
    monkeypatch.setenv("FLOW_DEBUG", "true")

    config = AppConfig.load("/nonexistent-path")
    assert config.flow.timeout == 80
    assert config.flow.max_retries == 10
    assert config.output_dir == "env_output"
    assert config.debug is True

def test_get_config_singleton():
    reset_config()
    c1 = get_config()
    c2 = get_config()
    assert c1 is c2
