"""
Example 01: Simple image generation using FlowSDK.
"""

from __future__ import annotations

import asyncio
import os

from google_flow import FlowSDK


async def main() -> None:
    # Retrieve Session Token (ST) from environment variable or replace with your token
    st_token = os.getenv("FLOW_ST_TOKEN", "your-session-token-here")

    print("Initializing FlowSDK...")
    # Initialize the SDK with the Session Token.
    # By default, it will attempt to use the in-process captcha provider if playwright is installed,
    # or fall back to standard settings.
    async with FlowSDK(st_token=st_token) as sdk:
        print("Checking credits...")
        try:
            credits_info = await sdk.check_credits()
            print(f"Credits: {credits_info.credits}, Tier: {credits_info.tier}")
        except Exception as e:
            print(f"Could not fetch credits (likely invalid token): {e}")
            return

        prompt = "A majestic flying castle surrounded by clouds, digital art"
        print(f"Generating image for prompt: '{prompt}'...")

        # Generate the image. It handles:
        # 1. Exchanging Session Token (ST) for Access Token (AT)
        # 2. Expiration retries and refreshing
        # 3. Generating the image
        # 4. Downloading the result automatically
        try:
            saved_path = await sdk.generate(
                prompt=prompt,
                model="gemini-3.1-flash-image-landscape",  # Default model or any listed in registry
                output_path="output/castle.png",
            )
            print(f"Success! Image saved to: {saved_path}")
        except Exception as e:
            print(f"Generation failed: {e}")


if __name__ == "__main__":
    asyncio.run(main())
