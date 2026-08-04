"""
Example 02: In-Process Captcha solving with FlowSDK.
"""

from __future__ import annotations

import asyncio
import os

from google_flow import FlowSDK, InProcessCaptchaProvider


async def main() -> None:
    st_token = os.getenv("FLOW_ST_TOKEN", "your-session-token-here")

    # Initialize the in-process captcha solver with a custom SQLite database path.
    # This runtime spins up local headless Chromium slots to solve recaptcha automatically.
    print("Setting up in-process captcha provider...")
    captcha_provider = InProcessCaptchaProvider(db_path="data/flow.db")

    print("Initializing FlowSDK...")
    async with FlowSDK(
        st_token=st_token,
        captcha_provider=captcha_provider,
        db_path="data/flow.db",
    ) as sdk:
        prompt = "A cute fluffy kitten playing with a ball of yarn, highly detailed"
        print(f"Generating image with in-process captcha: '{prompt}'...")

        try:
            saved_path = await sdk.generate(
                prompt=prompt,
                model="gemini-3.1-flash-image-square",
                output_path="output/kitten.png",
            )
            print(f"Success! Image saved to: {saved_path}")
        except Exception as e:
            print(f"Generation failed: {e}")


if __name__ == "__main__":
    asyncio.run(main())
