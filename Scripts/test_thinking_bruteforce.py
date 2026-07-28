"""
Brute-force test: find which model_id/header combination actually enables thinking mode.
Tests multiple model_ids and capacity values to see which one returns "thoughts".
"""
import asyncio
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "Modules" / "Gemini-API-2.0.0"
sys.path.insert(0, str(ROOT / "src"))

from gemini_webapi import GeminiClient
from gemini_webapi.constants import Model, build_model_header, AccountStatus, MODEL_HEADER_KEY
from gemini_webapi.utils.parsing import get_nested_value


def load_cookies():
    cookie_file = ROOT / "cookies.json"
    data = json.loads(cookie_file.read_text(encoding="utf-8"))
    cookies = data.get("cookies", data)
    return cookies["__Secure-1PSID"], cookies.get("__Secure-1PSIDTS"), cookies


async def test_model(client, model_desc, model_obj, prompt="Say hello in 5 words."):
    """Test a specific model and check if it returns thoughts."""
    try:
        chat = client.start_chat(model=model_obj)
        output = await chat.send_message(prompt, temporary=True)
        text = (output.text or "")[:100]
        thoughts = getattr(output, "thoughts", None) or ""
        has_thoughts = len(thoughts) > 10
        print(f"  {model_desc:<45} text={len(output.text or '')} chars  thoughts={'🧠 YES' if has_thoughts else '❌ no'}  [{text[:60]}...]")
        return has_thoughts
    except Exception as e:
        err = str(e)[:80]
        print(f"  {model_desc:<45} ERROR: {err}")
        return False


async def main():
    psid, psidts, all_cookies = load_cookies()

    print("=" * 70)
    print("🔬 Thinking Model Brute-Force Test")
    print("=" * 70)

    client = GeminiClient(psid, psidts, verify=False)
    client.cookies = all_cookies
    await client.init(auto_refresh=True)
    print(f"✅ Account: {client.account_status.name}\n")

    # List dynamic models for reference
    dyn_models = client.list_models()
    print("Dynamic registry model_ids:")
    for m in (dyn_models or []):
        print(f"  {m.model_id} → {m.display_name} (cap={m.capacity})")
    print()

    # ── Test 1: Dynamic models from registry ──
    print("── Test 1: Dynamic registry models ──")
    for m in (dyn_models or []):
        await test_model(client, f"dynamic: {m.display_name}", m)

    # ── Test 2: Hardcoded Model enum variants ──
    print("\n── Test 2: Hardcoded Model enum variants ──")
    
    test_cases = [
        ("BASIC_FLASH", Model.BASIC_FLASH),
        ("BASIC_PRO", Model.BASIC_PRO),
        ("BASIC_THINKING", Model.BASIC_THINKING),
        ("ADVANCED_FLASH", Model.ADVANCED_FLASH),
        ("ADVANCED_PRO", Model.ADVANCED_PRO),
        ("ADVANCED_THINKING", Model.ADVANCED_THINKING),
    ]
    
    for name, model in test_cases:
        await test_model(client, f"enum: {name} ({model.model_id})", model)

    # ── Test 3: Custom headers — try thinking model_id with different capacities ──
    print("\n── Test 3: Custom headers with different model_ids ──")
    
    # Known model_ids from dynamic registry
    custom_tests = [
        # (desc, model_id, capacity)
        ("Flash model_id + cap=1 (BASIC)", "56fdd199312815e2", 1),
        ("Flash model_id + cap=2 (ADV)", "56fdd199312815e2", 2),
        ("Pro model_id + cap=1 (BASIC)", "e6fa609c3fa255c0", 1),
        ("Pro model_id + cap=2 (ADV)", "e6fa609c3fa255c0", 2),
        # Thinking model_ids from constants.py
        ("THINKING BASIC model_id + cap=1", "5bf011840784117a", 1),
        ("THINKING BASIC model_id + cap=2", "5bf011840784117a", 2),
        ("THINKING ADV model_id + cap=1", "e051ce1aa80aa576", 1),
        ("THINKING ADV model_id + cap=2", "e051ce1aa80aa576", 2),
        ("THINKING ADV model_id + cap=4", "e051ce1aa80aa576", 4),
    ]
    
    for desc, mid, cap in custom_tests:
        custom_model = Model.from_dict({
            "model_name": f"test-{mid[:8]}",
            "model_header": build_model_header(mid, cap),
        })
        await test_model(client, f"custom: {desc}", custom_model)
    
    # ── Test 4: Use Flash model_id but with thinking-like name ──
    print("\n── Test 4: Same model_id, different model_name ──")
    for mid, cap in [("56fdd199312815e2", 2), ("e6fa609c3fa255c0", 2)]:
        for name_suffix in ["-flash", "-pro", "-thinking", "-flash-thinking"]:
            custom_model = Model.from_dict({
                "model_name": f"gemini-3{name_suffix}",
                "model_header": build_model_header(mid, cap),
            })
            await test_model(client, f"custom: {mid[:8]} as {name_suffix}", custom_model)

    await client.close()
    print("\nDone.")


if __name__ == "__main__":
    asyncio.run(main())
