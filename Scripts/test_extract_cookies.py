import os
import sys
import glob
import json
import base64
import sqlite3
import tempfile
import subprocess

try:
    import win32crypt
    import win32file
    import win32con
    from Crypto.Cipher import AES
    HAS_CRYPTO = True
except ImportError:
    HAS_CRYPTO = False

def copy_locked_file(src_path, dst_path):
    # Method 1: standard copy
    try:
        with open(src_path, "rb") as f_src:
            with open(dst_path, "wb") as f_dst:
                f_dst.write(f_src.read())
        return True
    except Exception:
        pass

    # Method 2: win32file with share flags
    try:
        handle = win32file.CreateFile(
            src_path,
            win32con.GENERIC_READ,
            win32con.FILE_SHARE_READ | win32con.FILE_SHARE_WRITE | win32con.FILE_SHARE_DELETE,
            None,
            win32con.OPEN_EXISTING,
            win32con.FILE_ATTRIBUTE_NORMAL,
            None
        )
        with open(dst_path, "wb") as f_dst:
            while True:
                hr, data = win32file.ReadFile(handle, 64 * 1024)
                if not data:
                    break
                f_dst.write(data)
        win32file.CloseHandle(handle)
        return True
    except Exception:
        pass

    # Method 3: PowerShell Copy-Item
    try:
        ps_cmd = f"Copy-Item -Path '{src_path}' -Destination '{dst_path}' -Force"
        subprocess.run(["powershell", "-Command", ps_cmd], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        return True
    except Exception:
        pass

    return False

def get_chrome_key(profile_dir):
    candidates = [
        os.path.join(profile_dir, "Local State"),
        os.path.join(os.path.dirname(profile_dir), "Local State"),
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "Google", "Chrome", "User Data", "Local State"),
        os.path.join(os.environ.get("LOCALAPPDATA", ""), "Microsoft", "Edge", "User Data", "Local State")
    ]
    for local_state_path in candidates:
        if os.path.exists(local_state_path):
            try:
                with open(local_state_path, "r", encoding="utf-8") as f:
                    local_state = json.load(f)
                encrypted_key = base64.b64decode(local_state["os_crypt"]["encrypted_key"])[5:]
                return win32crypt.CryptUnprotectData(encrypted_key, None, None, None, 0)[1]
            except Exception as e:
                pass
    return None

def decrypt_cookie_value(encrypted_value, key):
    try:
        if encrypted_value.startswith(b"v10") or encrypted_value.startswith(b"v11"):
            nonce = encrypted_value[3:15]
            ciphertext = encrypted_value[15:-16]
            tag = encrypted_value[-16:]
            cipher = AES.new(key, AES.MODE_GCM, nonce)
            return cipher.decrypt_and_verify(ciphertext, tag).decode("utf-8")
        else:
            return win32crypt.CryptUnprotectData(encrypted_value, None, None, None, 0)[1].decode("utf-8")
    except Exception:
        return None

def scan_and_extract_cookies():
    local_app_data = os.environ.get("LOCALAPPDATA", "")
    candidates = [
        r"D:\ChromeProfiles",
        os.path.join(local_app_data, "Google", "Chrome", "User Data"),
        os.path.join(local_app_data, "Microsoft", "Edge", "User Data"),
    ]

    all_cookies = {}

    for base in candidates:
        if not os.path.exists(base):
            continue

        profile_folders = [base] + glob.glob(os.path.join(base, "*"))

        for prof in profile_folders:
            if not os.path.isdir(prof):
                continue

            possible_dbs = [
                os.path.join(prof, "Default", "Network", "Cookies"),
                os.path.join(prof, "Network", "Cookies"),
                os.path.join(prof, "Cookies")
            ]

            for db in possible_dbs:
                if os.path.exists(db):
                    key = get_chrome_key(prof)
                    temp_db = tempfile.mktemp(suffix=".sqlite")
                    if copy_locked_file(db, temp_db):
                        try:
                            conn = sqlite3.connect(temp_db)
                            cursor = conn.cursor()
                            cursor.execute("SELECT name, encrypted_value, value FROM cookies WHERE name LIKE '%Secure-1PSID%' OR name = 'AEC'")
                            rows = cursor.fetchall()
                            for name, enc_val, val in rows:
                                decrypted = decrypt_cookie_value(enc_val, key) if (enc_val and key) else val
                                if decrypted and name not in all_cookies:
                                    all_cookies[name] = decrypted
                            conn.close()
                            if "__Secure-1PSID" in all_cookies:
                                print(f"[SUCCESS] Extracted Gemini Cookies from: {prof}")
                                if os.path.exists(temp_db): os.remove(temp_db)
                                return all_cookies
                        except Exception as e:
                            print(f"Query error on {db}: {e}")
                        finally:
                            if os.path.exists(temp_db):
                                os.remove(temp_db)
                    else:
                        print(f"[LOCKED] Could not copy db file: {db}")

    return all_cookies

if __name__ == "__main__":
    cookies = scan_and_extract_cookies()
    print("\nExtracted Cookies Summary:")
    for k in cookies:
        print(f"  {k}: {cookies[k][:35]}...")
