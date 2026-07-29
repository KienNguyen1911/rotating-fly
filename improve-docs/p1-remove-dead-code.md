# P1.1 — Xóa Dead Code: `GeminiSceneBreakdownStepLegacy`

## 🎯 Mục tiêu

Loại bỏ class `GeminiSceneBreakdownStepLegacy` — class `[Obsolete]` không còn client nào sử dụng thực sự (chỉ còn được đăng ký trong DI nhưng không ai gọi).

## 🔍 Phân tích trước refactor

Trước khi xóa, đã grep toàn bộ codebase:

```
Services/Steps/GeminiSceneBreakdownStep.cs  ← định nghĩa class GeminiSceneBreakdownStepLegacy
App.xaml.cs                                  ← chỉ đăng ký trong DI, không có client
```

**Kết luận**: Class chỉ được `AddSingleton` trong `App.xaml.cs` nhưng không có chỗ nào `GetService<GeminiSceneBreakdownStepLegacy>()` → dead code 100%.

## ✏️ Thay đổi

### File xóa
- ❌ `Services/Steps/GeminiSceneBreakdownStep.cs` (10.901 bytes, 192 dòng)

### File sửa
- ✅ `App.xaml.cs` — Xóa 4 dòng đăng ký DI:

```csharp
// TRƯỚC
// Active: Playwright-only Scene Creator (drives real Gemini Web UI).
services.AddSingleton<GeminiPlaywrightSceneBreakdownStep>();
// Legacy: API-mode fallback, opt-in only. Registered explicitly for backward compatibility.
#pragma warning disable CS0618 // Type or member is obsolete
services.AddSingleton<GeminiSceneBreakdownStepLegacy>();
#pragma warning restore CS0618
services.AddSingleton<SceneImageBatchStep>();

// SAU
// Active: Playwright-only Scene Creator (drives real Gemini Web UI).
services.AddSingleton<GeminiPlaywrightSceneBreakdownStep>();
services.AddSingleton<SceneImageBatchStep>();
```

## ✅ Verify

```bash
dotnet build
```

**Kết quả:**
- 0 errors
- 15 warnings (đều pre-existing, không liên quan đến thay đổi này)

## 📊 Tác động

- **LOC giảm**: 192 dòng
- **Cognitive load**: Giảm noise, IDE không cần suggest class obsolete khi type.
- **Không breaking change**: Không có client → không ảnh hưởng runtime.

## 📌 Ghi chú các service KHÔNG xóa (vì vẫn còn client)

| Service | Lý do giữ | Hành động tiếp theo |
|---|---|---|
| `LegacyVideoPipelineService` | Được `MainWindow.xaml.cs`, `MainViewModel.cs`, `MainWindow.AutomationSteps.cs` sử dụng | Đánh `[Obsolete]` (P1.5) |
| `GeminiVideoPipelineService` | Được `MainWindow.GeminiCreator.cs` sử dụng | Đánh `[Obsolete]` (P1.5) |
| `GeminiCreatorService` | Được `MainWindow.GeminiCreator.cs` sử dụng | Giữ — đang được dùng cho 1 flow cụ thể |
| `ScenesJsonRootModel` | Được dùng ở 7 file | Giữ |