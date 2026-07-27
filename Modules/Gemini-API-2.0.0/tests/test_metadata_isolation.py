"""
Quick test to verify ChatSession metadata isolation after .copy() fix.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "src"))

from gemini_webapi.constants import DEFAULT_METADATA
from gemini_webapi.client import GeminiClient


def main():
    client = GeminiClient()

    s1 = client.start_chat()
    s2 = client.start_chat()

    m1 = s1._ChatSession__metadata
    m2 = s2._ChatSession__metadata

    print(f"[Before] Session 1 metadata: {m1}")
    print(f"[Before] Session 2 metadata: {m2}")
    print(f"[Before] DEFAULT_METADATA:  {DEFAULT_METADATA}")

    # Simulate what happens when Google responds with new metadata
    # (the metadata setter mutates in-place)
    google_response_metadata = ["c_abc123", "r_xyz789", "rc_001", None, None, None, None, None, None, "ctx"]
    s1.metadata = google_response_metadata

    print(f"\n[After s1 response] Session 1 metadata: {m1}")
    print(f"[After s1 response] Session 2 metadata: {m2}")
    print(f"[After s1 response] DEFAULT_METADATA:  {DEFAULT_METADATA}")

    assert m1 is not m2, "FAIL: s1 and s2 share the same metadata list object!"
    assert m2 is not DEFAULT_METADATA, "FAIL: s2 metadata IS DEFAULT_METADATA (should be a copy)!"
    assert DEFAULT_METADATA == ["", "", "", None, None, None, None, None, None, ""], \
        f"FAIL: DEFAULT_METADATA was mutated! Got: {DEFAULT_METADATA}"
    assert m2[0] == "", f"FAIL: s2 cid contaminated: {m2[0]}"
    assert m1[0] == "c_abc123", f"FAIL: s1 cid not set correctly: {m1[0]}"

    print("\n✅ ALL TESTS PASSED: Sessions are properly isolated!")


if __name__ == "__main__":
    main()
