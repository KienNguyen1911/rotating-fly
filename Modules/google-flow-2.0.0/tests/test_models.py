import pytest

from google_flow.exceptions import FlowModelNotFoundError
from google_flow.models.registry import (
    detect_orientation,
    get_model_config,
    list_model_ids,
    list_models,
    resolve_model,
)
from google_flow.types import AspectRatio


def test_model_registry_contents():
    models = list_models()
    assert len(models) > 0
    assert "gemini-3.1-flash-image-landscape" in models

    ids = list_model_ids()
    assert len(ids) == len(models)
    assert "gemini-3.1-flash-image-landscape" in ids

def test_detect_orientation():
    # Test aspect ratio mapping
    assert detect_orientation(aspect_ratio="1:1") == "square"
    assert detect_orientation(aspect_ratio="16:9") == "landscape"
    assert detect_orientation(aspect_ratio="9:16") == "portrait"

    # Test size mapping
    assert detect_orientation(size="1024x1024") == "square"
    assert detect_orientation(size="1024x1536") == "portrait"
    assert detect_orientation(size="1536x1024") == "landscape"

    # Defaults
    assert detect_orientation() == "landscape"

def test_get_model_config():
    config = get_model_config("gemini-3.1-flash-image-landscape")
    assert config.model_name == "NARWHAL"
    assert config.aspect_ratio == AspectRatio.LANDSCAPE

    with pytest.raises(FlowModelNotFoundError):
        get_model_config("nonexistent-model")

def test_resolve_model():
    # Resolve simple exact name
    assert resolve_model("gemini-3.1-flash-image-landscape") == "gemini-3.1-flash-image-landscape"

    # Resolve family with orientation
    assert resolve_model("gemini-3.1-flash-image", aspect_ratio="1:1") == "gemini-3.1-flash-image-square"
    assert resolve_model("gemini-3.1-flash-image", aspect_ratio="9:16") == "gemini-3.1-flash-image-portrait"
    assert resolve_model("gemini-3.1-flash-image", size="1536x1024") == "gemini-3.1-flash-image-landscape"

    # Test alias
    assert resolve_model("nanobanana2", aspect_ratio="9:16") == "nano-banana-2-portrait"

    # Test error cases
    with pytest.raises(FlowModelNotFoundError):
        resolve_model("gemini-3.1-flash-image", aspect_ratio="21:9")  # doesn't support ultrawide
