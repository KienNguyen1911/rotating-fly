"""
Example 08: Multiple reference images in image-to-image generation.
"""

from __future__ import annotations

import asyncio
import os

from google_flow import FlowSDK


async def main() -> None:
    st_token = os.getenv("FLOW_ST_TOKEN", "your-session-token-here")

    async with FlowSDK(st_token=st_token) as sdk:
        print("Checking credits...")
        try:
            credits_info = await sdk.check_credits()
            print(f"Credits: {credits_info.credits}, Tier: {credits_info.tier}")
        except Exception as e:
            print(f"Could not fetch credits (likely invalid token): {e}")
            return

        # We will generate two reference images first: a red flower and a blue sky
        ref_path_1 = "output/flower_ref.png"
        ref_path_2 = "output/sky_ref.png"

        # Check/create reference image 1
        if not os.path.exists(ref_path_1):
            print(f"Generating first reference image '{ref_path_1}'...")
            try:
                await sdk.generate(
                    prompt="A vibrant close-up of a single red flower, digital art",
                    model="gemini-3.1-flash-image-square",
                    output_path=ref_path_1,
                )
            except Exception as e:
                print(f"Failed to generate first reference image: {e}")
                return

        # Check/create reference image 2
        if not os.path.exists(ref_path_2):
            print(f"Generating second reference image '{ref_path_2}'...")
            try:
                await sdk.generate(
                    prompt="A clear blue sky with fluffy white clouds, digital art",
                    model="gemini-3.1-flash-image-square",
                    output_path=ref_path_2,
                )
            except Exception as e:
                print(f"Failed to generate second reference image: {e}")
                return

        # Read both images into memory as bytes
        print(f"Reading reference images from:\n  - {ref_path_1}\n  - {ref_path_2}")
        with open(ref_path_1, "rb") as f1, open(ref_path_2, "rb") as f2:
            image_bytes_1 = f1.read()
            image_bytes_2 = f2.read()

        # Generate a new image blending elements of both reference images
        print("Generating a new image based on BOTH reference images...")
        try:
            blended_path = await sdk.generate(
                prompt="A red flower floating in a blue sky with fluffy white clouds, blending elements of the two reference images",
                model="gemini-3.1-flash-image-square",
                reference_image=[image_bytes_1, image_bytes_2],
                output_path="output/flower_in_sky_blended.png",
            )
            print(f"Success! Saved blended result to: {blended_path}")
        except Exception as e:
            print(f"Multiple reference image generation failed: {e}")


if __name__ == "__main__":
    # Ensure output directory exists
    os.makedirs("output", exist_ok=True)
    asyncio.run(main())
