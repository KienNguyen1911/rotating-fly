"""
Script kiểm tra API sau khi fix lỗi 401 + retry token.

Cách chạy:
    1. Khởi động API server (start-flow-api.bat)
    2. Vào http://127.0.0.1:8787/setup để đăng nhập (lấy token)
    3. Chạy: python test_api_fixed.py
"""

import json
import sys
import time
import urllib.request
import urllib.error

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

BASE_URL = "http://127.0.0.1:8787"
API_KEY = "flow-local-key"
TIMEOUT = 120  # seconds


def api_request(
    endpoint: str, method: str = "GET", data: dict | None = None
) -> dict:
    """Gửi HTTP request đến Flow API và trả về JSON."""
    url = f"{BASE_URL}{endpoint}"
    headers = {
        "Authorization": f"Bearer {API_KEY}",
        "Content-Type": "application/json",
    }
    body = json.dumps(data).encode("utf-8") if data else None
    req = urllib.request.Request(url, data=body, headers=headers, method=method)

    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as err:
        detail = err.read().decode("utf-8")
        print(f"\n  ❌ HTTP {err.code}: {detail[:300]}")
        raise
    except Exception as exc:
        print(f"\n  ❌ Lỗi kết nối: {exc}")
        raise


def test_health():
    print("\n[1/7] Kiểm tra health …", end=" ")
    try:
        resp = api_request("/health")
        assert resp.get("status") == "ok"
        print("✅ OK")
    except Exception:
        print("❌ FAIL - Server chưa chạy? Hãy chạy start-flow-api.bat trước.")
        return False
    return True


def test_list_models():
    print("[2/7] Liệt kê models …", end=" ")
    resp = api_request("/v1/models")
    models = [m["id"] for m in resp.get("data", [])]
    assert len(models) > 0, "Không có model nào!"
    print(f"✅ ({len(models)} models)")
    for m in models[:5]:
        print(f"      • {m}")
    if len(models) > 5:
        print(f"      … và {len(models)-5} models khác")
    return True


def test_basic_generation():
    print("[3/7] Sinh ảnh cơ bản (không reference) …", end=" ")

    resp = api_request(
        "/v1/images/generations",
        method="POST",
        data={
            "prompt": "A cute orange cat sitting on a windowsill, digital art",
            "model": "gemini-3.1-flash-image-landscape",
            "quality": "standard",
            "response_format": "url",
        },
    )
    item = resp["data"][0]
    assert "url" in item or "b64_json" in item
    media_id = item.get("media_id")
    print(f"✅ media_id={media_id}")
    return media_id


def test_edit_image():
    print("[4/7] Edit ảnh (dùng reference_media_id) …", end=" ")
    # Bước 1: sinh ảnh gốc để lấy media_id
    resp = api_request(
        "/v1/images/generations",
        method="POST",
        data={
            "prompt": "A peaceful mountain lake at sunset",
            "model": "gemini-3.1-flash-image-landscape",
            "quality": "standard",
        },
    )
    ref_media_id = resp["data"][0].get("media_id")
    if not ref_media_id:
        print("⚠️ BỎ QUA (không có media_id từ ảnh gốc)")
        return None

    # Bước 2: sinh biến thể dùng reference_media_id
    resp2 = api_request(
        "/v1/images/generations",
        method="POST",
        data={
            "prompt": "Transform to winter with snow covering everything",
            "model": "gemini-3.1-flash-image-landscape",
            "reference_media_id": ref_media_id,
        },
    )
    new_media_id = resp2["data"][0].get("media_id")
    print(f"✅ ref={ref_media_id} → new={new_media_id}")
    return new_media_id


def test_chat_completions():
    print("[5/7] Chat completion (sinh ảnh qua messages) …", end=" ")
    resp = api_request(
        "/v1/chat/completions",
        method="POST",
        data={
            "model": "gemini-3.1-flash-image",
            "messages": [
                {
                    "role": "user",
                    "content": "A beautiful futuristic cityscape at night with neon lights",
                }
            ],
        },
    )
    choices = resp.get("choices", [])
    assert len(choices) > 0, "Không có choice nào!"
    msg = choices[0].get("message", {}).get("content", "")
    assert msg, "Message content rỗng!"
    print(f"✅ (URL: {msg[:60]}…)")
    return True


def test_project_creation():
    print("[6/7] Tạo project trên Google Flow web …", end=" ")
    title = f"Test Project {int(time.time())}"
    resp = api_request("/v1/projects", method="POST", data={"title": title})
    project_id = resp.get("project_id")
    project_url = resp.get("project_url")
    assert project_id, "Không có project_id trong response!"
    print(f"✅ project_id={project_id}")
    print(f"      URL: {project_url}")
    return project_id


def test_generate_with_project():
    print("[7/7] Sinh ảnh với project cụ thể …", end=" ")
    # Tạo project trước
    title = f"Test Gen {int(time.time())}"
    proj_resp = api_request("/v1/projects", method="POST", data={"title": title})
    project_id = proj_resp.get("project_id")
    assert project_id, "Không tạo được project!"

    resp = api_request(
        "/v1/images/generations",
        method="POST",
        data={
            "prompt": "A dragon flying over a medieval castle, cinematic lighting",
            "model": "gemini-3.1-flash-image-landscape",
            "project_id": project_id,
        },
    )
    media_id = resp["data"][0].get("media_id")
    print(f"✅ media_id={media_id} (project={project_id})")
    return True


def main():
    print("=" * 65)
    print("  🧪 KIỂM TRA FLOW API — TOKEN RETRY FIX")
    print("=" * 65)
    print()
    print("⚠️  Yêu cầu:")
    print("   1. API server đang chạy (start-flow-api.bat)")
    print("   2. Đã đăng nhập tại http://127.0.0.1:8787/setup")
    print()

    if not test_health():
        sys.exit(1)

    failures = []

    tests = [
        ("List models", test_list_models),
        ("Basic generation", test_basic_generation),
        ("Edit image", test_edit_image),
        ("Chat completions", test_chat_completions),
        ("Project creation", test_project_creation),
        ("Generate with project", test_generate_with_project),
    ]

    for name, func in tests:
        try:
            func()
        except Exception as e:
            print(f"  ❌ {name} THẤT BẠI: {e}")
            failures.append(name)

    print()
    print("=" * 65)
    if failures:
        print(f"  ⚠️  {len(failures)}/{len(tests)} tests thất bại:")
        for f in failures:
            print(f"     • {f}")
    else:
        print("  🎉 TẤT CẢ TESTS ĐỀU THÀNH CÔNG!")
    print("=" * 65)


if __name__ == "__main__":
    main()
