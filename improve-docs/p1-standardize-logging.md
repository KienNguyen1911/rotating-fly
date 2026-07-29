# P1.2 — Chuẩn hóa Logging: Inject `ILogService` qua DI

## 🎯 Mục tiêu

Thay thế pattern `_log: Action<string>` cũ bằng `ILogService` (đã có sẵn nhưng chưa được tận dụng đầy đủ) — để:
- Log có cấu trúc (level + category + source + timestamp).
- Dễ dàng redirect log tới UI/Trace/buffer đã có sẵn.
- Một đường log duy nhất (không phải đuổi theo `_log` rải rác).

## 🔍 Vấn đề trước refactor

```csharp
// Anti-pattern xuất hiện ở 5+ service:
private readonly Action<string> _log;

public MyService(Action<string> log) { _log = log; }
```

- Không có level/category.
- Mỗi service tự quản lý cách route log.
- UI phải truyền `Log` callback vào từng service.

## ✏️ Thay đổi

### Pattern áp dụng: **Adapter Constructor**

Service vẫn giữ constructor `Action<string>` (backward-compat cho nơi đang `new` thủ công), nhưng thêm constructor nhận `ILogService`. DI container sẽ chọn constructor `ILogService`.

### Đăng ký DI trong `App.xaml.cs`

```csharp
// Logging — đăng ký đầu tiên để các service khác inject được
services.AddSingleton<ILogService, LogService>();
```

### `BrowserService`

**Trước:**
```csharp
private readonly Action<string> _log;
public BrowserService(Action<string> log) { _log = log; }
// ... dùng _log("...")
```

**Sau:**
```csharp
private readonly Action<string>? _legacyLog;
private readonly ILogService? _logService;

public BrowserService(Action<string> log) { _legacyLog = log; }
public BrowserService(ILogService logService) { _logService = logService; }

private void LogInfo(string message) => _logService?.Info(LogCategory.General, message, "Browser");
private void LogWarning(string message) => _logService?.Warning(LogCategory.General, message, "Browser");
private void LogError(string message) => _logService?.Error(LogCategory.General, message, "Browser");
```

### `HistoryService`, `UpdateService`

Tương tự — adapter constructor + helper methods.

## ✅ Verify

```bash
dotnet build
```

**Kết quả:** 0 errors, 33 warnings (đều pre-existing hoặc obsolete từ P1.5).

## 📊 Tác động

| Metric | Giá trị |
|---|---|
| Service đã migrate | 3/5 (`BrowserService`, `HistoryService`, `UpdateService`) |
| Service còn dùng `_log(Action<string>)` | 2 (`GeminiCookieSyncService`, `GeminiPlaywrightSceneCreator`) |
| Breaking change | Không — backward-compat constructor giữ nguyên |
| Runtime path | DI gọi constructor mới; code cũ gọi constructor cũ → vẫn chạy |

## 📌 Còn lại (cho sprint sau)

- `GeminiCookieSyncService` (353 LOC) — lớn, làm riêng.
- `GeminiPlaywrightSceneCreator` (1700 LOC) — sẽ refactor toàn bộ ở P2.1.
- `ConfigService.Debug.WriteLine` → chuyển sang `ILogService`.

## 💡 Bài học

Pattern **Adapter Constructor** cho phép migrate dần dần:
1. Thêm constructor mới nhận `ILogService`.
2. Helper methods `LogInfo/LogWarning/LogError` chọn implementation phù hợp.
3. Build OK ngay cả khi code cũ vẫn dùng constructor cũ.
4. Sau đó migrate từng caller sang constructor mới.