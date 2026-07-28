"""
Test script: drive Gemini Web UI via Playwright Python (sync API).
Goal: replicate what GeminiPlaywrightSceneCreator.cs does in C#, but in Python,
to identify which selectors still work against the CURRENT Gemini DOM.

Usage:
    python test_gemini_playwright.py

Outputs:
    - Console log with [PW] prefix for every step
    - Screenshots in D:\Assets\sunday-scaries\pw_test_*.png
    - Dumps the Gemini DOM tree to gemini_dom.html for inspection
"""
import os
import sys
import time
import json
import traceback
from pathlib import Path

from playwright.sync_api import sync_playwright, TimeoutError as PWTimeout, Error as PWError

# ────────────────────────────────────────────────────────────
# Config
# ────────────────────────────────────────────────────────────
PROFILE_PATH = r"D:\ChromeProfiles\taro.siomi7@gmail.com"
GEM_ID = "b1af0c371214"
GEM_URL = f"https://gemini.google.com/gem/{GEM_ID}"
ASSETS_DIR = Path(r"D:\Assets\sunday-scaries")
SRT_PATH = ASSETS_DIR / "voiceover.srt"
TRANSCRIPT_PATH = ASSETS_DIR / "transcript.txt"
MODEL_NAME = "2.5 Pro"   # Display name; adjust if your UI uses different label
ENABLE_THINKING = True

OUTPUT_DIR = ASSETS_DIR
DEBUG_HTML = OUTPUT_DIR / "gemini_dom.html"
SCREENSHOT_LOAD = OUTPUT_DIR / "pw_test_01_loaded.png"
SCREENSHOT_INPUT = OUTPUT_DIR / "pw_test_02_input.png"
SCREENSHOT_MODEL = OUTPUT_DIR / "pw_test_03_model.png"
SCREENSHOT_FILES = OUTPUT_DIR / "pw_test_04_files.png"
SCREENSHOT_PROMPT = OUTPUT_DIR / "pw_test_05_prompt.png"
SCREENSHOT_SENT = OUTPUT_DIR / "pw_test_06_sent.png"
SCREENSHOT_RESPONSE = OUTPUT_DIR / "pw_test_07_response.png"
SCREENSHOT_FINAL = OUTPUT_DIR / "pw_test_08_final.png"

# ────────────────────────────────────────────────────────────
# Logging helpers
# ────────────────────────────────────────────────────────────
def log(msg):
    ts = time.strftime("%H:%M:%S")
    print(f"[{ts}] {msg}", flush=True)

def log_section(title):
    log("=" * 70)
    log(f"  {title}")
    log("=" * 70)

# ────────────────────────────────────────────────────────────
# Selector probing: try a list, return the first that matches
# ────────────────────────────────────────────────────────────
def probe_selectors(page, selectors, label, timeout=3000, require_visible=True):
    """
    Returns (selector_that_worked, element_handle) or (None, None).
    Logs each try. Doesn't throw.
    """
    state = "visible" if require_visible else "attached"
    for sel in selectors:
        try:
            log(f"   [PROBE] trying {label}: {sel!r}")
            el = page.wait_for_selector(sel, timeout=timeout, state=state)
            if el is not None:
                log(f"   [PROBE] ✅ MATCHED {label}: {sel!r}")
                return sel, el
        except PWTimeout:
            log(f"   [PROBE] ⚠️  timeout ({label}): {sel!r}")
        except Exception as e:
            log(f"   [PROBE] ⚠️  error {type(e).__name__}: {e!s}  selector={sel!r}")
    return None, None

# ────────────────────────────────────────────────────────────
# Dump current DOM for debugging
# ────────────────────────────────────────────────────────────
def dump_dom(page, label):
    try:
        html = page.content()
        DEBUG_HTML.write_text(html, encoding="utf-8")
        log(f"   [DOM] dumped ({len(html)} chars) → {DEBUG_HTML}")
        # Also dump a tree of role=button / role=textbox / contenteditable for quick inspection
        info = page.evaluate("""() => {
            const out = {url: location.href, title: document.title};
            const buttons = [...document.querySelectorAll('button, [role="button"]')].slice(0, 40).map(b => ({
                text: (b.innerText || b.textContent || '').trim().slice(0, 80),
                aria: b.getAttribute('aria-label'),
                dataTestId: b.getAttribute('data-test-id'),
                visible: !!(b.offsetParent !== null || b.getClientRects().length)
            }));
            const textboxes = [...document.querySelectorAll('[contenteditable="true"], textarea, [role="textbox"]')].slice(0, 20).map(t => ({
                tag: t.tagName,
                aria: t.getAttribute('aria-label'),
                dataTestId: t.getAttribute('data-test-id'),
                classes: t.className.slice(0, 100),
                visible: !!(t.offsetParent !== null || t.getClientRects().length)
            }));
            const richTextareas = [...document.querySelectorAll('rich-textarea')].slice(0, 10).map(r => ({
                aria: r.getAttribute('aria-label'),
                inner: r.innerHTML.slice(0, 200)
            }));
            return {...out, buttons, textboxes, richTextareas};
        }""")
        log(f"   [DOM] URL={info.get('url')} TITLE={info.get('title')!r}")
        log(f"   [DOM] buttons found: {len(info.get('buttons', []))}")
        for i, b in enumerate(info.get("buttons", [])[:15]):
            log(f"      btn[{i}] text={b.get('text','')!r} aria={b.get('aria')!r} test-id={b.get('dataTestId')!r} visible={b.get('visible')}")
        log(f"   [DOM] textboxes found: {len(info.get('textboxes', []))}")
        for i, t in enumerate(info.get("textboxes", [])[:10]):
            log(f"      tb[{i}] tag={t.get('tag')} aria={t.get('aria')!r} test-id={t.get('dataTestId')!r} visible={t.get('visible')}")
        log(f"   [DOM] rich-textareas found: {len(info.get('richTextareas', []))}")
        for i, r in enumerate(info.get("richTextareas", [])[:5]):
            log(f"      rt[{i}] aria={r.get('aria')!r} inner={r.get('inner','')[:80]!r}")
    except Exception as e:
        log(f"   [DOM] dump failed: {e!s}")

# ────────────────────────────────────────────────────────────
# Main test flow
# ────────────────────────────────────────────────────────────
def run_test():
    log_section("Starting Playwright + Gemini test")
    log(f"Profile: {PROFILE_PATH}")
    log(f"Gem URL: {GEM_URL}")
    log(f"SRT exists: {SRT_PATH.exists()} ({SRT_PATH.stat().st_size if SRT_PATH.exists() else 0} bytes)")
    log(f"Transcript exists: {TRANSCRIPT_PATH.exists()} ({TRANSCRIPT_PATH.stat().st_size if TRANSCRIPT_PATH.exists() else 0} bytes)")

    results = {"steps": {}, "errors": []}

    with sync_playwright() as p:
        # Launch chromium with persistent context
        log_section("Step 1: Launching Chromium with persistent profile")
        try:
            context = p.chromium.launch_persistent_context(
                user_data_dir=PROFILE_PATH,
                headless=False,
                channel="chrome",
                args=[
                    "--disable-blink-features=AutomationControlled",
                    "--no-sandbox",
                    "--disable-features=TranslateUI",
                    "--start-maximized",
                ],
                viewport={"width": 1400, "height": 900},
                accept_downloads=True,
            )
            page = context.pages[0] if context.pages else context.new_page()
            log(f"✅ Browser launched. Page URL: {page.url}")
            results["steps"]["launch"] = "OK"
        except Exception as e:
            log(f"❌ Browser launch failed: {type(e).__name__}: {e}")
            results["steps"]["launch"] = f"FAIL: {e}"
            return results

        # Navigate
        log_section("Step 2: Navigate to Gemini gem page (DOMContentLoaded, 30s)")
        try:
            t0 = time.time()
            page.goto(GEM_URL, wait_until="domcontentloaded", timeout=30000)
            log(f"✅ Navigation OK in {time.time()-t0:.2f}s. URL: {page.url}")
            results["steps"]["navigate"] = "OK"
        except Exception as e:
            log(f"❌ Navigation failed: {type(e).__name__}: {e}")
            log(f"   Current URL: {page.url}")
            page.screenshot(path=str(SCREENSHOT_LOAD))
            results["steps"]["navigate"] = f"FAIL: {e}"
            results["errors"].append(("navigate", str(e)))
            context.close()
            return results

        page.wait_for_timeout(2000)
        page.screenshot(path=str(SCREENSHOT_LOAD))
        dump_dom(page, "after navigation")

        # Step 3: Find input area
        log_section("Step 3: Find input area (textbox / contenteditable)")
        INPUT_SELECTORS = [
            "[data-test-id='textarea-inner']",
            "rich-textarea div.ql-editor[contenteditable='true']",
            "[aria-label='Nhập câu lệnh cho Gemini']",
            "[aria-label*='Nhập câu lệnh']",
            "[aria-label*='Enter a prompt']",
            "[aria-label*='prompt']",
            "rich-textarea",
            "[contenteditable='true']",
            "div[role='textbox']",
            "textarea",
        ]
        sel_input, el_input = probe_selectors(page, INPUT_SELECTORS, "input-area", timeout=4000)
        page.screenshot(path=str(SCREENSHOT_INPUT))
        if not el_input:
            log("❌ No input area found. Will try clicking 'new chat' triggers...")
        else:
            log(f"✅ Input area found: {sel_input!r}")
            results["steps"]["input_area"] = sel_input

        # Step 3b: If no input, try new-chat triggers
        if not el_input:
            log_section("Step 3b: Trying 'new chat' triggers on Gem page")
            NEWCHAT_SELECTORS = [
                "button:has-text('Hỏi Gemini')",
                "[aria-label*='Hỏi Gemini']",
                "[aria-label*='Ask Gemini']",
                "button:has-text('Ask Gemini')",
                "button[aria-label*='New chat']",
                "button:has-text('New chat')",
                "[data-test-id='new-chat-button']",
                "button:has-text('Bắt đầu')",
                "button:has-text('Start')",
                "button:has-text('Gemini')",
                "[role='button']:has-text('Gemini')",
            ]
            sel_btn, el_btn = probe_selectors(page, NEWCHAT_SELECTORS, "new-chat-trigger", timeout=3000)
            if el_btn:
                try:
                    el_btn.click()
                    log(f"✅ Clicked new-chat trigger: {sel_btn!r}")
                    page.wait_for_timeout(1500)
                    sel_input, el_input = probe_selectors(page, INPUT_SELECTORS, "input-area (retry)", timeout=5000)
                except Exception as e:
                    log(f"❌ Click on new-chat trigger failed: {e}")
            else:
                log("❌ No new-chat trigger found.")

        if not el_input:
            log("❌ STILL no input area. Aborting further steps.")
            page.screenshot(path=str(SCREENSHOT_FINAL))
            dump_dom(page, "no input")
            context.close()
            results["steps"]["input_area"] = "FAIL"
            return results

        # Step 4: Open model selector
        log_section("Step 4: Open model selector dropdown")
        MODEL_BTN_SELECTORS = [
            "[data-test-id='bard-mode-menu-button']",
            "[aria-label*='chọn chế độ']",
            "[aria-label*='chọn chế']",
            "[aria-label*='Select model']",
            "[aria-label*='model menu']",
            "button[aria-label*='model']",
            "button:has-text('Flash')",
            "button:has-text('Pro')",
        ]
        sel_model_btn, el_model_btn = probe_selectors(page, MODEL_BTN_SELECTORS, "model-button", timeout=4000)
        if el_model_btn:
            try:
                el_model_btn.click()
                page.wait_for_timeout(800)
                log(f"✅ Clicked model selector: {sel_model_btn!r}")
                results["steps"]["model_selector_open"] = sel_model_btn
            except Exception as e:
                log(f"❌ Click model selector failed: {e}")
        else:
            log("❌ Could not find/click model selector.")
            results["steps"]["model_selector_open"] = "FAIL"

        page.screenshot(path=str(SCREENSHOT_MODEL))
        dump_dom(page, "after model selector open")

        # Step 4b: Pick model
        log_section("Step 4b: Pick model from dropdown")
        MODEL_ITEM_SELECTORS = [
            f"[data-test-id='gem-mode-menu'] [role='menuitem']:has-text(\"{MODEL_NAME}\")",
            f"[role='menuitem']:has-text(\"{MODEL_NAME}\")",
            f"[data-test-id='gem-mode-menu'] >> text=\"{MODEL_NAME}\"",
            f"[role='menuitemradio']:has-text(\"{MODEL_NAME}\")",
        ]
        sel_model_item, el_model_item = probe_selectors(page, MODEL_ITEM_SELECTORS, "model-item", timeout=3000)
        if el_model_item:
            try:
                el_model_item.click()
                page.wait_for_timeout(500)
                log(f"✅ Selected model: {sel_model_item!r}")
                results["steps"]["model_picked"] = sel_model_item
            except Exception as e:
                log(f"❌ Click model item failed: {e}")
        else:
            log(f"⚠️ Model '{MODEL_NAME}' item not found. May already be selected or label differs.")

        # Step 4c: Enable extended thinking (Tư duy mở rộng)
        if ENABLE_THINKING:
            log_section("Step 4c: Enable extended thinking")
            THINK_SELECTORS = [
                "[data-test-id='gem-mode-menu'] [role='menuitem']:has-text('Tư duy mở rộng')",
                "[role='menuitem']:has-text('Tư duy mở rộng')",
                "[role='menuitem']:has-text('Extended thinking')",
                "[data-test-id='gem-mode-menu'] >> text='Tư duy mở rộng'",
                "[role='menuitemcheckbox']",
            ]
            sel_think, el_think = probe_selectors(page, THINK_SELECTORS, "thinking", timeout=3000)
            if el_think:
                try:
                    el_think.click()
                    page.wait_for_timeout(500)
                    log(f"✅ Clicked thinking: {sel_think!r}")
                except Exception as e:
                    log(f"❌ Click thinking failed: {e}")
            else:
                log("⚠️ Thinking option not found (model may not support or already enabled).")

        # Step 5: Attach files
        log_section("Step 5: Attach SRT + transcript files")
        files_to_attach = []
        if TRANSCRIPT_PATH.exists():
            files_to_attach.append(str(TRANSCRIPT_PATH))
        if SRT_PATH.exists():
            files_to_attach.append(str(SRT_PATH))
        log(f"Files to attach: {files_to_attach}")

        attached = False
        # Strategy 1: SetInputFiles on drop targets
        for target in ["[data-test-id='textarea-inner']", "rich-textarea", "[contenteditable='true']", "[role='textbox']"]:
            try:
                log(f"   [ATTACH] Strategy 1: SetInputFiles on {target!r}")
                loc = page.locator(target).first
                loc.set_input_files(files_to_attach)
                page.wait_for_timeout(1500)
                attached = True
                log(f"   [ATTACH] ✅ Files attached via {target!r}")
                break
            except Exception as e:
                log(f"   [ATTACH] ⚠️ {target!r} failed: {type(e).__name__}: {e!s}")

        # Strategy 2: Click upload button → menu → click "Tải tệp lên" → file chooser
        # Gemini opens a menu first; the file-upload item inside that menu is what triggers native file chooser.
        if not attached:
            try:
                log("   [ATTACH] Strategy 2: Click 'Nội dung tải lên và công cụ' → 'Tải tệp lên' menu item → file chooser")
                upload_btn = page.locator("button[aria-label='Nội dung tải lên và công cụ']").first
                upload_btn.click()
                page.wait_for_timeout(1500)

                menu_item_selectors = [
                    "[role='menuitem']:has-text('Tải tệp lên')",
                    "[role='menuitem']:has-text('Upload files')",
                    "[role='menuitem']:has-text('Upload file')",
                    "button:has-text('Tải tệp lên')",
                ]
                menu_item = None
                for ms in menu_item_selectors:
                    try:
                        mi = page.locator(ms).first
                        if mi.count() > 0:
                            menu_item = mi
                            log(f"   [ATTACH] Found menu item: {ms!r}")
                            break
                    except Exception:
                        continue

                if menu_item:
                    with page.expect_file_chooser(timeout=8000) as fc_info:
                        menu_item.click()
                    file_chooser = fc_info.value
                    file_chooser.set_files(files_to_attach)
                    page.wait_for_timeout(2500)
                    attached = True
                    log("   [ATTACH] ✅ Files attached via menu → file chooser")
                else:
                    log("   [ATTACH] ⚠️ No 'Tải tệp lên' menu item found after opening menu")
                    # Close menu
                    page.keyboard.press("Escape")
                    page.wait_for_timeout(500)
            except Exception as e:
                log(f"   [ATTACH] ⚠️ Upload button → menu approach failed: {type(e).__name__}: {e}")
                try:
                    page.keyboard.press("Escape")
                    page.wait_for_timeout(500)
                except Exception:
                    pass

        if not attached:
            # Strategy 3: try file input directly
            try:
                log("   [ATTACH] Strategy 3: query input[type='file']")
                fi = page.query_selector("input[type='file']")
                if fi:
                    fi.set_input_files(files_to_attach)
                    page.wait_for_timeout(1500)
                    attached = True
                    log("   [ATTACH] ✅ Files attached via input[type='file']")
            except Exception as e:
                log(f"   [ATTACH] ⚠️ input[type='file'] failed: {e}")

        page.screenshot(path=str(SCREENSHOT_FILES))
        results["steps"]["files_attached"] = attached

        # Step 6: Type prompt
        log_section("Step 6: Type prompt into input")
        PROMPT = (
            "Tạo scenes JSON cho video từ file SRT và transcript đính kèm."
        )
        log(f"Prompt length: {len(PROMPT)} chars")

        # Close any open menu first (upload menu often stays open after attach attempt)
        log("   [PRE-TYPE] Pressing Escape to close any open menu/overlay...")
        try:
            page.keyboard.press("Escape")
            page.wait_for_timeout(500)
        except Exception:
            pass

        # Re-find input (Gemini may have re-rendered after file attach)
        sel_input2, el_input2 = probe_selectors(page, INPUT_SELECTORS, "input-area (before type)", timeout=4000)
        if not el_input2:
            log("❌ Input area vanished after file attach. Aborting.")
            page.screenshot(path=str(SCREENSHOT_FINAL))
            context.close()
            return results

        try:
            el_input2.click()
            page.wait_for_timeout(500)

            # Clear any residual text in the input
            try:
                page.keyboard.press("Control+a")
                page.wait_for_timeout(100)
                page.keyboard.press("Delete")
                page.wait_for_timeout(200)
            except Exception:
                pass

            # Type with human-like delay (40ms is realistic, fast but not bot-like)
            # 271 chars × 40ms ≈ 11s, well within patience but visibly human-paced
            TYPE_DELAY_MS = 40
            log(f"   [TYPE] Typing {len(PROMPT)} chars @ {TYPE_DELAY_MS}ms/keystroke (≈{len(PROMPT)*TYPE_DELAY_MS/1000:.1f}s)...")
            page.keyboard.type(PROMPT, delay=TYPE_DELAY_MS)
            page.wait_for_timeout(800)  # let Gemini update internal state

            # Verify typed text matches by reading DOM back
            typed_len = page.evaluate("""() => {
                const inp = document.querySelector('[aria-label="Nhập câu lệnh cho Gemini"]')
                    || document.querySelector('.ql-editor')
                    || document.querySelector('[contenteditable="true"]');
                return inp ? (inp.innerText || inp.textContent || '').length : -1;
            }""")
            expected_len = len(PROMPT)
            log(f"   [TYPE] DOM typed length: {typed_len}, expected: {expected_len}")

            if typed_len < expected_len * 0.85:
                # Less than 85% typed — likely lost keystrokes. Re-type missing portion.
                log(f"   [TYPE] ⚠️ Only {typed_len}/{expected_len} chars typed. Retrying missing portion...")
                # Just retry the whole prompt
                el_input2.click()
                page.wait_for_timeout(300)
                page.keyboard.press("Control+a")
                page.wait_for_timeout(100)
                page.keyboard.press("Delete")
                page.wait_for_timeout(300)
                page.keyboard.type(PROMPT, delay=TYPE_DELAY_MS)
                page.wait_for_timeout(800)

            log("✅ Prompt typed")
            results["steps"]["prompt_typed"] = True
        except Exception as e:
            log(f"❌ Typing failed: {e}")
            results["steps"]["prompt_typed"] = False

        page.screenshot(path=str(SCREENSHOT_PROMPT))

        # Close any menu that opened during typing
        try:
            page.keyboard.press("Escape")
            page.wait_for_timeout(500)
        except Exception:
            pass

        # Step 7: Click send button
        log_section("Step 7: Click send button")
        SEND_SELECTORS = [
            "button[aria-label='Gửi tin nhắn']",
            "button[aria-label='Send message']",
            "button[aria-label*='Send message']",
            "button[aria-label*='Send']",
            "button:has(svg[data-test-id='send-icon'])",
            "[data-test-id='send-button']",
            "button[class*='send-button']",
            "button.send-button",
        ]
        sel_send, el_send = probe_selectors(page, SEND_SELECTORS, "send-button", timeout=3000)
        sent = False
        if el_send:
            try:
                el_send.click()
                sent = True
                log(f"✅ Clicked send: {sel_send!r}")
            except Exception as e:
                log(f"❌ Click send failed: {e}")

        if not sent:
            log("   [SEND] Falling back to Enter key")
            try:
                page.keyboard.press("Enter")
                sent = True
                log("   [SEND] ✅ Pressed Enter")
            except Exception as e:
                log(f"   [SEND] ❌ Enter failed: {e}")

        page.screenshot(path=str(SCREENSHOT_SENT))
        results["steps"]["sent"] = sent

        if not sent:
            log("❌ Cannot send prompt. Aborting.")
            context.close()
            return results

        # Step 8: Wait for response (poll for stable text)
        log_section("Step 8: Wait for response (poll up to 5 min)")
        last_text = ""
        stable_count = 0
        REQUIRED_STABLE = 3
        POLL_INTERVAL = 4000
        MAX_WAIT = 300
        start = time.time()
        final_text = ""
        final_thoughts = None

        while time.time() - start < MAX_WAIT:
            page.wait_for_timeout(POLL_INTERVAL)
            elapsed = time.time() - start
            try:
                # Check if still generating
                still_gen = False
                for sel in ["button[aria-label*='Stop']", "button[aria-label*='Dừng']", "[data-test-id='stop-response']", ".stop-generating"]:
                    try:
                        el = page.query_selector(sel)
                        if el and el.is_visible():
                            still_gen = True
                            log(f"   [{elapsed:.0f}s] ⏳ still generating (matched {sel!r})")
                            break
                    except Exception:
                        pass

                # Extract response text
                current_text = ""
                for sel in [
                    "[data-test-role='model']",
                    "[data-message-role='model']",
                    "model-response",
                    ".model-response",
                    "div.markdown",
                ]:
                    try:
                        els = page.query_selector_all(sel)
                        if els:
                            t = els[-1].inner_text() if hasattr(els[-1], 'inner_text') else els[-1].text_content()
                            if t and len(t.strip()) > 50:
                                current_text = t.strip()
                                break
                    except Exception:
                        pass

                # Fallback: body text
                if not current_text:
                    try:
                        current_text = (page.inner_text("body") or "").strip()
                    except Exception:
                        pass

                if current_text and current_text != last_text:
                    log(f"   [{elapsed:.0f}s] 📝 response updated: {len(last_text)} → {len(current_text)} chars")
                    last_text = current_text
                    stable_count = 0
                elif current_text and not still_gen:
                    stable_count += 1
                    log(f"   [{elapsed:.0f}s] 📊 stable {stable_count}/{REQUIRED_STABLE} ({len(current_text)} chars)")
                    if stable_count >= REQUIRED_STABLE:
                        final_text = current_text
                        log(f"✅ Response stable. {len(final_text)} chars.")
                        break
                else:
                    if still_gen:
                        stable_count = 0

            except Exception as e:
                log(f"   [{elapsed:.0f}s] ⚠️ poll error: {e}")

        if not final_text:
            final_text = last_text
            log(f"⚠️ Timeout. Using last captured text ({len(final_text)} chars).")

        page.screenshot(path=str(SCREENSHOT_RESPONSE))
        results["steps"]["response_chars"] = len(final_text)
        results["final_text_preview"] = final_text[:500]

        # Step 9: Try extract thoughts
        log_section("Step 9: Try extract thoughts/thinking")
        for sel in [
            "[data-test-id='thinking-section']",
            "[data-test-id='model-thinking']",
            ".thinking-content",
            "button:has-text('Suy nghĩ')",
            "button:has-text('Thinking')",
            "button:has-text('Ẩn suy nghĩ')",
            "details summary",
        ]:
            try:
                el = page.query_selector(sel)
                if el:
                    text = el.inner_text() if hasattr(el, 'inner_text') else el.text_content()
                    if text and len(text.strip()) > 20:
                        log(f"   [THOUGHTS] Found via {sel!r}: {len(text)} chars")
                        final_thoughts = text.strip()
                        break
            except Exception:
                pass
        results["thoughts_chars"] = len(final_thoughts) if final_thoughts else 0

        page.screenshot(path=str(SCREENSHOT_FINAL))
        log_section("Summary")
        log(json.dumps(results, indent=2, ensure_ascii=False))

        log("Cleaning up browser...")
        context.close()
        log("Done.")

    return results


if __name__ == "__main__":
    try:
        run_test()
    except Exception:
        log("FATAL:")
        traceback.print_exc()
        sys.exit(1)