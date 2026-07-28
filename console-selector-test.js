// Paste this into Chrome DevTools Console on gemini.google.com

const selectors = [
  { selector: "[data-test-id='textarea-inner']", desc: "Textarea inner" },
  { selector: "[data-test-id='bard-mode-menu-button']", desc: "Model menu button" },
  { selector: "[data-test-id='gem-mode-menu']", desc: "Gem mode menu" },
  { selector: "[aria-label='Nhập câu lệnh cho Gemini']", desc: "Input area (VN)" },
  { selector: "[aria-label*='Ask Gemini']", desc: "Input area (EN)" },
  { selector: "[data-test-id='send-button']", desc: "Send button" },
  { selector: "button[aria-label*='Gửi']", desc: "Send button (VN)" },
  { selector: "rich-textarea", desc: "Rich textarea" },
  { selector: "rich-textarea div.ql-editor[contenteditable='true']", desc: "QL Editor" },
  { selector: "[data-test-id='gem-mode-menu'] [role='menuitem']", desc: "Model menu items" },
  { selector: "button:has-text('Hỏi Gemini')", desc: "New chat trigger (VN)" },
  { selector: "button:has-text('Ask Gemini')", desc: "New chat trigger (EN)" },
];

async function testSelectors() {
  console.log("🔍 Testing selectors on Gemini...\n");
  
  for (const { selector, desc } of selectors) {
    try {
      const elements = document.querySelectorAll(selector);
      if (elements.length > 0) {
        console.log(`✅ [${desc}] FOUND (${elements.length} elements)`);
        elements.forEach((el, i) => {
          const rect = el.getBoundingClientRect();
          const visible = rect.width > 0 && rect.height > 0 && 
            window.getComputedStyle(el).visibility !== 'hidden' &&
            window.getComputedStyle(el).display !== 'none';
          console.log(`   [${i}] Tag: ${el.tagName}, Visible: ${visible}, Class: ${el.className || 'none'}`);
          console.log(`       Aria-label: ${el.getAttribute('aria-label') || 'none'}`);
          console.log(`       Data-test-id: ${el.getAttribute('data-test-id') || 'none'}`);
          console.log(`       Rect: ${Math.round(rect.x)},${Math.round(rect.y)} ${Math.round(rect.w)}x${Math.round(rect.h)}`);
        });
      } else {
        console.log(`❌ [${desc}] NOT FOUND`);
        console.log(`   Selector: ${selector}`);
      }
    } catch (e) {
      console.log(`⚠️ [${desc}] ERROR: ${e.message}`);
      console.log(`   Selector: ${selector}`);
    }
    console.log("");
  }
  
  console.log("✅ Done. Check results above.");
}

testSelectors();