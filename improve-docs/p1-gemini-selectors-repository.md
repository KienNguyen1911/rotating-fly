# P1.4 — Tạo `GeminiSelectors` repository

## 🎯 Mục tiêu

Trích **TẤT CẢ** CSS / aria-label / role selector ra một file tập trung `Services/Selectors/GeminiSelectors.cs`. Khi Gemini đổi DOM → sửa 1 chỗ duy nhất.

## 🔍 Vấn đề trước refactor

`GeminiPlaywrightSceneCreator.cs` chứa hàng chục mảng `string[]` hardcode rải rác:
- `gemSelectors`, `modelSelectors`, `sendSelectors`, `turnSelectors`, `contentSelectors`, `thoughtSelectors`, `toggleSelectors`, `uploadButtonSelectors`, `fileMenuItemSelectors`, `attachTexts`, `attachSelectorFallbacks`, `newChatTriggers`, `gemItemSelectors`, `fileDropTargets`, `inputSelectors`...
- Mỗi selector kèm comment dài giải thích (bằng Tiếng Việt) — dấu hiệu "code là documentation không đáng tin".
- Sửa DOM 1 chỗ → phải grep cả file để tìm.

## ✏️ Thay đổi

### File mới

**`Services/Selectors/GeminiSelectors.cs`** (~340 dòng) chứa:

| Loại | Ví dụ |
|---|---|
| Single constants | `TextareaInner`, `BardModeMenuButton`, `GemModeMenu`, `SendButtonContainer`, `RichTextarea` |
| Fallback chains | `InputAreaFallbacks`, `SendButtonFallbacks`, `ModelItemFallbacks`, `GemSelectorButtonFallbacks(gemName)` |
| JS strings | `JsStopButtonDetection`, `JsSendButtonReady`, `JsExtractResponseExcludingThinking`, `GetPromptInputLengthJs()` |
| Timeout constants | `DefaultSelectorTimeoutMs`, `ShortTimeoutMs`, `MediumTimeoutMs`, `InputAreaTimeoutMs` |

### File sửa

**`Services/GeminiPlaywrightSceneCreator.cs`**: ~25 chỗ thay hardcode selector → reference `GeminiSelectors.X`.

Ví dụ:

```csharp
// TRƯỚC — 13 dòng hardcode trong method body
var sendSelectors = new[]
{
    "button[aria-label='Gửi tin nhắn']",
    "button[aria-label='Send message']",
    "button[aria-label*='Send message']",
    // ... 10 dòng nữa
};

// SAU — 1 dòng
var sendSelectors = GeminiSelectors.SendButtonFallbacks;
```

```csharp
// TRƯỚC — 35 dòng JS string trong EvaluateAsync
var result = await _page!.EvaluateAsync<bool>(@"() => {
    const stopSelectors = [ ... ];
    for (const sel of stopSelectors) { ... }
}");

// SAU — 1 dòng
var result = await _page!.EvaluateAsync<bool>(GeminiSelectors.JsStopButtonDetection);
```

## ✅ Verify

```bash
dotnet build
```

**Kết quả:** 0 errors, 33 warnings (đều pre-existing).

## 📊 Tác động

| Metric | Trước | Sau |
|---|---|---|
| LOC của `GeminiPlaywrightSceneCreator.cs` | 1.696 | 1.512 (-184 dòng) |
| Số mảng selector hardcode trong file | ~15 | 0 |
| Số JS string hardcode trong file | ~4 | 0 |
| Số file chứa selector | 1 | 1 (file mới chuyên dụng) |
| Độ dài GeminiSelectors | 0 | ~340 dòng |

## 💡 Lợi ích

1. **DRY** — Một nguồn sự thật duy nhất cho selector.
2. **Bảo trì** — Khi Gemini đổi DOM, chỉ sửa 1 file.
3. **Review** — PR thay đổi selector giờ chỉ touch 1 file.
4. **Versioning** — Có thể thêm comment phiên bản Gemini ở đầu file (`// Gemini 2024-Q4 / 2025`).
5. **Tái sử dụng** — Các service khác (như `GeminiTopicResearchStep`) có thể dùng cùng selector.

## 📌 Còn lại (cho P2.x)

- `ChatGptService` cũng có selector riêng — sẽ tạo `ChatGptSelectors` tương tự khi tách P2.2.
- `YoutubeTopicSuggestionStep`, `GeminiApiService` cũng có selector/API URL hardcode.
- Các timeout còn lại (15s sleep, 10 phút wait) vẫn hardcode trong logic — chuyển ra `GeminiSelectors` hoặc `AppConstants` ở P4.2.