"""
Test script: Verify Gemini Thinking Mode is actually working.

Usage:
    cd Modules/Gemini-API-2.0.0
    python ../../Scripts/test_thinking_mode.py

This script:
1. Lists all available models from Gemini dynamic registry
2. Sends the same prompt with STANDARD vs THINKING model
3. Compares outputs to verify thinking mode produces different results
"""
import asyncio
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "Modules" / "Gemini-API-2.0.0"
sys.path.insert(0, str(ROOT / "src"))

from gemini_webapi import GeminiClient
from gemini_webapi.constants import Model, AccountStatus


def load_cookies():
    cookie_file = ROOT / "cookies.json"
    if not cookie_file.exists():
        print(f"❌ cookies.json not found at {cookie_file}")
        sys.exit(1)

    data = json.loads(cookie_file.read_text(encoding="utf-8"))
    cookies = data.get("cookies", data)
    psid = cookies.get("__Secure-1PSID")
    psidts = cookies.get("__Secure-1PSIDTS")

    if not psid:
        print("❌ __Secure-1PSID not found in cookies.json")
        sys.exit(1)

    return psid, psidts, cookies


async def main():
    psid, psidts, all_cookies = load_cookies()

    print("=" * 60)
    print("🔍 Gemini Thinking Mode Verification")
    print("=" * 60)

    # Init client
    print("\n[1] Initializing Gemini client...")
    client = GeminiClient(psid, psidts, verify=False)
    client.cookies = all_cookies
    await client.init(auto_refresh=True)

    if client.account_status != AccountStatus.AVAILABLE:
        print(f"⚠️ Account status: {client.account_status.name}")
    else:
        print(f"✅ Account status: {client.account_status.name}")

    # ── List available models from Gemini ──
    print("\n[2] Fetching available models from Gemini dynamic registry...")
    models = client.list_models()
    if not models:
        print("❌ No models returned from Gemini.")
        await client.close()
        return

    print(f"\n📋 {len(models)} models available:\n")
    print(f"{'MODEL NAME':<38} {'DISPLAY':<14} {'CAP':<5} {'AVAIL':<6} {'THINKING':<10}")
    print("-" * 75)
    for m in sorted(models, key=lambda x: (not ("thinking" in x.model_name.lower()), x.model_name)):
        name = m.model_name or "(no name)"
        disp = m.display_name or "-"
        cap = m.capacity
        avail = "✅" if m.is_available else "❌"
        thinking = "🧠 YES" if "thinking" in name.lower() else "-"
        print(f"{name:<38} {disp:<14} {cap:<5} {avail:<6} {thinking:<10}")

    # Find thinking model
    thinking_models = [m for m in models if m.is_available and "thinking" in m.model_name.lower()]
    standard_models = [m for m in models if m.is_available and "thinking" not in m.model_name.lower()]

    if not thinking_models:
        print("\n⚠️ No thinking models available on your account!")
        print("   Thinking mode requires Gemini Advanced subscription.")
    else:
        print(f"\n🧠 Thinking models available: {[m.model_name for m in thinking_models]}")

    # ── Test: send same prompt with both models ──
    test_prompt = "Explain quantum entanglement in 2 sentences."

    if thinking_models and standard_models:
        thinking_model = thinking_models[0]
        standard_model = standard_models[0]

        print(f"\n[3] Testing: same prompt with 2 models")
        print(f"    Standard: {standard_model.model_name}")
        print(f"    Thinking: {thinking_model.model_name}")

        # Test standard
        print("\n--- STANDARD model response ---")
        chat_std = client.start_chat(model=standard_model.model_name)
        output_std = await chat_std.send_message(test_prompt)
        std_text = output_std.text or "(empty)"
        std_thoughts = getattr(output_std, "thoughts", None) or ""
        print(f"Response ({len(std_text)} chars):")
        print(std_text[:300])
        if std_thoughts:
            print(f"\nThoughts: {std_thoughts[:200]}...")

        # Test thinking
        print("\n--- THINKING model response ---")
        chat_think = client.start_chat(model=thinking_model.model_name)
        output_think = await chat_think.send_message(test_prompt)
        think_text = output_think.text or "(empty)"
        think_thoughts = getattr(output_think, "thoughts", None) or ""
        print(f"Response ({len(think_text)} chars):")
        print(think_text[:300])
        if think_thoughts:
            print(f"\nThoughts: {think_thoughts[:200]}...")
        else:
            print("\n⚠️ No 'thoughts' field in thinking response!")
            print("   This may indicate thinking mode is NOT actually engaged.")
            print("   The model name is set, but Google may be silently falling back.")

        # Summary
        print("\n" + "=" * 60)
        print("📊 VERDICT:")
        print(f"   Standard response: {len(std_text)} chars")
        print(f"   Thinking response: {len(think_text)} chars")
        print(f"   Thinking 'thoughts' present: {'✅ YES' if think_thoughts else '❌ NO'}")
        
        if think_thoughts:
            print("\n✅ Thinking mode IS working! You can see the 'thoughts' field.")
        else:
            print("\n⚠️ Thinking mode may NOT be active. Check:")
            print("   1. Do you have Gemini Advanced subscription?")
            print("   2. Is the thinking model available for your account?")
            print("   3. Some accounts only support thinking via the Web UI, not API.")

    await client.close()
    print("\nDone.")


if __name__ == "__main__":
    asyncio.run(main())
