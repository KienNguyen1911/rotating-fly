"""
Example 05: Complete Cycle - Profile Creation, Automated Google Login,
and Image Generation with In-Process Captcha Solving.
"""

from __future__ import annotations

import asyncio
import os
import sys

from google_flow import FlowSDK, InProcessCaptchaProvider
from google_flow.token_updater.browser import BrowserManager
from google_flow.token_updater.database import ProfileDB


async def main() -> None:
    # 1. Retrieve Google Flow credentials from environment variables
    account = os.getenv("GOOGLE_ACCOUNT")
    password = os.getenv("GOOGLE_PASSWORD")
    proxy = os.getenv("PROXY_URL", "")  # Optional proxy if needed

    if not account or not password:
        print("=" * 80)
        print("ERROR: Please set GOOGLE_ACCOUNT and GOOGLE_PASSWORD environment variables.")
        print("Example on Windows Powershell:")
        print('  $env:GOOGLE_ACCOUNT="your.email@gmail.com"')
        print('  $env:GOOGLE_PASSWORD="your-password-here"')
        print("=" * 80)
        sys.exit(1)

    db_path = "data/flow.db"
    profile_name = "GoogleFlowUser"

    # 2. Setup the unified SQLite database and add the profile
    print(f"1. Initializing profile database at '{db_path}'...")
    db = ProfileDB()
    # Explicitly set the db path to our unified path
    from google_flow.token_updater.config import config as updater_config

    updater_config.db_path = db_path
    await db.init()

    # Clean up old profile if it exists to allow re-runs
    old_profile = await db.get_profile_by_name(profile_name)
    if old_profile:
        print(f"   Removing existing profile '{profile_name}' for clean setup...")
        await db.delete_profile(old_profile["id"])

    print(f"2. Adding profile '{profile_name}' with account credentials...")
    profile_id = await db.add_profile(
        name=profile_name,
        remark="Programmatic automated test profile",
        login_account=account,
        login_password=password,
        proxy_url=proxy,
    )

    # 3. Perform automated Google Login using BrowserManager
    print("3. Launching Chromium via BrowserManager for automated Google Flow login...")
    print(
        "   (This runs in headless mode unless ENABLE_VNC=1 is set. "
        "It will advance prompts automatically)"
    )
    browser = BrowserManager()
    await browser.start()
    try:
        login_result = await browser.auto_login(profile_id)
        if not login_result.get("success"):
            print(f"   Login failed: {login_result.get('error')}")
            return
        print(
            "   Login successful! Session token extracted and persisted "
            f"for profile '{profile_name}'."
        )
    finally:
        await browser.stop()

    # 4. Use FlowSDK with the newly logged-in profile
    print("\n4. Initializing FlowSDK and selecting the new profile...")
    captcha_provider = InProcessCaptchaProvider(db_path=db_path)
    async with FlowSDK(captcha_provider=captcha_provider, db_path=db_path) as sdk:
        # Switch the SDK context to use this profile
        await sdk.select_profile(profile_name)

        # Check credits to verify authentication
        print("5. Checking account credits...")
        try:
            credits_info = await sdk.check_credits()
            print(f"   Credits: {credits_info.credits}, Tier: {credits_info.tier}")
        except Exception as e:
            print(f"   Could not fetch credits: {e}")
            return

        # Generate image (uses in-process captcha solver)
        prompt = (
            "A futuristic laboratory designing artificial intelligence, "
            "digital painting, glowing neon accents"
        )
        print(f"6. Generating image (solving captcha automatically in-process): '{prompt}'...")
        try:
            saved_path = await sdk.generate(
                prompt=prompt,
                model="gemini-3.1-flash-image-landscape",
                output_path="output/complete_cycle_result.png",
            )
            print(f"   Success! Output saved to: {saved_path}")
        except Exception as e:
            print(f"   Generation failed: {e}")


if __name__ == "__main__":
    asyncio.run(main())
