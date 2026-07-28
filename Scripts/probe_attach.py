"""Probe file input area to find a working attach strategy."""
import time, base64, os
from pathlib import Path
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

    # 1. List all <input> elements
    inputs = page.evaluate("""() => {
        const ins = [...document.querySelectorAll('input')];
        return ins.map((i, idx) => ({
            idx, type: i.type, accept: i.accept,
            visible: !!(i.offsetParent !== null || i.getClientRects().length),
            display: getComputedStyle(i).display,
            multiple: i.multiple,
            classes: i.className
        }));
    }""")
    print(f"=== {len(inputs)} <input> elements ===")
    for i in inputs:
        print(f"  [{i['idx']}] type={i['type']} visible={i['visible']} display={i['display']} accept={i['accept']!r}")

    # 2. List all elements with 'upload' or 'attach' in attributes
    attach_btns = page.evaluate("""() => {
        const buttons = [...document.querySelectorAll('button, [role=button]')];
        const attachy = buttons.filter(b => {
            const t = (b.innerText || '').toLowerCase();
            const a = (b.getAttribute('aria-label') || '').toLowerCase();
            return t.includes('attach') || t.includes('upload') || t.includes('tệp') ||
                   a.includes('attach') || a.includes('upload') || a.includes('tệp') ||
                   a.includes('thêm');
        });
        return attachy.map(b => ({
            text: b.innerText || '',
            aria: b.getAttribute('aria-label'),
            testid: b.getAttribute('data-test-id'),
            classes: b.className.toString().slice(0, 80),
            visible: !!(b.offsetParent !== null || b.getClientRects().length)
        }));
    }""")
    print(f"\n=== {len(attach_btns)} attach-like buttons ===")
    for b in attach_btns:
        print(f"  text={b['text']!r:30} aria={b['aria']!r:50} testid={b['testid']!r}")

    # 3. Find ALL elements with 'upload' or 'attach' in class/data-test-id (anywhere in DOM)
    elements_with_upload = page.evaluate("""() => {
        const all = [...document.querySelectorAll('*')];
        const matches = [];
        for (const el of all) {
            const cls = (el.className && el.className.toString) ? el.className.toString().toLowerCase() : '';
            const tid = (el.getAttribute('data-test-id') || '').toLowerCase();
            const aria = (el.getAttribute('aria-label') || '').toLowerCase();
            if (cls.includes('upload') || cls.includes('attach') || cls.includes('file') ||
                tid.includes('upload') || tid.includes('attach') || tid.includes('file') ||
                aria.includes('upload') || aria.includes('attach') || aria.includes('file')) {
                matches.push({
                    tag: el.tagName, testid: el.getAttribute('data-test-id'),
                    classes: el.className.toString().slice(0, 80),
                    aria: el.getAttribute('aria-label'),
                    visible: !!(el.offsetParent !== null || el.getClientRects().length)
                });
            }
        }
        return matches.slice(0, 20);
    }""")
    print(f"\n=== {len(elements_with_upload)} elements with upload/attach/file in attrs ===")
    for e in elements_with_upload:
        print(f"  tag={e['tag']:15} testid={e['testid']!r:40} aria={e['aria']!r}")

    # 4. Try JS DataTransfer drop event on the textarea-inner
    print("\n=== Trying JS DataTransfer drop event ===")
    file_payload = []
    for fp in TEST_FILES:
        with open(fp, 'rb') as f:
            data = f.read()
        file_payload.append({
            "name": os.path.basename(fp),
            "type": "text/plain" if fp.endswith('.txt') else "application/x-subrip",
            "b64": base64.b64encode(data).decode('ascii')
        })
    import json
    payload_json = json.dumps(file_payload)

    result = page.evaluate("""(args) => {
        const [filesJson, selector] = args;
        const files = JSON.parse(filesJson);
        const target = document.querySelector(selector);
        if (!target) return { ok: false, reason: 'target not found: ' + selector };
        try {
            const dt = new DataTransfer();
            for (const f of files) {
                const bin = atob(f.b64);
                const arr = new Uint8Array(bin.length);
                for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
                const file = new File([arr], f.name, { type: f.type });
                dt.items.add(file);
            }
            target.dispatchEvent(new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: dt }));
            target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt }));
            target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt }));
            return { ok: true, fileCount: files.length, target: selector };
        } catch (e) {
            return { ok: false, reason: e.message };
        }
    }""", [payload_json, "[data-test-id='textarea-inner']"])
    print(f"  Result: {result}")
    page.wait_for_timeout(3000)
    page.screenshot(path=r"D:\Assets\sunday-scaries\pw_test_attach.png")

    ctx.close()