"""Quick DOM probe — find the send button."""
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

PROFILE = r"D:\ChromeProfiles\taro.siomi7@gmail.com"
GEM_URL = "https://gemini.google.com/gem/b1af0c371214"

with sync_playwright() as p:
    ctx = p.chromium.launch_persistent_context(
        user_data_dir=PROFILE, headless=False, channel="chrome",
        args=["--no-sandbox"], viewport={"width": 1400, "height": 900})
    page = ctx.pages[0]
    page.goto(GEM_URL, wait_until="domcontentloaded", timeout=30000)
    page.wait_for_timeout(3000)

    # Find ALL buttons in the input area
    info = page.evaluate("""() => {
        // Look for any element with "send" in text or class
        const allButtons = [...document.querySelectorAll('button')];
        const result = allButtons.map((b, i) => ({
            i, text: (b.innerText || '').trim().slice(0, 50),
            aria: b.getAttribute('aria-label'),
            classes: (b.className || '').toString().slice(0, 100),
            dataTestId: b.getAttribute('data-test-id'),
            visible: !!(b.offsetParent !== null || b.getClientRects().length),
            html: b.outerHTML.slice(0, 250)
        }));
        // Also try elements with send-icon
        const sendIcons = [...document.querySelectorAll('[data-test-id="send-icon"]')];
        const sendIconsInfo = sendIcons.map(s => ({
            tag: s.tagName,
            parent: s.parentElement?.outerHTML.slice(0, 300),
            parentAria: s.parentElement?.getAttribute('aria-label'),
            grandAria: s.parentElement?.parentElement?.getAttribute('aria-label')
        }));
        return { buttons: result, sendIcons: sendIconsInfo };
    }""")

    print(f"\n=== {len(info['buttons'])} buttons total ===")
    for b in info['buttons']:
        if b['visible']:
            print(f"[{b['i']}] text={b['text']!r:30} aria={b['aria']!r}")
            print(f"    classes={b['classes']!r}")
            print(f"    testid={b['dataTestId']!r}")
            print(f"    html={b['html'][:150]}")
            print()

    print(f"\n=== {len(info['sendIcons'])} elements with send-icon ===")
    for s in info['sendIcons']:
        print(f"  tag={s['tag']}")
        print(f"  parentAria={s['parentAria']!r}")
        print(f"  grandAria={s['grandAria']!r}")
        print(f"  parentHTML={s['parent'][:200]}")
        print()

    # Take screenshot of input area
    page.screenshot(path=r"D:\Assets\sunday-scaries\pw_test_sendbutton.png")
    print("Screenshot: D:\\Assets\\sunday-scaries\\pw_test_sendbutton.png")
    ctx.close()