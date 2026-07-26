from datetime import datetime, timezone
import json
import os
import re
import sys

# Thiết lập UTF-8 cho I/O trên Windows để tránh UnicodeEncodeError
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
        sys.stdin.reconfigure(encoding="utf-8")
    except Exception:
        pass

DEFAULT_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36 Edg/150.0.0.0"
)


def clean_and_parse_json(raw_text: str):
    """Làm sạch chuỗi và parse JSON an toàn, loại bỏ ký tự điều khiển hoặc rác ở cuối."""
    if not raw_text:
        raise ValueError("Dữ liệu đầu vào trống!")

    # Loại bỏ các ký tự điều khiển đặc biệt như Ctrl+D (^D), Ctrl+Z (^Z), EOF chars
    cleaned = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", "", raw_text).strip()

    # Thử parse trực tiếp
    try:
        return json.loads(cleaned)
    except json.JSONDecodeError:
        pass

    # Nếu dán trong terminal bị dính rác ở cuối, tìm substring JSON hợp lệ từ { hoặc [
    first_brace = min(
        (pos for pos in [cleaned.find("{"), cleaned.find("[")] if pos != -1),
        default=-1,
    )

    if first_brace != -1:
        substring = cleaned[first_brace:]
        # Dùng raw_decode để đọc object JSON đầu tiên hợp lệ
        try:
            decoder = json.JSONDecoder()
            obj, _ = decoder.raw_decode(substring)
            return obj
        except json.JSONDecodeError:
            pass

        # Thử cắt từ brace đầu tiên đến brace đóng cuối cùng
        last_brace = max(cleaned.rfind("}"), cleaned.rfind("]"))
        if last_brace > first_brace:
            candidate = cleaned[first_brace : last_brace + 1]
            try:
                return json.loads(candidate)
            except json.JSONDecodeError:
                pass

    raise ValueError("Không thể parse JSON từ dữ liệu đầu vào. Vui lòng kiểm tra định dạng.")


def get_clipboard_text() -> str:
    """Lấy nội dung từ Clipboard trên Windows/macOS/Linux."""
    try:
        import tkinter as tk

        root = tk.Tk()
        root.withdraw()
        text = root.clipboard_get()
        root.destroy()
        return text.strip()
    except Exception:
        return ""


def transform_cookie_data(input_data, user_agent: str = None) -> dict:
    """Chuyển đổi cookie từ nhiều dạng dữ liệu sang dạng Key-Value chuẩn."""
    formatted_cookies = {}

    if isinstance(input_data, list):
        # Dạng danh sách: [{"name": "...", "value": "..."}, ...]
        formatted_cookies = {
            item["name"]: item["value"]
            for item in input_data
            if isinstance(item, dict) and "name" in item and "value" in item
        }
    elif isinstance(input_data, dict):
        raw_cookies = input_data.get("cookies", [])
        if isinstance(raw_cookies, list):
            formatted_cookies = {
                item["name"]: item["value"]
                for item in raw_cookies
                if isinstance(item, dict) and "name" in item and "value" in item
            }
        elif isinstance(raw_cookies, dict):
            formatted_cookies = raw_cookies
        else:
            # Dạng dict trực tiếp {"SID": "...", ...}
            formatted_cookies = {
                k: str(v)
                for k, v in input_data.get("cookies", input_data).items()
                if k not in ("updated_at", "user_agent")
            }

    now_utc = (
        datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"
    )

    return {
        "updated_at": now_utc,
        "user_agent": user_agent or DEFAULT_USER_AGENT,
        "cookies": formatted_cookies,
    }


def main():
    print("=" * 60)
    print("  TOOL CHUYỂN ĐỔI GIAO DIỆN COOKIES CHO GEMINI API")
    print("=" * 60)

    input_json = None
    source_name = ""

    # 1. Thử đọc từ tham số truyền vào (đường dẫn file)
    if len(sys.argv) > 1:
        arg_path = sys.argv[1]
        if os.path.exists(arg_path):
            print(f"[+] Đang đọc dữ liệu từ file: {arg_path}")
            try:
                with open(arg_path, "r", encoding="utf-8") as f:
                    content = f.read()
                input_json = clean_and_parse_json(content)
                source_name = f"File ({arg_path})"
            except Exception as e:
                print(f"❌ Lỗi khi đọc file {arg_path}: {e}")

    # 2. Thử đọc tự động từ Clipboard
    if not input_json:
        clip_text = get_clipboard_text()
        if clip_text and (clip_text.startswith("{") or clip_text.startswith("[")):
            try:
                input_json = clean_and_parse_json(clip_text)
                source_name = "Clipboard (Khay nhớ tạm)"
                print("[+] Đã tự động đọc dữ liệu Cookie JSON từ Clipboard!")
            except Exception:
                input_json = None

    # 3. Nếu chưa có, yêu cầu dán thủ công từ Terminal
    if not input_json:
        print("\nDÁN NỘI DUNG COOKIE JSON VÀO ĐÂY (Nhấn Ctrl+D hoặc Ctrl+Z rồi Enter để hoàn tất):")
        print("-" * 60)
        try:
            raw_input = sys.stdin.read()
        except KeyboardInterrupt:
            print("\nĐã hủy thao tác.")
            return

        if not raw_input.strip():
            print("❌ Dữ liệu đầu vào trống!")
            return

        try:
            input_json = clean_and_parse_json(raw_input)
            source_name = "Terminal (Dán thủ công)"
        except Exception as e:
            print(f"\n❌ Lỗi: Nội dung dán vào không đúng định dạng JSON!\nDetails: {e}")
            return

    # Chuyển đổi dữ liệu
    result = transform_cookie_data(input_json)
    cookie_count = len(result.get("cookies", {}))

    print("\n" + "=" * 60)
    print(f"KẾT QUẢ SAU KHI CHUYỂN ĐỔI (Nguồn: {source_name}):")
    print(f"Tổng số cookie trích xuất được: {cookie_count}")
    print("=" * 60)

    formatted_output = json.dumps(result, indent=2, ensure_ascii=False)
    print(formatted_output)

    # Ghi tự động vào cookies.json nằm cùng thư mục
    script_dir = os.path.dirname(os.path.abspath(__file__))
    output_path = os.path.join(script_dir, "cookies.json")
    try:
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(formatted_output)
        print(f"\n✅ Đã tự động lưu thông tin cookie vào file: {output_path}")

        # Xóa cache cũ trong thư mục temp nếu có
        try:
            import tempfile
            from pathlib import Path
            cache_dir = Path(tempfile.gettempdir()) / "gemini_webapi"
            if cache_dir.exists():
                for cf in cache_dir.glob("*.json"):
                    try:
                        cf.unlink()
                    except Exception:
                        pass
                print("🧹 Đã dọn dẹp file cookie cache cũ trong thư mục tạm!")
        except Exception:
            pass
    except Exception as e:
        print(f"\n⚠️ Không thể ghi vào file cookies.json: {e}")


if __name__ == "__main__":
    main()