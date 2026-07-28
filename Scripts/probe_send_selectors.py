r"""Probe send button with multiple selector strategies."""
import time
from playwright.sync_api import sync_playwright

PROFILE = r"D:\ChromeProfiles\taro.siomi7@gmail.com"
GEM_URL = "https://gemini.google.com/gem/b1af0c371214"

# Use raw Vietnamese strings (avoid \u escape problems)
GUI = "Gửi tin nhắn"
TIN_NHAN = "Gửi tin nhắn"

selectors_to_try = [
    f"button[aria-label='{GUI}']",
    f'button[aria-label="{GUI}"]',
    f"button[aria-label*='{GUI[:4]}']",
    f"button[aria-label*='{GUI[-4:]}']",
    f"button[aria-label*='nhắn']",
    f"button:has-text('{GUI}')",
    # Try by class hierarchy
    "button.input-area-send-button",
    "button[class*='send-button']",
    "button[class*='send']",
    # By position: last visible button in input footer
    "footer button:last-child",
    "[data-test-id='input-action-bar'] button",
    ".input-action-bar button",
]

with sync_playwright() as p:
    ctx = p.chromium.launch_persistent_context(
        user_data_dir=PROFILE, headless=False, channel="chrome",
        args=["--no-sandbox"], viewport={"width": 1400, "height": 900})
    page = ctx.pages[0]
    page.goto(GEM_URL, wait_until="domcontentloaded", timeout=30000)
    page.wait_for_timeout(3000)

    for sel in selectors_to_try:
        try:
            print(f"\n[TRY] {sel!r}")
            count = page.locator(sel).count()
            print(f"  count={count}")
            if count > 0:
                page.locator(sel).first.click(timeout=2000)
                print(f"  CLICKED OK")
                break
        except Exception as e:
            err = str(e).split('\n')[0][:200]
            print(f"  ERR {type(e).__name__}: {err}")

    ctx.close()