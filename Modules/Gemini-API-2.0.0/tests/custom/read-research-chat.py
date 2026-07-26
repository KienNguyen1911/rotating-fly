import asyncio
import json
import sys
from pathlib import Path
from gemini_webapi import GeminiClient

if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8")

async def main():
    cookie_file = Path("cookies.json")
    data = json.loads(cookie_file.read_text(encoding="utf-8"))
    
    client = GeminiClient(verify=False)
    client.cookies = data.get("cookies", data)
    await client.init(auto_refresh=False)

    # Đọc lịch sử câu trả lời từ phiên Deep Research
    chat_history = await client.read_chat("c_f250085ace1595f5")
    if chat_history and chat_history.turns:
        report_text = ""
        for turn in chat_history.turns:
            if turn.role == "model" and len(turn.text) > 300:
                report_text = turn.text
                break
        
        if report_text:
            output_file = Path("deep_research_report.md")
            output_file.write_text(report_text, encoding="utf-8")
            print(f"✅ Đã trích xuất thành công bản báo cáo Deep Research ({len(report_text)} ký tự)!")
            print(f"📄 Đã lưu vào file: {output_file.resolve()}\n")
            print("--- BẮT ĐẦU NỘI DUNG BÁO CÁO (PREVIEW) ---\n")
            print(report_text[:1500])
            print("\n... [Xem đầy đủ trong file deep_research_report.md]")
        else:
            print("Chưa tìm thấy báo cáo dài, in các lượt chat:")
            for turn in chat_history.turns:
                print(f"\n[{turn.role.upper()}]:\n{turn.text}")

    await client.close()

if __name__ == "__main__":
    asyncio.run(main())
