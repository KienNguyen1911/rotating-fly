"""
Example 03: Profile selection and rotating generation.
"""

from __future__ import annotations

import asyncio

from google_flow import FlowSDK


async def main() -> None:
    print("Initializing FlowSDK with local database...")
    # Initialize SDK without st_token, pointing to our unified SQLite database.
    async with FlowSDK(db_path="data/flow.db") as sdk:
        print("Listing active profiles in database...")
        profiles = await sdk.list_profiles()

        if not profiles:
            print("No profiles found in database.")
            print("Please add one first using the flow-api dashboard UI or token updater.")
            return

        print(f"Found {len(profiles)} profiles:")
        for p in profiles:
            logged_in_status = "Logged In" if p.get("is_logged_in") else "Not Logged In"
            print(
                f"- ID: {p['id']}, Name: {p['name']}, "
                f"Email: {p.get('email')}, Status: {logged_in_status}"
            )

        # Select the first profile
        selected_profile = profiles[0]
        print(f"\nSelecting profile '{selected_profile['name']}'...")
        try:
            # Under the hood, this will switch the SDK session to this profile.
            # If a session token needs to be refreshed, it will extract it
            # from the local browser profile.
            await sdk.select_profile(selected_profile["name"])

            print("Successfully loaded profile credentials. Checking credits...")
            credits_info = await sdk.check_credits()
            print(f"Credits for {selected_profile['name']}: {credits_info.credits}")

            print("Generating test image under selected profile...")
            path = await sdk.generate(
                prompt="Sunset over a calm ocean, realistic photography",
                output_path=f"output/sunset_{selected_profile['name']}.png",
            )
            print(f"Saved: {path}")

        except Exception as e:
            print(f"Error executing under profile: {e}")


if __name__ == "__main__":
    asyncio.run(main())
