"""Find upload/attach button in Gemini input area."""
import time
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

    # Inspect element around input — find all siblings/parents of textarea-inner
    info = page.evaluate("""() => {
        const ta = document.querySelector("[data-test-id='textarea-inner']");
        if (!ta) return { err: 'textarea-inner not found' };
        // climb up to find container with input toolbar
        const rich = document.querySelector('rich-textarea');
        const candidates = [];
        let cur = ta;
        for (let i = 0; i < 10 && cur; i++) {
            // find input-action-bar children
            const btns = [...cur.querySelectorAll('button')];
            for (const b of btns) {
                const r = b.getBoundingClientRect();
                candidates.push({
                    depth: i,
                    aria: b.getAttribute('aria-label'),
                    text: (b.innerText || '').trim().slice(0, 30),
                    tag: b.tagName,
                    classes: b.className.toString().slice(0, 80),
                    cx: r.x, cy: r.y, w: r.width, h: r.height,
                    visible: r.width > 0 && r.height > 0
                });
            }
            cur = cur.parentElement;
        }
        return { candidates: candidates.slice(0, 80) };
    }""")

    print("=== Buttons near textarea-inner ===")
    for c in info.get('candidates', []):
        if c['visible']:
            print(f"  d={c['depth']} pos=({c['cx']:.0f},{c['cy']:.0f} {c['w']:.0f}x{c['h']:.0f}) aria={c['aria']!r:40} text={c['text']!r:20} tag={c['tag']}")

    # Hover/click around the input area to see if any +/- button appears
    print("\n=== Trying to find upload via class containing 'file' or 'attach' ===")
    more = page.evaluate("""() => {
        const all = [...document.querySelectorAll('input, button, [role=button], [contenteditable]')];
        return all.filter(el => {
            const cls = (el.className && el.className.toString) ? el.className.toString().toLowerCase() : '';
            return cls.includes('upload') || cls.includes('attach') || cls.includes('file-input');
        }).map(el => ({
            tag: el.tagName, type: el.type, classes: el.className.toString().slice(0, 100),
            aria: el.getAttribute('aria-label'),
            visible: !!(el.offsetParent !== null || el.getClientRects().length)
        }));
    }""")
    for m in more:
        print(f"  {m}")

    # Now try to find the "+" button (Gemini might have a Tools menu)
    print("\n=== Find buttons in input-footer area ===")
    footer_btns = page.evaluate("""() => {
        // find input-area or rich-textarea's nearest container with toolbar
        const rich = document.querySelector('rich-textarea');
        if (!rich) return [];
        const parent = rich.closest('.input-area-container, .input-area, [class*="input-area"], [class*="input"]');
        if (!parent) return [];
        const btns = [...parent.querySelectorAll('button')];
        return btns.map(b => {
            const r = b.getBoundingClientRect();
            return {
                aria: b.getAttribute('aria-label'),
                text: (b.innerText || '').trim().slice(0, 30),
                visible: r.width > 0 && r.height > 0,
                x: r.x, y: r.y
            };
        }).filter(b => b.visible);
    }""")
    for b in footer_btns:
        print(f"  aria={b['aria']!r:40} text={b['text']!r:20} pos=({b['x']:.0f},{b['y']:.0f})")

    ctx.close()