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

def main():
    parser = argparse.ArgumentParser(description="Fetch YouTube transcript via youtube-transcript-api fallback.")
    parser.add_argument("--video-id", required=True, help="YouTube Video ID")
    parser.add_argument("--lang", default=None, help="Optional target language code")
    parser.add_argument("--proxy", default=None, help="Proxy URL (http://user:pass@host:port)")

    args = parser.parse_args()

    proxy_config = None
    if args.proxy:
        p_str = args.proxy if args.proxy.startswith("http") else f"http://{args.proxy}"
        proxy_config = GenericProxyConfig(http_url=p_str, https_url=p_str)

    try:
        ytt_api = YouTubeTranscriptApi(proxy_config=proxy_config)
        transcript_list = ytt_api.list(args.video_id)

        transcript = None
        if args.lang:
            target_lang = args.lang.strip().lower()
            if " - " in target_lang:
                target_lang = target_lang.split(" - ")[1].strip().lower()
            elif target_lang.startswith("viet"):
                target_lang = "vi"
            elif target_lang.startswith("eng"):
                target_lang = "en"
            
            try:
                transcript = transcript_list.find_transcript([target_lang])
            except NoTranscriptFound:
                pass

        if not transcript:
            for t in transcript_list:
                transcript = t
                break

        if not transcript:
            sys.exit(0)

        fetched = transcript.fetch()
        text_segments = [snippet.text for snippet in fetched if snippet.text]
        full_text = " ".join(text_segments).strip()

        sys.stdout.reconfigure(encoding='utf-8')
        print(full_text)

    except (TranscriptsDisabled, NoTranscriptFound, CouldNotRetrieveTranscript):
        sys.exit(0)
    except Exception as e:
        sys.stderr.write(f"Python fallback error: {str(e)}\n")
        sys.exit(1)

if __name__ == "__main__":
    main()
