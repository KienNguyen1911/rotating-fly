import os
import sys
import argparse
import glob
import math
import subprocess
import re

# Dynamic resolution of user site-packages to handle different execution contexts
try:
    parts = os.path.abspath(__file__).split(os.sep)
    if "Users" in parts:
        user_idx = parts.index("Users") + 1
        if user_idx < len(parts):
            username = parts[user_idx]
            custom_site = f"C:\\Users\\{username}\\AppData\\Roaming\\Python\\Python312\\site-packages"
            if os.path.exists(custom_site) and custom_site not in sys.path:
                sys.path.insert(0, custom_site)
            local_site = f"C:\\Users\\{username}\\AppData\\Local\\Programs\\Python\\Python312\\Lib\\site-packages"
            if os.path.exists(local_site) and local_site not in sys.path:
                sys.path.insert(0, local_site)
except Exception:
    pass

def check_dependencies():
    try:
        from PIL import Image, ImageChops
        import imageio_ffmpeg
    except ImportError:
        print("[ERROR] Missing required libraries. Please run: pip install pillow imageio-ffmpeg moviepy")
        sys.exit(1)

def get_ffmpeg_exe():
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        return "ffmpeg"

def format_srt_path_for_ffmpeg(srt_path):
    # Convert backslashes to forward slashes
    path = os.path.abspath(srt_path).replace("\\", "/")
    # Escape colon (e.g., C:/ -> C\\:/)
    if ":" in path:
        path = path.replace(":", "\\:")
    # Escape single quotes if any
    path = path.replace("'", "'\\\\''")
    return path

def natural_sort_key(s):
    return [int(text) if text.isdigit() else text.lower() for text in re.split(r'(\d+)', s)]

def get_audio_duration_moviepy(audio_path):
    try:
        from moviepy import AudioFileClip
        audio = AudioFileClip(audio_path)
        duration = audio.duration
        audio.close()
        return duration
    except Exception as e:
        print(f"[WARNING] Failed to get duration via MoviePy: {e}. Trying ffprobe...")
        # Fallback to ffprobe
        ffmpeg_exe = get_ffmpeg_exe()
        ffprobe_exe = ffmpeg_exe.replace("ffmpeg.exe", "ffprobe.exe")
        if os.path.exists(ffprobe_exe):
            cmd = [
                ffprobe_exe,
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                audio_path
            ]
            try:
                result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                return float(result.stdout.strip())
            except Exception:
                pass
        return 10.0 # final fallback

def render_video_with_ffmpeg_pipe(bg_path, audio_path, srt_path, effect_dir, resolution, output_path):
    from PIL import Image, ImageChops
    
    print("[INFO] Initializing video creation with Pillow Blend and FFmpeg Pipe...")
    
    # 1. Parse resolution
    try:
        res_w, res_h = map(int, resolution.lower().split("x"))
    except ValueError:
        print(f"[WARNING] Invalid resolution format '{resolution}'. Using 1920x1080.")
        res_w, res_h = 1920, 1080

    # 2. Get background image and size
    bg = Image.open(bg_path).convert("RGB").resize((res_w, res_h))
    print(f"[INFO] Background image resized to: {res_w}x{res_h}")

    # 3. Get audio duration
    duration = get_audio_duration_moviepy(audio_path)
    print(f"[INFO] Audio duration: {duration:.2f} seconds")

    # 4. Load effect frames if provided
    effect_frames = []
    num_frames = 0
    effect_fps = 9.33
    
    if effect_dir and os.path.exists(effect_dir):
        # Auto-detect subdirectory with image frames if root has none
        target_dir = effect_dir
        direct_files = [f for f in os.listdir(effect_dir) if f.lower().endswith(('.png', '.jpg', '.jpeg'))]
        if not direct_files:
            found = False
            for root_path, dirs, filenames in os.walk(effect_dir):
                img_files = [f for f in filenames if f.lower().endswith(('.png', '.jpg', '.jpeg'))]
                if img_files:
                    target_dir = root_path
                    found = True
                    break
            if found:
                print(f"[INFO] Auto-detected effect frames folder at: {target_dir}")
        
        print(f"[INFO] Loading effect frames from {target_dir}...")
        effect_files = sorted(
            [os.path.join(target_dir, f) for f in os.listdir(target_dir) if f.lower().endswith(('.png', '.jpg', '.jpeg'))],
            key=natural_sort_key
        )
        num_frames = len(effect_files)
        if num_frames > 0:
            print(f"[INFO] Pre-loading and resizing {num_frames} effect frames...")
            for file_path in effect_files:
                with Image.open(file_path) as img:
                    resized_img = img.convert("RGB").resize((res_w, res_h))
                    effect_frames.append(resized_img)
        else:
            print("[WARNING] No effect frames found in the specified directory.")
            
    # 5. Set up FFmpeg process
    ffmpeg_exe = get_ffmpeg_exe()
    video_fps = 24
    total_video_frames = int(math.ceil(duration * video_fps))

    cmd = [
        ffmpeg_exe,
        "-y",
        "-f", "rawvideo",
        "-vcodec", "rawvideo",
        "-pix_fmt", "rgb24",
        "-s", f"{res_w}x{res_h}",
        "-r", str(video_fps),
        "-i", "-", # Read from stdin pipe
        "-i", audio_path,
    ]

    # Add subtitle filter if srt_path is provided
    video_filters = []
    if srt_path and os.path.exists(srt_path):
        escaped_srt = format_srt_path_for_ffmpeg(srt_path)
        print(f"[INFO] Adding subtitle burning from: {srt_path}")
        video_filters.append(f"subtitles='{escaped_srt}'")

    if video_filters:
        cmd.extend(["-vf", ",".join(video_filters)])

    cmd.extend([
        "-c:v", "libx264",
        "-pix_fmt", "yuv420p",
        "-c:a", "aac",
        "-shortest",
        output_path
    ])

    print(f"[INFO] Encoding {total_video_frames} frames to {output_path}...")
    process = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

    import threading
    def log_reader(pipe):
        try:
            for line in pipe:
                decoded_line = line.decode('utf-8', errors='replace')
                if "frame=" in decoded_line or "video:" in decoded_line:
                    print(decoded_line.strip(), flush=True)
        except Exception:
            pass

    reader_thread = threading.Thread(target=log_reader, args=(process.stdout,), daemon=True)
    reader_thread.start()

    try:
        sys.stdout.reconfigure(line_buffering=True)
    except AttributeError:
        pass

    try:
        for i in range(total_video_frames):
            # Calculate the current effect frame index
            if num_frames > 0:
                t = i / float(video_fps)
                effect_idx = int(t * effect_fps) % num_frames
                
                # Blend background and effect using Pillow Multiply
                blended = ImageChops.multiply(bg, effect_frames[effect_idx])
            else:
                blended = bg
            
            # Write raw RGB bytes to FFmpeg stdin
            process.stdin.write(blended.tobytes())
            
            if i % 10 == 0 or i == total_video_frames - 1:
                print(f"[PROGRESS] Progress: {i}/{total_video_frames} frames processed...", flush=True)
    except Exception as e:
        print(f"[ERROR] Error during frame writing: {e}", flush=True)
    finally:
        process.stdin.close()

    process.wait()
    
    if process.returncode == 0:
        print("[SUCCESS] Video generated successfully using FFmpeg pipe!")
    else:
        print(f"[ERROR] FFmpeg process failed with exit code {process.returncode}")
        sys.exit(1)

def main():
    parser = argparse.ArgumentParser(description="Generate video/audio from image and voiceover.")
    parser.add_argument("--mode", choices=["video", "audio"], required=True, help="Generation mode: video or audio")
    parser.add_argument("--image", required=True, help="Path to background image")
    parser.add_argument("--audio", required=True, help="Path to voiceover audio")
    parser.add_argument("--resolution", default="1920x1080", help="Resolution for video (e.g. 1920x1080 or 3840x2160)")
    parser.add_argument("--output", required=True, help="Path to output file")
    parser.add_argument("--effect", help="Path to directory containing effect animation frames (optional)")
    parser.add_argument("--srt", help="Path to SRT subtitle file (optional)")
    
    args = parser.parse_args()
    
    check_dependencies()
    
    # Verify input files
    if not os.path.exists(args.image):
        print(f"[ERROR] Image file does not exist: {args.image}")
        sys.exit(1)
    if not os.path.exists(args.audio):
        print(f"[ERROR] Audio file does not exist: {args.audio}")
        sys.exit(1)
        
    print(f"[INFO] Mode: {args.mode}")
    print(f"[INFO] Image: {args.image}")
    print(f"[INFO] Audio: {args.audio}")
    if args.effect:
        print(f"[INFO] Effect Directory: {args.effect}")
    if args.srt:
        print(f"[INFO] SRT file: {args.srt}")
    
    # Create output directory if it doesn't exist
    out_dir = os.path.dirname(os.path.abspath(args.output))
    if out_dir and not os.path.exists(out_dir):
        os.makedirs(out_dir, exist_ok=True)
        
    if args.mode == "audio":
        print("[INFO] Exporting audio only...")
        try:
            from moviepy import AudioFileClip
            audio = AudioFileClip(args.audio)
            audio.write_audiofile(args.output, bitrate="320k")
            audio.close()
            print("[SUCCESS] Audio exported successfully!")
        except Exception as e:
            print(f"[ERROR] Failed to export audio: {str(e)}")
            sys.exit(1)
            
    elif args.mode == "video":
        # If effect directory or SRT file is provided, use the optimized FFmpeg pipe method
        if args.effect or args.srt:
            render_video_with_ffmpeg_pipe(
                bg_path=args.image,
                audio_path=args.audio,
                srt_path=args.srt,
                effect_dir=args.effect,
                resolution=args.resolution,
                output_path=args.output
            )
        else:
            # Fallback to existing MoviePy composite method
            print("[INFO] Generating video using MoviePy...")
            try:
                from moviepy import ImageClip, AudioFileClip
                # Parse resolution
                try:
                    res_w, res_h = map(int, args.resolution.lower().split("x"))
                except ValueError:
                    print(f"[WARNING] Invalid resolution format '{args.resolution}'. Using 1920x1080.")
                    res_w, res_h = 1920, 1080
                    
                audio = AudioFileClip(args.audio)
                duration = audio.duration
                print(f"[INFO] Video duration will be {duration:.2f} seconds.")
                
                # Setup background image
                bg = ImageClip(args.image).resized((res_w, res_h)).with_duration(duration)
                
                final_video = bg.with_audio(audio)
                
                print(f"[INFO] Rendering video to {args.output}...")
                final_video.write_videofile(
                    args.output,
                    fps=24,
                    codec="libx264",
                    audio_codec="aac",
                    threads=4,
                    logger="bar"
                )
                
                # Cleanup
                audio.close()
                bg.close()
                final_video.close()
                print("[SUCCESS] Video generated successfully!")
            except Exception as e:
                print(f"[ERROR] Failed to generate video: {str(e)}")
                sys.exit(1)

if __name__ == "__main__":
    main()
