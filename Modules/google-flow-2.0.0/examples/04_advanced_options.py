"""
Example 04: Advanced options (Image-to-Image and Upscaling).
"""

from __future__ import annotations

import asyncio
import os

from google_flow import FlowSDK


async def main() -> None:
    st_token = os.getenv("FLOW_ST_TOKEN", "your-session-token-here")

    async with FlowSDK(st_token=st_token) as sdk:
        # 1. Image-to-Image (using a reference image)
        ref_path = "output/castle.png"
        if not os.path.exists(ref_path):
            print(
                f"Reference image {ref_path} not found. "
                "Running simple generation first to create it..."
            )
            try:
                ref_path = await sdk.generate(
                    prompt="A majestic flying castle surrounded by clouds, digital art",
                    model="gemini-3.1-flash-image-landscape",
                    output_path="output/castle.png",
                )
            except Exception as e:
                print(f"Failed to generate base reference: {e}")
                return

        print(f"Reading reference image from {ref_path}...")
        with open(ref_path, "rb") as f:
            image_bytes = f.read()

        print("Generating new image based on reference image...")
        try:
            img2img_path = await sdk.generate(
                prompt="A futuristic cyberpunk version of the castle, neon lights",
                model="gemini-3.1-flash-image-landscape",
                reference_image=image_bytes,
                output_path="output/castle_cyberpunk.png",
            )
            print(f"Saved image-to-image result: {img2img_path}")
        except Exception as e:
            print(f"Image-to-image failed: {e}")

        # 2. Image generation with 2k/4k upscaling
        # Note: Upscaling requires paid tier credits on Google Flow.
        print("\nGenerating image with high-resolution 2K upscaling...")
        try:
            upscaled_path = await sdk.generate(
                prompt="A highly detailed close-up of a dragon eye, fantasy art",
                model="gemini-3.1-flash-image-square",
                output_path="output/dragon_eye.png",
                upscale="2k",  # Can be "none", "2k", or "4k"
            )
            print(f"Saved upscaled image: {upscaled_path}")
        except Exception as e:
            print(f"Upscaling failed (might require paid tier / credits): {e}")


if __name__ == "__main__":
    asyncio.run(main())
