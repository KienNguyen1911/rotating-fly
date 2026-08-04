"""
Example 06: Direct Profile Directory Usage and Lifecycle Management.

This script demonstrates how to:
1. Check if a previously extracted token exists and is valid.
2. If yes, proceed directly to image generation.
3. If no, attempt to silently extract a new token from the browser profile directory (headless).
4. If silent extraction fails because the user is not logged in:
   - Launch the browser in non-headless mode for the user to log in.
   - Wait for the user to complete login in the browser.
   - Extract the new token and verify it works before proceeding.
"""

from __future__ import annotations

import asyncio
import contextlib
import os
import sys
from pathlib import Path

from google_flow import FlowSDK, PlaywrightCaptchaProvider
from google_flow.token_updater.browser import BrowserManager


async def verify_token(token: str) -> bool:
    """Verify if the token is valid by checking credits via FlowSDK."""
    captcha_provider = PlaywrightCaptchaProvider(st_token=token, headless=False)
    try:
        async with FlowSDK(st_token=token, captcha_provider=captcha_provider) as sdk:
            credits_info = await sdk.check_credits()
            print(f"   [SUCCESS] Token verified. Credits: {credits_info.credits}")
            return True
    except Exception as e:
        print(f"   [VERIFICATION FAILED] Token is invalid or expired: {e}")
        return False


async def main() -> None:
    # Path to the persistent browser profile directory
    profile_dir = os.path.abspath("data/browser_profiles/my_custom_profile")
    profile_dir = r"C:\Users\ammar\AppData\Local\ffroliva\gflow-cli\profile_default"
    token_cache_file = Path("data/cached_token.txt")
    db_path = "data/flow.db"

    # Ensure directories exist
    os.makedirs("data", exist_ok=True)
    os.makedirs(os.path.dirname(profile_dir), exist_ok=True)

    print("=" * 80)
    print("Google Flow - Direct Profile Directory Example")
    print(f"Profile Dir: {profile_dir}")
    print("=" * 80)

    token = None

    # Step 1: Check if we have a cached token from a previous run and if it works
    if token_cache_file.exists():
        cached_token = token_cache_file.read_text(encoding="utf-8").strip()
        print("1. Found cached session token. Verifying validity...")
        if await verify_token(cached_token):
            token = cached_token
        else:
            print("   Cached token is invalid. Clearing cache...")
            token_cache_file.unlink(missing_ok=True)

    # Step 2: Try to silently extract/refresh token from profile_dir (silently/headless)
    if not token:
        print("\n2. Attempting silent token extraction from profile directory...")
        browser = BrowserManager()
        await browser.start()
        try:
            # First peek if a session cookie exists in the profile
            peeked = await browser.peek_token(profile_dir=profile_dir)
            if peeked:
                print("   Found session cookie in profile. Refreshing session...")
                extracted = await browser.extract_token(profile_dir=profile_dir)
                if extracted:
                    print("   Token extracted. Verifying...")
                    if await verify_token(extracted):
                        token = extracted
                        token_cache_file.write_text(token, encoding="utf-8")
                        print("   [SUCCESS] Silent extraction and verification succeeded!")
            else:
                print("   No session cookie found in the profile.")
        except Exception as e:
            print(f"   Error during silent extraction: {e}")
        finally:
            await browser.stop()

    # Step 3: Interactive Login if still no valid token
    if not token:
        print("\n3. [ACTION REQUIRED] User is not logged in!")
        print("   Opening browser for interactive login. Please log in to Google Flow...")

        browser = BrowserManager()
        await browser.start()
        interactive_context = None
        try:
            # Launch in non-headless mode so the user can interact and log in
            interactive_context = await browser._playwright.chromium.launch_persistent_context(
                user_data_dir=profile_dir,
                channel="chrome",
                headless=False,  # Visual mode
                viewport={"width": 1024, "height": 768},
                locale="en-US",
                args=["--no-sandbox", "--disable-setuid-sandbox"],
                ignore_default_args=["--enable-automation"],
            )
            
            page = await interactive_context.new_page()
            await page.goto("https://labs.google/fx/tools/flow")
            
            print("\n   " + "!" * 50)
            print("   Please sign in to Google in the opened browser window.")
            print("   Once you successfully log in and reach the Google Flow dashboard,")
            print("   return to this terminal and press ENTER.")
            print("   " + "!" * 50 + "\n")
            
            # Wait for user input in the terminal
            await asyncio.to_thread(input, "   Press ENTER after you have logged in: ")
            
            # Close the interactive browser context before extracting token
            await interactive_context.close()
            interactive_context = None
            
            # Now run headless extraction to obtain the token
            print("   Extracting token from the new logged-in session...")
            extracted = await browser.extract_token(profile_dir=profile_dir)
            if extracted:
                print("   Token extracted. Verifying...")
                if await verify_token(extracted):
                    token = extracted
                    token_cache_file.write_text(token, encoding="utf-8")
                    print("   [SUCCESS] Login verified and token saved!")
                else:
                    print("   [FAILED] Token verification failed after login.")
            else:
                print("   [FAILED] Could not extract token after login.")
                
        except Exception as e:
            print(f"   Error during interactive login: {e}")
        finally:
            if interactive_context:
                with contextlib.suppress(Exception):
                    await interactive_context.close()
            await browser.stop()

    # Step 4: Proceed with Image Generation if we have a valid token
    if not token:
        print("\n❌ Could not obtain a valid session token. Exiting.")
        return

    print("\n4. Initializing FlowSDK with verified session token...")
    captcha_provider = PlaywrightCaptchaProvider(st_token=token, headless=False)
    async with FlowSDK(st_token=token, captcha_provider=captcha_provider) as sdk:
        prompts = [
            "A majestic dragon sitting on top of a mountain of glowing crystals, digital art",
            "A serene lake surrounded by cherry blossom trees at sunset, oil painting",
            "A futuristic cyberpunk cityscape with neon lights and flying cars, cinematic rendering"
        ]
        
        print("5. Generating multiple images concurrently in the same session...")
        
        async def generate_single(index: int, prompt_text: str) -> None:
            print(f"   [Started] Generating image {index}: '{prompt_text}'...")
            try:
                saved_path = await sdk.generate(
                    prompt=prompt_text,
                    model="gemini-3.1-flash-image-landscape",
                    # output_path=f"output/profile_dir_result_{index}.png",
                )
                print(f"   [Success] Image {index} saved to: {saved_path}")
            except Exception as e:
                print(f"   [Failed] Image {index} generation failed: {e}")

        tasks = [generate_single(i, prompt) for i, prompt in enumerate(prompts, start=1)]
        await asyncio.gather(*tasks)


if __name__ == "__main__":
    asyncio.run(main())
