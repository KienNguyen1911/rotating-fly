"""Test injecting hidden <input type=file> and setting files via Playwright CDP.
This bypasses Chrome's synthetic DataTransfer security block.
"""
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

    print("=== Inject hidden <input type=file> ===")
    page.evaluate("""() => {
        const old = document.querySelector('#__pw_injected_file_input');
        if (old) old.remove();
        const input = document.createElement('input');
        input.type = 'file';
        input.id = '__pw_injected_file_input';
        input.multiple = true;
        input.style.position = 'fixed';
        input.style.left = '-9999px';
        input.style.opacity = '0';
        document.body.appendChild(input);
        window.__pwFileInput = input;
        console.log('[INJECTED] file input', input);
    }""")
    page.wait_for_timeout(500)

    print("=== Set files via Playwright CDP ===")
    try:
        page.set_input_files('#__pw_injected_file_input', TEST_FILES, timeout=10000)
        print(f"✅ set_input_files OK on hidden input")
    except Exception as e:
        print(f"❌ set_input_files failed: {e}")
        ctx.close()
        exit(1)

    page.wait_for_timeout(1000)

    # Now check whether files appeared
    has_files = page.evaluate("""() => {
        const inp = document.querySelector('#__pw_injected_file_input');
        if (!inp) return { ok: false, reason: 'input gone' };
        return {
            files: inp.files ? Array.from(inp.files).map(f => ({ name: f.name, size: f.size, type: f.type })) : [],
            fileCount: inp.files ? inp.files.length : 0,
        };
    }""")
    print(f"\n=== Files in input ===\n{has_files}")

    page.screenshot(path=r"D:\Assets\sunday-scaries\pw_inject_after.png")

    # Now click 'Tải tệp lên' menu item (real menu) — does it open file dialog using our input?
    print("\n=== Try clicking 'Tải tệp lên' menu item ===")
    try:
        page.locator("button[aria-label='Nội dung tải lên và công cụ']").first.click()
        page.wait_for_timeout(1500)
        page.screenshot(path=r"D:\Assets\sunday-scaries\pw_inject_menu_open.png")

        # Now click "Tải tệp lên" with filechooser listener
        with page.expect_file_chooser(timeout=8000) as fc_info:
            page.locator("[role='menuitem']:has-text('Tải tệp lên')").first.click()
        fc = fc_info.value
        print(f"✅ File chooser opened! Setting files: {TEST_FILES}")
        fc.set_files(TEST_FILES)
        page.wait_for_timeout(3000)
        page.screenshot(path=r"D:\Assets\sunday-scaries\pw_inject_after2.png")

        # Check if files appear in chat input
        post = page.evaluate("""() => {
            // Look for file chip in Gemini UI
            const allText = document.body.innerText;
            const fileNames = ['transcript.txt', 'voiceover.srt'];
            return {
                transcript_present: allText.includes('transcript.txt'),
                srt_present: allText.includes('voiceover.srt'),
                upload_chips: [...document.querySelectorAll('[class*=upload], [class*=chip], [class*=file]')]
                    .filter(el => el.textContent && (el.textContent.includes('.txt') || el.textContent.includes('.srt')))
                    .map(el => el.textContent.slice(0, 100))
            };
        }""")
        print(f"\n=== Post-upload state ===\n{post}")
    except Exception as e:
        print(f"❌ Menu approach: {e}")
        page.screenshot(path=r"D:\Assets\sunday-scaries\pw_inject_menu_err.png")

    ctx.close()