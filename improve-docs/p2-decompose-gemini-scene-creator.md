# P2.1 — Tách `GeminiPlaywrightSceneCreator` thành các service chuyên biệt

## 🎯 Mục tiêu

Phá vỡ "God Class" `GeminiPlaywrightSceneCreator` (1.696 LOC) thành các service chuyên biệt, mỗi service một trách nhiệm duy nhất (SRP).

## 🔍 Vấn đề trước refactor

`GeminiPlaywrightSceneCreator.cs` ban đầu chứa **21 method** làm tất cả mọi thứ:
- Khởi tạo Playwright
- Điều hướng UI (Gem, model, thinking mode)
- Attach files (4 strategies)
- Type prompt, click send
- Poll response, extract text + thoughts
- Retry logic (close + reopen + "Continue")
- Save thoughts file

Khi Gemini đổi DOM → phải đọc hiểu cả file để tìm chỗ sửa.

## ✏️ Thay đổi

### Cấu trúc mới

```
Services/
├─ GeminiPlaywrightSceneCreator.cs    (265 LOC) — Orchestrator + retry
└─ Gemini/
   ├─ GeminiBrowserSession.cs          ( 78 LOC) — Playwright lifecycle
   ├─ GeminiNavigator.cs               (347 LOC) — WaitForReady, SelectGem, SelectModel, Thinking
   ├─ GeminiFileAttacher.cs            (291 LOC) — 4 attachment strategies
   ├─ GeminiPromptSubmitter.cs         (210 LOC) — Type + Send + Continue
   └─ GeminiResponseWaiter.cs          (376 LOC) — Wait + extract text + thoughts + chat URL
```

### Trách nhiệm từng class

| Class | Trách nhiệm | Phụ thuộc |
|---|---|---|
| `GeminiPlaywrightSceneCreator` | Điều phối end-to-end + retry-on-timeout | Tất cả 5 class dưới |
| `GeminiBrowserSession` | `InitializeAsync` / `CleanupAsync` / `IAsyncDisposable` | — |
| `GeminiNavigator` | `WaitForReadyAsync` / `SelectGemAsync` / `SelectModelAsync` (4 step + thinking) | `GeminiSelectors` |
| `GeminiFileAttacher` | `AttachAsync` với 4 fallback strategies (textarea drag, menu chooser, file input, JS DataTransfer) | `GeminiSelectors` |
| `GeminiPromptSubmitter` | `TypeAsync` / `ClickSendAsync` / `SendContinueAsync` | `GeminiSelectors` |
| `GeminiResponseWaiter` | `WaitAsync` (poll) + `ExtractResponseTextAsync` + `ExtractThoughtsAsync` + `GetCurrentChatUrlAsync` | `GeminiSelectors`, `GeminiPromptSubmitter` (cho Continue) |

### Mỗi class nhận `IPage` qua constructor

Không còn static state chia sẻ, không còn `_page?` nullable field ở class ngoài.

### Orchestrator pattern

`GeminiPlaywrightSceneCreator.CreateScenesAsync` giờ chỉ là 1 chuỗi method calls:

```csharp
await using var session = new GeminiBrowserSession(...);
await session.InitializeAsync();
var page = session.Page;

var navigator = new GeminiNavigator(page, _log);
await navigator.WaitForReadyAsync();
await navigator.SelectGemAsync(gemName);
await navigator.SelectModelAsync(model, enableThinking);

var fileAttacher = new GeminiFileAttacher(page, _log);
await fileAttacher.AttachAsync(srtPath, transcriptPath);

var submitter = new GeminiPromptSubmitter(page, _log);
var waiter = new GeminiResponseWaiter(page, _log, submitter);
await submitter.TypeAsync(prompt);
await submitter.ClickSendAsync();
var result = await waiter.WaitAsync();
```

Retry logic vẫn ở orchestrator vì liên quan đến **multiple browser sessions**.

## ✅ Verify

```bash
dotnet build
```

**Kết quả:** 0 errors, 25 warnings (đều pre-existing hoặc từ P1).

## 📊 Tác động

| Metric | Trước | Sau |
|---|---|---|
| `GeminiPlaywrightSceneCreator.cs` | 1.696 LOC | **265 LOC** (-84%) |
| Số file | 1 | 6 |
| File lớn nhất | 1.696 | 376 |
| Phương thức trong file lớn nhất | 21 | 7 |
| Số field null-condition (`_page?`, `_context?`, ...) | 3 | 0 (chỉ còn ở BrowserSession) |

## 💡 Lợi ích

1. **Testable** — Mỗi service có thể test với mock `IPage` (Playwright API hỗ trợ in-memory page).
2. **Đọc nhanh hơn** — Đọc `GeminiFileAttacher.cs` để hiểu attach, không cần scroll qua 1500 dòng.
3. **Sửa 1 chỗ** — Gemini đổi DOM → chỉ sửa class tương ứng (vd: file attach → sửa `GeminiFileAttacher`).
4. **Tái sử dụng** — `GeminiNavigator.WaitForReadyAsync` có thể dùng bởi `GeminiTopicResearchStep` nếu sau này muốn chuyển sang Playwright.
5. **Bớt `this._page!.FooAsync()`** — pattern null-forgiving operator giảm mạnh.

## 📌 Còn lại (cho P3.x)

- Vẫn dùng `Action<string> _log` — chưa migrate sang `ILogService` (sẽ làm ở P2.2 kết hợp với `IDomFileDropper`).
- 6 service trong `Gemini/` folder vẫn share `_log` callback — có thể refactor tiếp thành `IGeminiContext` chứa logger + selectors.
- Các timeout magic (`MAX_WAIT_MINUTES = 10`, `RESPONSE_MIN_CHARS = 500`) vẫn trong code → chuyển ra `AppConstants` ở P4.2.