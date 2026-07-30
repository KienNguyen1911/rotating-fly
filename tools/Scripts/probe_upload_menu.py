"""Test: click upload button to open menu, then look for 'Upload files' item inside."""
import time
from playwright.sync_api import sync_playwright

PROFILE = r"D:\ChromeProfiles\taro.siomi7@gmail.com"
GEM_URL = "https://gemini.google.com/gem/b1af0c371214"
TEST_FILES = [r"D:\Assets\sunday-scaries\transcript.txt", r"D:\Assets\sunday-scaries\voiceover.srt"]

with sync_playwright() as p:
    ctx = p.chromium.launch_persistent_context(
        user_data_dir=PROFILE, headless=False, channel="chrome",
        args=["--no-sandbox"], viewport={"width": 1400, "height": 900})
    page = ctx.pages[0]
    page.goto(GEM_URL, wait_until="domcontentloaded", timeout=30000)
    page.wait_for_timeout(3000)

    # Click upload button
    print("=== Click 'Nội dung tải lên và công cụ' ===")
    page.locator("button[aria-label='Nội dung tải lên và công cụ']").first.click()
    page.wait_for_timeout(2000)
    page.screenshot(path=r"D:\Assets\sunday-scaries\pw_test_upload_menu.png")

    # Now dump all visible menu items
    menu_items = page.evaluate("""() => {
        // Look for menu/popup elements that appeared
        const menus = [...document.querySelectorAll('[role=menu], [role=menuitem], mat-menu, .mat-mdc-menu-panel, [class*=menu-panel]')];
        const items = [...document.querySelectorAll('[role=menuitem], button, [role=button]')];
        const visibleItems = items.filter(i => {
            const r = i.getBoundingClientRect();
            return r.width > 0 && r.height > 0 && r.y < 1100;
        }).map(i => ({
            tag: i.tagName,
            text: (i.innerText || '').trim().slice(0, 40),
            aria: i.getAttribute('aria-label'),
            role: i.getAttribute('role'),
            testid: i.getAttribute('data-test-id'),
            x: i.getBoundingClientRect().x,
            y: i.getBoundingClientRect().y
        }));
        return { visibleItems };
    }""")

    print(f"\n=== {len(menu_items.get('visibleItems', []))} visible menu-ish items ===")
    for i in menu_items.get('visibleItems', []):
        print(f"  y={i['y']:.0f} tag={i['tag']:15} text={i['text']!r:30} aria={i['aria']!r:50} role={i['role']!r}")

    ctx.close()