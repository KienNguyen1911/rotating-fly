import argparse
import sys
import subprocess

try:
    from youtube_transcript_api import YouTubeTranscriptApi, TranscriptsDisabled, NoTranscriptFound, CouldNotRetrieveTranscript
    from youtube_transcript_api.proxies import GenericProxyConfig
except ImportError:
    try:
        subprocess.check_call([sys.executable, "-m", "pip", "install", "youtube-transcript-api", "--quiet"])
        from youtube_transcript_api import YouTubeTranscriptApi, TranscriptsDisabled, NoTranscriptFound, CouldNotRetrieveTranscript
        from youtube_transcript_api.proxies import GenericProxyConfig
    except Exception as err:
        sys.stderr.write(f"ModuleNotFoundError: Missing 'youtube_transcript_api'. Auto-install failed: {err}\n")
        sys.exit(1)

def get_transcript_for_api(ytt_api, video_id, lang_code=None):
    transcript_list = ytt_api.list(video_id)
    transcript = None
    if lang_code:
        target_lang = lang_code.strip().lower()
        if " - " in target_lang:
            target_lang = target_lang.split(" - ")[1].strip().lower()
        elif target_lang.startswith("viet"):
            target_lang = "vi"
        elif target_lang.startswith("eng"):
            target_lang = "en"
        
        try:
            transcript = transcript_list.find_transcript([target_lang])
        except Exception:
            pass

    if not transcript:
        for t in transcript_list:
            transcript = t
            break

    if transcript:
        fetched = transcript.fetch()
        text_segments = [snippet.text for snippet in fetched if snippet.text]
        return " ".join(text_segments).strip()
    return None

def main():
    parser = argparse.ArgumentParser(description="Fetch YouTube transcript via youtube-transcript-api fallback.")
    parser.add_argument("--video-id", required=True, help="YouTube Video ID")
    parser.add_argument("--lang", default=None, help="Optional target language code")
    parser.add_argument("--proxy", default=None, help="Proxy URL (http://user:pass@host:port)")

    args = parser.parse_args()

    full_text = None

    # 1. Try with Proxy if provided
    if args.proxy:
        p_str = args.proxy if args.proxy.startswith("http") else f"http://{args.proxy}"
        try:
            proxy_config = GenericProxyConfig(http_url=p_str, https_url=p_str)
            ytt_api = YouTubeTranscriptApi(proxy_config=proxy_config)
            full_text = get_transcript_for_api(ytt_api, args.video_id, args.lang)
        except Exception as e:
            sys.stderr.write(f"[WARNING] Python fallback with proxy failed ({e}). Retrying via direct connection...\n")

    # 2. Try Direct Connection if proxy failed or no proxy was provided
    if not full_text:
        try:
            ytt_api = YouTubeTranscriptApi()
            full_text = get_transcript_for_api(ytt_api, args.video_id, args.lang)
        except Exception as e:
            sys.stderr.write(f"[ERROR] Python fallback direct connection failed: {e}\n")

    if full_text:
        sys.stdout.reconfigure(encoding='utf-8')
        print(full_text)
        sys.exit(0)
    else:
        sys.stderr.write("[ERROR] Could not retrieve transcript from any connection method.\n")
        sys.exit(1)

if __name__ == "__main__":
    main()
