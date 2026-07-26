import asyncio
import os
import sys
import httpx

# Ensure UTF-8 output encoding for Windows terminal
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

BASE_URL = "http://127.0.0.1:8000/api"
TARGET_GEM_ID = "25aa53b3798a"

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
OUTPUT_FILE_MD = os.path.join(SCRIPT_DIR, "gemini_research_result.md")

PROMPT_TEXT = """Nghiên cứu chi tiết chủ đề: Thấu Hiểu Bản Ngã & Tiềm Thức (Self-Discovery & Unconscious Mind)
(Phong cách: Chiêm nghiệm, chiều sâu, khám phá bản thân)
Nội dung: Khai thác các khái niệm tâm lý học kinh điển (như của Carl Jung, Sigmund Freud...) nhưng được đơn giản hóa thành các câu chuyện tự nghiệm dành cho đêm muộn.

Gợi ý tiêu đề Video/Shorts:
1. "Vì sao ban đêm lại khiến con người cảm thấy cô đơn hơn?"
2. "Khám phá 'Shadow Self' (Bản ngã bóng tối): Mặt khuất mà bạn luôn giấu kín"
3. "Tại sao chúng ta hay tự tổn thương chính mình trong vô thức?"

Lý do chọn: Ban đêm là thời điểm con người sống thật nhất với cảm xúc của mình và dễ mở lòng tiếp nhận những chủ đề sâu lắng.

Yêu cầu: Hãy đóng vai chuyên gia nghiên cứu nội dung và kịch bản, phân tích sâu sắc, chi tiết chủ đề trên, sau đó tạo ra bản Kịch bản (Transcript) đầy đủ, lôi cuốn và lắng đọng cho video.
"""


async def get_gems_list(client: httpx.AsyncClient):
    """1. Lấy danh sách Gem khả dụng từ Gemini WebAPI Server."""
    print("=" * 60)
    print(" 1. ĐANG LẤY DANH SÁCH GEMINI GEMS (GET /api/gems)...")
    print("=" * 60)
    try:
        res = await client.get(f"{BASE_URL}/gems?include_hidden=true")
        if res.status_code != 200:
            print(f"[NOTE] Thông báo lấy danh sách Gems ({res.status_code}): {res.text}")
            print(f"[INFO] Gem ID '{TARGET_GEM_ID}' vẫn sẽ được dùng trực tiếp để tương tác.")
            return []

        gems = res.json()
        print(f"[OK] Đã tìm thấy tổng cộng {len(gems)} Gems:")
        for i, gem in enumerate(gems, 1):
            gem_id = gem.get("id", "")
            gem_name = gem.get("name", "Unknown")
            is_target = " (===> TARGET GEM)" if gem_id == TARGET_GEM_ID else ""
            print(f"  {i:02d}. [{gem_id}] {gem_name}{is_target}")

        return gems
    except Exception as ex:
        print(f"[ERROR] Không thể kết nối tới server Gemini API (localhost:8000): {ex}")
        return []


async def research_topic_with_gem(client: httpx.AsyncClient):
    """2. Gửi request Research chủ đề tới Gem 25aa53b3798a (có fallback khi không có quota Deep Research)."""
    print("\n" + "=" * 60)
    print(f" 2. ĐANG GỬI REQUEST RESEARCH CHỦ ĐỀ TỚI GEM [{TARGET_GEM_ID}]...")
    print("=" * 60)

    for try_deep in [True, False]:
        mode_str = "DEEP RESEARCH" if try_deep else "STANDARD GEM CHAT"
        print(f"\n---> Thử nghiệm với chế độ: {mode_str} (gem_id='{TARGET_GEM_ID}')...")

        payload = {
            "message": PROMPT_TEXT,
            "gem_id": TARGET_GEM_ID,
            "deep_research": try_deep,
            "temporary": False
        }

        try:
            res = await client.post(f"{BASE_URL}/chat", json=payload, timeout=httpx.Timeout(180.0))

            if res.status_code == 200:
                data = res.json()
                session_id = data.get("session_id", "N/A")
                text_content = data.get("text", "")
                thoughts = data.get("thoughts", "")

                print(f"[OK] Đã nhận phản hồi thành công từ Gem [{TARGET_GEM_ID}]!")
                print(f"     Session ID: {session_id}")
                if thoughts:
                    print(f"     Thoughts: {thoughts[:200]}...")

                return text_content
            else:
                print(f"[WARNING] Mode {mode_str} trả về status {res.status_code}: {res.text}")
                if "not eligible for deep research" in res.text and try_deep:
                    print("[INFO] Tài khoản hiện tại không hỗ trợ Deep Research Quota. Đang tự động chuyển sang Standard Gem Chat...")
                    continue
        except Exception as ex:
            print(f"[ERROR] Ngoại lệ khi gọi API (mode {mode_str}): {ex}")

    return None


async def save_result(content: str):
    """3. In ra màn hình và lưu kết quả ra file."""
    print("\n" + "=" * 60)
    print(" 3. KẾT QUẢ NGHIÊN CỨU & KỊCH BẢN NỘI DUNG:")
    print("=" * 60 + "\n")
    print(content)
    print("\n" + "=" * 60)

    header = f"""# BÁO CÁO NGHIÊN CỨU & KỊCH BẢN (GEMINI DEEP RESEARCH)
**Gem ID**: `{TARGET_GEM_ID}`  
**Chủ đề**: Thấu Hiểu Bản Ngã & Tiềm Thức (Self-Discovery & Unconscious Mind)  

---

"""
    full_markdown = header + content

    target_paths = [
        OUTPUT_FILE_MD,
        os.path.join(PROJECT_ROOT, "gemini_research_result.md")
    ]

    for p in target_paths:
        try:
            with open(p, "w", encoding="utf-8") as f:
                f.write(full_markdown)
            print(f"[SUCCESS] Đã lưu kết quả thành công vào: {p}")
        except Exception as e:
            print(f"[WARNING] Không thể lưu file tại {p}: {e}")


async def main():
    async with httpx.AsyncClient(timeout=httpx.Timeout(30.0)) as client:
        # Step 1: Lấy danh sách Gem
        await get_gems_list(client)

        # # Step 2: Research chủ đề
        # result_text = await research_topic_with_gem(client)

        # # Step 3: Lưu kết quả
        # if result_text:
        #     await save_result(result_text)
        # else:
        #     print("[FAILED] Không nhận được nội dung kịch bản từ Gemini API.")


if __name__ == "__main__":
    asyncio.run(main())
