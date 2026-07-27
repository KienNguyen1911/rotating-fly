import argparse
import asyncio
import json
import os
import re
import sys
from pathlib import Path

# Fix stdout encoding for Windows terminal
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8")

# Ensure gemini_webapi package is in python path
MODULE_DIR = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(MODULE_DIR / "src"))

from gemini_webapi import GeminiClient
from gemini_webapi.constants import Model
from gemini_webapi.exceptions import GeminiError


def clear_temp_cookie_cache():
    """Clear stale .cached_cookies_*.json files in temp directory to prevent loading expired tokens."""
    try:
        import tempfile
        cache_dir = Path(tempfile.gettempdir()) / "gemini_webapi"
        if cache_dir.exists():
            for f in cache_dir.glob("*.json"):
                try:
                    f.unlink()
                except Exception:
                    pass
    except Exception:
        pass


def load_cookies() -> dict:
    """Read and parse cookies.json file, or fallback to browser cookies if available."""
    cookie_file = MODULE_DIR / "cookies.json"
    if cookie_file.exists():
        data = json.loads(cookie_file.read_text(encoding="utf-8"))
        cookies = data.get("cookies", data)
        if cookies.get("__Secure-1PSID"):
            return cookies

    # Fallback to browser cookies
    try:
        from gemini_webapi.utils.load_browser_cookies import load_browser_cookies, HAS_BC3
        if HAS_BC3:
            browser_cookies = load_browser_cookies(domain_name="google.com", verbose=False)
            if browser_cookies:
                for _, cookie_list in browser_cookies.items():
                    cookies_dict = {c["name"]: c["value"] for c in cookie_list}
                    if cookies_dict.get("__Secure-1PSID"):
                        return cookies_dict
    except Exception:
        pass

    raise RuntimeError("No valid cookies found in cookies.json or browser.")


async def run_bedtime_psychology_workflow(skip_mp3: bool = False, skip_image: bool = False):
    print("==========================================================================")
    print("🚀 BẮT ĐẦU KIỂM THỬ WORKFLOW VIDEO AUTOMATION (todo.md)")
    print(f"   • Config: skip_mp3={skip_mp3}, skip_image={skip_image}")
    print("==========================================================================\n")

    clear_temp_cookie_cache()
    cookies = load_cookies()
    client = GeminiClient(verify=False)
    client.cookies = cookies
    await client.init(auto_refresh=True)

    try:
        # ----------------------------------------------------------------------
        # STEP 1: GỢI Ý & CHỌN CHỦ ĐỀ
        # ----------------------------------------------------------------------
        topic = "think outside the box"
        print(f"[STEP 1] Chủ đề được chọn: {topic}")

        # ----------------------------------------------------------------------
        # STEP 2A: BEDTIME PSYCHOLOGY WRITER + DEEP RESEARCH -> REPORT
        # ----------------------------------------------------------------------
        print("\n[STEP 2A] Đang tra cứu danh sách Gems...")
        psychology_gem_id = "25aa53b3798a"
        gems = []
        try:
            await client.fetch_gems()
            gems = client.gems
            for gem in gems:
                if "psychology" in gem.name.lower() or "bedtime" in gem.name.lower():
                    psychology_gem_id = gem.id
                    print(f"  -> Tìm thấy Custom Gem: '{gem.name}' (ID: {gem.id})")
                    break
            else:
                print(f"  -> Sử dụng Gem ID mặc định: {psychology_gem_id}")
        except Exception as ge:
            print(f"  ⚠️ Lưu ý fetch_gems: {ge}. Sử dụng Gem ID mặc định: {psychology_gem_id}")

        print("\n[+] Đang khởi tạo phiên Chat với Gem + Mode 'Tư duy mở rộng' (gemini-3-flash-thinking)...")
        chat_research = client.start_chat(
            gem=psychology_gem_id,
            model="gemini-3-flash-thinking",  # Extended Thinking Mode
        )

        research_prompt = f"""
Hãy thực hiện nghiên cứu chuyên sâu (Deep Research) về chủ đề sau:

【Chủ đề】: {topic}
【Yêu cầu nghiên cứu】:
1. Phân tích góc nhìn khoa học, tâm lý học và cơ chế sinh học thần kinh (neuroscience).
2. Trích dẫn các công trình nghiên cứu nổi tiếng và góc nhìn triết học hiện đại.
3. Tổng hợp báo cáo nghiên cứu đa chiều và toàn diện.
"""

        print("[+] Đang tạo Kế hoạch Deep Research...")
        plan = await client.create_deep_research_plan(research_prompt, chat=chat_research)
        print(f"  -> Kế hoạch nghiên cứu: '{plan.title or 'Deep Research Plan'}'")
        if plan.steps:
            print("  -> Các bước dự kiến:")
            for s in plan.steps:
                print(f"     • {s}")

        print("\n[...] Đang tiến hành Deep Research ngầm trên Internet (có thể mất 1-3 phút)...")
        await client.start_deep_research(plan, chat=chat_research)
        research_result = await client.wait_for_deep_research(plan, poll_interval=10.0, timeout=600.0)

        report_text = research_result.text or (research_result.final_output.text if research_result.final_output else "")
        
        # Fallback if plan output is short
        if not report_text or len(report_text.strip()) < 300:
            print("[!] Fallback: Đang lấy báo cáo trực tiếp từ phiên Chat...")
            fallback_msg = await chat_research.send_message("Hãy tổng hợp báo cáo nghiên cứu chi tiết theo kế hoạch trên.")
            report_text = fallback_msg.text or ""

        output_report_file = MODULE_DIR / "output_report.md"
        output_report_file.write_text(report_text, encoding="utf-8")
        print(f"\n✅ [STEP 2A COMPLETE] Đã tạo & lưu Báo cáo Nghiên cứu Deep Research ({len(report_text)} ký tự)")
        print(f"📄 Lưu tại file: {output_report_file.resolve()}")

        # ----------------------------------------------------------------------
        # STEP 2B: CHAT TẠO FULL TEXT TRANSCRIPT (ONLY SPOKEN WORDS)
        # ----------------------------------------------------------------------
        print("\n[STEP 2B] Đang gửi Prompt tạo Transcript thuần (spoken words only)...")
        transcript_prompt = "Write a complete, high-quality script of approximately 1600 - 2000 words based on these guidelines. Remember: output ONLY the spoken words."
        
        transcript_msg = await chat_research.send_message(transcript_prompt)
        transcript_text = getattr(transcript_msg, "text", "") or ""
        
        if (not transcript_text or len(transcript_text.strip()) < 50) and chat_research.cid:
            print("[!] Fallback: Đang lấy phản hồi mới nhất từ chat_research.cid...")
            fallback_resp = await client.fetch_latest_chat_response(chat_research.cid)
            if fallback_resp and fallback_resp.text:
                transcript_text = fallback_resp.text

        # Lưu đồng thời vào transcript.txt và output_transcript.md để khớp 100%
        output_transcript_txt = MODULE_DIR / "transcript.txt"
        output_transcript_txt.write_text(transcript_text, encoding="utf-8")
        
        output_transcript_md = MODULE_DIR / "output_transcript.md"
        output_transcript_md.write_text(transcript_text, encoding="utf-8")
        
        print(f"✅ [STEP 2B COMPLETE] Đã tạo & lưu Transcript thuần ({len(transcript_text)} ký tự)")
        print(f"📄 Lưu tại file: {output_transcript_txt.resolve()}")
        print(f"📄 Lưu tại file: {output_transcript_md.resolve()}")

        # ----------------------------------------------------------------------
        # STEP 3: VOICE & SUBTITLES / MOCK MP3 (DỰA TRÊN TRANSCRIPT.TXT)
        # ----------------------------------------------------------------------
        if skip_mp3:
            print("\n[STEP 3] ⏩ Đã bỏ qua bước sinh file Voiceover MP3 (--skip-mp3). Đang tạo dữ liệu Phụ đề Mock SRT...")
        else:
            print("\n[STEP 3] Đang xử lý Voiceover MP3 & dữ liệu Phụ đề (Mock SRT & Timestamps)...")

        sentences = [s.strip() for s in re.split(r"(?<=[.!?])\s+", transcript_text) if s.strip()]
        
        srt_entries = []
        current_seconds = 0.0
        for i, sentence in enumerate(sentences[:15], start=1):  # Take sample sentences for test
            duration = max(3.0, len(sentence.split()) * 0.45)
            start_sec = current_seconds
            end_sec = current_seconds + duration
            current_seconds = end_sec

            start_str = f"{int(start_sec//3600):02d}:{int((start_sec%3600)//60):02d}:{int(start_sec%60):02d},{int((start_sec%1)*1000):03d}"
            end_str = f"{int(end_sec//3600):02d}:{int((end_sec%3600)//60):02d}:{int(end_sec%60):02d},{int((end_sec%1)*1000):03d}"
            
            srt_entries.append(f"{i}\n{start_str} --> {end_str}\n{sentence}\n")

        mock_srt_text = "\n".join(srt_entries)
        output_srt_file = MODULE_DIR / "output_subtitles.srt"
        output_srt_file.write_text(mock_srt_text, encoding="utf-8")
        print(f"✅ [STEP 3 COMPLETE] Đã tạo Mock SRT với {len(srt_entries)} phân đoạn phụ đề.")

        # ----------------------------------------------------------------------
        # STEP 4: SCENE CREATOR GEM -> JSON GENERATION
        # ----------------------------------------------------------------------
        print("\n[STEP 4] Đang gửi Transcript + Phụ đề sang Scene Creator để tạo JSON phân cảnh...")
        
        # Dynamic search for "Scene Creator" gem or fallback to ID f5c0e6766ee4
        scene_gem_id = "f5c0e6766ee4"
        for gem in gems:
            if "scene" in gem.name.lower() or "creator" in gem.name.lower():
                scene_gem_id = gem.id
                print(f"  -> Tìm thấy Custom Gem: '{gem.name}' (ID: {gem.id})")
                break
        
        chat_scene_kwargs = {"gem": scene_gem_id} if scene_gem_id else {}
        chat_scene = client.start_chat(**chat_scene_kwargs)

        scene_prompt = f"""
Bạn là một Scene Creator chuyên nghiệp. Hãy chuyển đổi Transcript và Phụ đề sau đây thành 1 đối tượng JSON phân cảnh duy nhất.

Cấu trúc JSON bắt buộc tuân thủ 100% (chỉ trả về JSON thuần trong khối ```json ```, không kèm câu dẫn):

```json
{{
    "video_title": "Flow State: The Key to Unlocking Your Full Potential (Mastering the Art of Deep Work)",
    "scene_count": 5,
    "scenes": [
        {{
            "scene": 1,
            "id": "scene_001",
            "time": {{
                "start": "00:00:00,000",
                "end": "00:00:05,500",
                "duration": 5.5
            }},
            "transcript": "Nội dung câu nói ở cảnh này...",
            "image_prompt": "Close-up of @character lying awake on its back staring at ceiling. Glowing heavy weights press on its chest. minimalist golden neon line art stickman doodle style, dark 2D lo-fi aesthetic, pitch black background."
        }}
    ]
}}
```

Yêu cầu cho image_prompt:
- Phong cách: Minimalist golden neon line art stickman doodle style, dark 2D lo-fi aesthetic, pitch black background.
- Mô tả trực quan phù hợp với cảm xúc đoạn transcript.

NỘI DUNG PHỤ ĐỀ & TRANSCRIPT:
{mock_srt_text}
"""

        scene_response = await chat_scene.send_message(scene_prompt)
        raw_scene_text = scene_response.text or ""

        # Extract JSON from response
        json_match = re.search(r"```(?:json)?\s*(\{[\s\S]*?\})\s*```", raw_scene_text)
        if json_match:
            json_str = json_match.group(1)
        else:
            json_str = raw_scene_text.strip()

        try:
            scenes_data = json.loads(json_str)
            output_json_file = MODULE_DIR / "output_scenes.json"
            output_json_file.write_text(json.dumps(scenes_data, ensure_ascii=False, indent=4), encoding="utf-8")
            print(f"✅ [STEP 4 COMPLETE] Tạo thành công JSON Phân cảnh!")
            print(f"📄 Lưu tại file: {output_json_file.resolve()}")
            print(f"  • Video Title: {scenes_data.get('video_title')}")
            print(f"  • Tổng số cảnh: {scenes_data.get('scene_count', len(scenes_data.get('scenes', [])))}")
        except json.JSONDecodeError as je:
            print(f"⚠️ Không thể parse JSON trực tiếp: {je}")
            print("Nội dung thô trả về từ AI:")
            print(raw_scene_text[:500])

        # ----------------------------------------------------------------------
        # STEP 5: VERIFY & GENERATE IMAGES FROM PROMPTS
        # ----------------------------------------------------------------------
        if skip_image:
            print("\n[STEP 5] ⏩ Đã bỏ qua bước sinh/kiểm tra ảnh từ image_prompt (--skip-image).")
        else:
            print("\n[STEP 5] Kiểm tra chất lượng các Image Prompts đã tạo:")
            if 'scenes_data' in locals() and "scenes" in scenes_data:
                for sc in scenes_data["scenes"][:3]:
                    print(f"  • Scene #{sc.get('scene')} ({sc.get('id')}): {sc.get('time', {}).get('duration')}s")
                    print(f"    Transcript: {sc.get('transcript')[:60]}...")
                    print(f"    Image Prompt: {sc.get('image_prompt')[:100]}...")
                    print("-" * 50)

        print("\n==========================================================================")
        print("🎉 WORKFLOW TEST HOÀN TẤT THÀNH CÔNG!")
        print("==========================================================================")

    finally:
        await client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Test video automation workflow from todo.md")
    parser.add_argument("--skip-mp3", action="store_true", help="Bỏ qua bước sinh/xử lý âm thanh Voiceover MP3")
    parser.add_argument("--skip-image", action="store_true", help="Bỏ qua bước sinh/kiểm tra ảnh từ image_prompt")
    
    args = parser.parse_args()
    asyncio.run(run_bedtime_psychology_workflow(skip_mp3=args.skip_mp3, skip_image=args.skip_image))
