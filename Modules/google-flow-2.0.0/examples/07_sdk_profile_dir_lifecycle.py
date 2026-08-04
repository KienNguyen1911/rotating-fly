"""
Example 07: Checking and Using Profile Directory Login Status via FlowSDK.

This script demonstrates how to:
1. Check if a browser profile directory is logged in programmatically.
2. If logged in, initialize FlowSDK under that profile directory.
3. Verify account credits and generate an image.
4. If not logged in, prompt the user on how to sign in first.
"""

from __future__ import annotations

import asyncio
import os
from pathlib import Path

from google_flow import FlowSDK


async def main() -> None:
    # Use the same default browser profile path for consistency
    profile_dir = r"C:\Users\ammar\AppData\Local\ffroliva\gflow-cli\profile_default"
    
    print("=" * 80)
    print("Google Flow - SDK Profile Directory Status Check & Usage")
    print(f"Target Profile Directory: {profile_dir}")
    print("=" * 80)

    # 1. Initialize FlowSDK (no initial token needed for status checking)
    sdk = FlowSDK()

    # 2. Check if the profile directory has a valid active session
    print("\n[Step 1] Checking if the profile directory is logged in...")
    is_logged_in = await sdk.is_profile_dir_logged_in(profile_dir)

    if is_logged_in:
        print("   [STATUS] User is logged in! [OK]")
        
        # 3. Enter the SDK context and switch to the profile directory
        print("\n[Step 2] Initializing SDK and loading the profile directory...")
        async with sdk:
            await sdk.select_profile_dir(profile_dir)
            
            # 4. Check account credits
            credits_info = await sdk.check_credits()
            print(f"   [Credits] Available credits: {credits_info.credits} (Tier: {credits_info.tier})")
            
            # 5. Generate a sample image
            prompt = "A high-tech laboratory with hologram screens and robotics, concept art"
            print(f"\n[Step 3] Generating image: '{prompt}'...")
            try:
                saved_path = await sdk.generate(
                    prompt=prompt,
                    model="gemini-3.1-flash-image-landscape",
                )
                print(f"   [SUCCESS] Image generated and saved to: {saved_path}")
            except Exception as e:
                print(f"   [ERROR] Image generation failed: {e}")
                
    else:
        print("   [STATUS] User is NOT logged in. [FAILED]")
        print("\n[ACTION REQUIRED] Please log in first!")
        print("You can log in by running:")
        print("   - The CLI interactive login command: flow-cli login")
        print("   - Or by running example 06 to prompt headed browser sign-in.")


if __name__ == "__main__":
    asyncio.run(main())
