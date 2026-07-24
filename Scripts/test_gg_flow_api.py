import asyncio
import sys
import httpx

# Cấu hình encoding hiển thị tiếng Việt trên Windows
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

BASE_URL = "http://127.0.0.1:8787"
HEADERS = {
    "Authorization": "Bearer flow-local-key",
    "Content-Type": "application/json"
}

# Giới hạn số request chạy đồng thời (Concurrency Limit)
MAX_CONCURRENT = 3
semaphore = asyncio.Semaphore(MAX_CONCURRENT)


async def test_create_project(client: httpx.AsyncClient, title: str):
    """Kiểm thử API tạo project mới trên Google Flow (POST /v1/projects)."""
    print(f"1. Đang gửi request tạo Project: '{title}'...")
    res = await client.post(f"{BASE_URL}/v1/projects", json={"title": title})
    if res.status_code != 200:
        print(f"  └─ [ERROR] Status {res.status_code}: {res.text}")
        return None
    data = res.json()
    print(f"  └─ [OK] Project ID: {data.get('project_id')} | URL: {data.get('project_url')}\n")
    return data


async def generate_variant(client: httpx.AsyncClient, prompt: str, ref_media_id: str, project_id: str, index: int):
    """Gửi request sinh 1 ảnh biến thể từ reference_media_id và project_id."""
    async with semaphore:
        print(f"[Task {index}] Đang sinh ảnh biến thể: '{prompt}'...")
        payload = {
            "model": "gemini-3.1-flash-image-landscape",
            "prompt": prompt,
            "reference_media_id": ref_media_id,
            "project_id": project_id
        }
        res = await client.post(f"{BASE_URL}/v1/images/generations", json=payload)
        if res.status_code != 200:
            print(f"  └─ [ERROR Task {index}] Status {res.status_code}: {res.text}")
            return None

        data = res.json()
        item = data["data"][0]
        print(f"  └─ [OK Task {index}] URL: {item['url']} | media_id: {item.get('media_id')} | project_url: {item.get('project_url')}")
        return item


async def main():
    print("=" * 60)
    print("  TEST SUITE: GOOGLE FLOW OPENAI-COMPATIBLE API (1-to-N & PROJECTS)")
    print("=" * 60 + "\n")

    async with httpx.AsyncClient(headers=HEADERS, timeout=httpx.Timeout(120.0)) as client:
        # Step 1: Test Create Project
        proj_data = await test_create_project(client, "Dự Án Test Automated API - Game Art")
        project_id = proj_data.get("project_id") if proj_data else None

        # Step 2: Generate Initial Reference Image
        print("2. Đang sinh ảnh tham chiếu ban đầu...")
        init_payload = {
            "model": "gemini-3.1-flash-image-landscape",
            "prompt": "A peaceful mountain lake at sunrise, digital art"
        }
        if project_id:
            init_payload["project_id"] = project_id

        init_res = await client.post(f"{BASE_URL}/v1/images/generations", json=init_payload)
        if init_res.status_code != 200:
            print(f"[ERROR] Không thể khởi tạo ảnh gốc: {init_res.text}")
            return

        init_data = init_res.json()["data"][0]
        ref_media_id = init_data.get("media_id")
        print(f"  └─ Đã tạo ảnh gốc thành công!")
        print(f"  └─ media_id: {ref_media_id}")
        print(f"  └─ project_url: {init_data.get('project_url')}\n")

        # Step 3: Generate 1-to-N Variants concurrently
        prompts = [
            "Biến khung cảnh thành mùa đông tuyết rơi phủ trắng",
            "Biến khung cảnh thành phong cách Cyberpunk về đêm với đèn neon",
            "Biến khung cảnh thành tranh vẽ màu nước Studio Ghibli",
            "Biến khung cảnh thành hoàng hôn rực rỡ với bầu trời màu cam tím"
        ]

        print(f"3. Đang sinh song song {len(prompts)} ảnh biến thể bằng reference_media_id...")
        tasks = [
            generate_variant(client, prompt, ref_media_id, project_id, i + 1)
            for i, prompt in enumerate(prompts)
        ]
        results = await asyncio.gather(*tasks)

        valid_results = [r for r in results if r is not None]
        print(f"\n🎉 KẾT QUẢ KIỂM THỬ: Hoàn thành {len(valid_results)}/{len(prompts)} ảnh biến thể thành công!")


if __name__ == "__main__":
    asyncio.run(main())
