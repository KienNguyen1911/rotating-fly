import asyncio
import json
import sys
from pathlib import Path
from gemini_webapi import GeminiClient

# Set UTF-8 stdout for Windows terminal compatibility
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8")

async def main():
    # 1. Đọc cookie từ cookies.json
    cookie_file = Path("cookies.json")
    data = json.loads(cookie_file.read_text(encoding="utf-8"))
    cookies_dict = data.get("cookies", data)

    # 2. Khởi tạo GeminiClient với cookie
    client = GeminiClient(verify=False)
    client.cookies = cookies_dict
    await client.init(auto_refresh=False)

    try:
        # 3. Tạo phiên Chat liên kết trực tiếp với Gem "Bedtime Psychology Writer" (ID: 25aa53b3798a)
        chat = client.start_chat(gem="25aa53b3798a")

        prompt = """
Hãy thực hiện nghiên cứu chuyên sâu (Deep Research) và xây dựng kịch bản nội dung cho chủ đề sau:

【Chủ đề】: Thấu Hiểu Bản Ngã & Tiềm Thức (Self-Discovery & Unconscious Mind)
【Phong cách】: Chiêm nghiệm, chiều sâu, vỗ về tâm hồn đêm muộn.
【Nội dung cốt lõi】: Khai thác các khái niệm tâm lý học kinh điển (như Shadow Self, Archetypes của Carl Jung; Vô thức, Ego/Superego của Sigmund Freud) nhưng được đơn giản hóa thành những câu chuyện tự nghiệm dành cho đêm muộn.

【Danh sách Tiêu đề Video/Shorts】:
1. "Vì sao ban đêm lại khiến con người cảm thấy cô đơn hơn?"
2. "Khám phá 'Shadow Self' (Bản ngã bóng tối): Mặt khuất mà bạn luôn giấu kín"
3. "Tại sao chúng ta hay tự tổn thương chính mình trong vô thức?"

Yêu cầu thực hiện:
1. Phân tích góc nhìn tâm lý học & cảm xúc người nghe cho 3 tiêu đề trên.
2. Xây dựng dàn ý chi tiết và viết kịch bản hoàn chỉnh (bao gồm: Lời dẫn Voiceover, gợi ý hình ảnh/bối cảnh thị giác, câu hỏi suy ngẫm cuối video) cho tiêu đề 2: "Khám phá 'Shadow Self'".
"""
        print("[+] Đang tạo Kế hoạch Deep Research cùng Gem Bedtime Psychology Writer...")

        # 4. Đẩy prompt và tạo kế hoạch Deep Research (Plan) thông qua Gem session
        plan = await client.create_deep_research_plan(prompt, chat=chat)
        print(f"\n[+] Đã tạo kế hoạch nghiên cứu: {plan.title or 'Deep Research Plan'}")
        if plan.steps:
            print("Các bước nghiên cứu dự kiến:")
            for step in plan.steps:
                print(f"  - {step}")

        # 5. Kích hoạt tiến trình Deep Research với Gem Session
        await client.start_deep_research(plan, chat=chat)
        print("\n[...] Đang tiến hành Deep Research trên Internet (có thể mất 1-3 phút)...")

        # 6. Chờ cho tới khi Deep Research hoàn tất và thu thập báo cáo kết quả
        result = await client.wait_for_deep_research(plan, poll_interval=10.0, timeout=600.0)

        print("\n================= KẾT QUẢ DEEP RESEARCH & KỊCH BẢN =================\n")
        print(result.text)

    finally:
        await client.close()

if __name__ == "__main__":
    asyncio.run(main())
