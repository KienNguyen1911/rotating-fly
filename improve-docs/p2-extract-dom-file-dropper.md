# P2.2 — Trích `IDomFileDropper` — Chia sẻ DragDrop JS giữa ChatGPT & Gemini

## 🎯 Mục tiêu

Loại bỏ duplicate code giữa `ChatGptService` và `GeminiFileAttacher` — cả 2 đều có cùng logic "simulate file drag-drop via JS DataTransfer", chỉ khác overlay cleanup strings.

## 🔍 Vấn đề trước refactor

### `ChatGptService` (cũ):
```csharp
private const string DragDropJsTemplate = @"() => {
    const raw = atob(base64); ...
    target.dispatchEvent(new DragEvent('drop', ...));
    // cleanup overlay 'Add anything' / 'Drop any file'
}";
```

### `GeminiFileAttacher` Strategy 4 (cũ):
```csharp
var jsResult = await _page.EvaluateAsync(@"async (filesJson) => {
    const files = JSON.parse(filesJson); ...
    target.dispatchEvent(enter); target.dispatchEvent(over); target.dispatchEvent(drop);
    // cleanup overlay 'Drop' / 'Thả' / 'thả tệp'
}");
```

**~80 dòng JS gần như giống hệt nhau** — sửa 1 chỗ, quên chỗ kia.

## ✏️ Thay đổi

### File mới

**`Services/Browser/IDomFileDropper.cs`** (~30 LOC):
```csharp
public interface IDomFileDropper
{
    Task DropFileAsync(IPage page, string base64Data, string fileName, string mimeType = "text/plain");
    Task DropFileAsync(IPage page, string filePath, string mimeType = "text/plain");
}
```

**`Services/Browser/DomFileDropper.cs`** (~85 LOC):
- Implementation thống nhất — handle cả ChatGPT và Gemini overlay strings.
- 2 overload: base64 hoặc file path.

### Refactor `ChatGptService`

**Trước:**
```csharp
public class ChatGptService
{
    private const string DragDropJsTemplate = @"(args) => { ... 40 dòng JS ... }";

    public async Task SimulateDragDropFileAsync(IPage page, string base64Data, string fileName, string mimeType = "text/plain")
    {
        await page.EvaluateAsync(DragDropJsTemplate, new { base64 = base64Data, name = fileName, mime = mimeType });
    }
}
```

**Sau:**
```csharp
public class ChatGptService
{
    private readonly IDomFileDropper _fileDropper;

    public ChatGptService() : this(new DomFileDropper()) { }  // backward-compat
    public ChatGptService(IDomFileDropper fileDropper) { _fileDropper = fileDropper; }

    public Task SimulateDragDropFileAsync(IPage page, string base64Data, string fileName, string mimeType = "text/plain")
        => _fileDropper.DropFileAsync(page, base64Data, fileName, mimeType);
}
```

### Refactor `GeminiFileAttacher`

Strategy 4 giảm từ ~70 dòng JS inline xuống 1 call:
```csharp
foreach (var filePath in filesToAttach)
{
    string mimeType = MimeTypeHelper.GetMimeType(filePath);
    await _fileDropper.DropFileAsync(_page, filePath, mimeType);
}
```

### Đăng ký DI

```csharp
services.AddSingleton<IDomFileDropper, DomFileDropper>();
```

## ✅ Verify

```bash
dotnet build
```

**Kết quả:** 0 errors, 25 warnings (đều pre-existing).

## 📊 Tác động

| Metric | Trước | Sau |
|---|---|---|
| Số bản copy của DragDrop JS | 2 | 1 |
| LOC JS inline trong GeminiFileAttacher | ~70 | 0 |
| LOC JS inline trong ChatGptService | ~40 | 0 |
| Constructor mới của `ChatGptService` | 0 | 1 (nhận `IDomFileDropper`) |
| Backward-compat constructor | — | Có (default `new DomFileDropper()`) |

## 💡 Lợi ích

1. **DRY** — Một nguồn sự thật cho drag-drop JS.
2. **Testable** — `IDomFileDropper` có thể mock để test logic mà không cần Playwright thật.
3. **Mở rộng** — Sau này cần support thêm UI khác (Claude.ai, Grok...) chỉ cần tạo `IDomFileDropper` mới hoặc mở rộng `DomFileDropper`.
4. **Bớt magic** — `MimeTypeHelper.GetMimeType()` đã được dùng đúng chỗ (file path → mime).

## 📌 Còn lại (cho P3.x)

- Các file khác (như `GeminiTopicResearchStep`?) có thể cũng dùng `IDomFileDropper` nếu có cần attach file.
- Có thể tách overlay cleanup strings thành configuration trong `IDomFileDropper` nếu sau này phát sinh thêm UI mới.