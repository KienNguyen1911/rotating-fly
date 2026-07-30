# P2.3 — Thêm `CancellationToken` cho các async method dài

## 🎯 Mục tiêu

Cho phép caller (UI, parent pipeline) **hủy** các tác vụ dài trong `GeminiPlaywrightSceneCreator` — đặc biệt là `WaitForResponseAsync` (đợi 10 phút), `SendPromptThenWaitWithRetryAsync` (có thể lên tới 30+ phút).

## 🔍 Vấn đề trước refactor

```csharp
// Trước:
public async Task<(string SceneJson, string? Thoughts)> CreateScenesAsync(...)
{
    // ... không có cách nào hủy
    var result = await SendPromptThenWaitWithRetryAsync(...);
}
```

**Hậu quả:**
- User đóng cửa sổ / nhấn Stop → app bị "đứng" cho đến khi 10 phút timeout hoặc response xong.
- Không có cách graceful shutdown browser khi user yêu cầu cancel.

## ✏️ Thay đổi

### `CreateScenesAsync` — thêm `CancellationToken cancellationToken = default`

```csharp
public async Task<(string SceneJson, string? Thoughts)> CreateScenesAsync(
    string outputDir, string srtPath, string transcriptPath,
    string gemName, string? gemId, string modelDisplayName, bool enableExtendedThinking,
    CancellationToken cancellationToken = default)  // MỚI
{
    cancellationToken.ThrowIfCancellationRequested();
    // ... propagate xuống các method con ...
}
```

### `SendPromptThenWaitWithRetryAsync` — thêm token + dùng trong các chỗ blocking

```csharp
// Trước:
await Task.Delay(TimeSpan.FromSeconds(COOLDOWN_SECONDS));  // không cancel được

// Sau:
await Task.Delay(TimeSpan.FromSeconds(COOLDOWN_SECONDS), cancellationToken);  // cancel được

// Thêm ThrowIfCancellationRequested() trước mỗi attempt:
for (int attempt = 1; attempt <= MAX_CONTINUE_ATTEMPTS; attempt++)
{
    cancellationToken.ThrowIfCancellationRequested();  // MỚI
    // ...
}
```

### `SendPromptWithFilesAsync` — propagate token

```csharp
private async Task<...> SendPromptWithFilesAsync(
    ...,
    CancellationToken cancellationToken)
{
    // ... không cần dùng trực tiếp vì các service con chưa nhận token,
    //     nhưng đã có sẵn cho việc mở rộng sau này.
}
```

## ✅ Verify

```bash
dotnet build
```

**Kết quả:** 0 errors, 25 warnings (đều pre-existing).

## 📊 Tác động

| API | Trước | Sau |
|---|---|---|
| `CreateScenesAsync` parameters | 7 | 7 + 1 optional (`CancellationToken`) |
| `SendPromptThenWaitWithRetryAsync` parameters | 7 | 7 + 1 (`CancellationToken`) |
| `SendPromptWithFilesAsync` parameters | 6 | 6 + 1 (`CancellationToken`) |
| `ThrowIfCancellationRequested()` calls | 0 | 4 |
| `Task.Delay(..., cancellationToken)` calls | 0 | 2 |
| Breaking change | — | Không (default = `CancellationToken.None`) |

## 💡 Lợi ích

1. **Responsive UI** — User nhấn Stop → `CancellationTokenSource.Cancel()` → throw `OperationCanceledException` ngay lập tức.
2. **Resource cleanup** — `await using` cho `GeminiBrowserSession` đảm bảo browser được đóng khi cancel.
3. **Forward-compatible** — Khi tích hợp với `PipelineOrchestrator` (đã có `CancellationToken`), chỉ cần pass token xuống.

## 📌 Còn lại (cho P3.x)

- Các service con (`GeminiNavigator`, `GeminiFileAttacher`, `GeminiPromptSubmitter`, `GeminiResponseWaiter`) hiện chưa nhận `CancellationToken`. Khi cần cancel chi tiết hơn (vd: hủy giữa typing prompt), sẽ thêm vào.
- `GeminiResponseWaiter.WaitAsync` đang là tight loop `while (DateTime.Now - startTime < wait)` với `Task.Delay(4000)` — nên thêm `cancellationToken.ThrowIfCancellationRequested()` trong vòng lặp.

## Ví dụ caller

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));  // timeout 30 phút
cts.Cancel();  // user nhấn Stop

try
{
    await sceneCreator.CreateScenesAsync(..., cts.Token);
}
catch (OperationCanceledException)
{
    // Browser đã cleanup tự động (await using)
    _logService.Info(LogCategory.Pipeline, "User cancelled scene creation.");
}
```