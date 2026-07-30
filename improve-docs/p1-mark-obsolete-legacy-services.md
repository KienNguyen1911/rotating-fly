# P1.5 — Mark `[Obsolete]` for legacy pipeline services

> **Status:** ✅ Done
> **Goal:** Discourage new code from depending on the legacy single-window pipeline services and nudge callers toward `PipelineOrchestrator`.

---

## 🎯 Why

Two services were kept around only for the legacy UI flows:

- `LegacyVideoPipelineService` — used by the original 5-step `MainWindow.xaml.cs` flow.
- `GeminiVideoPipelineService` — used by the legacy Gemini Creator tab.

Both have been superseded by `PipelineOrchestrator` + the new step services. Without an `[Obsolete]` marker, future code (including AI-assisted suggestions) tends to re-instantiate them.

## 📦 Changes

Marked both classes with `[Obsolete]` and a clear migration message:

```csharp
[Obsolete("Use PipelineOrchestrator instead. This service is kept only for backward compatibility with the legacy 5-step UI flow.")]
public class LegacyVideoPipelineService
```

```csharp
[Obsolete("Use PipelineOrchestrator instead. This service is kept only for the legacy Gemini Creator UI tab.")]
public class GeminiVideoPipelineService
```

Both classes are still registered in DI (so the legacy UI tabs still build and run), but any new consumer will get a compiler warning pointing them at `PipelineOrchestrator`.

## 📂 Affected Files

| File | Change |
|---|---|
| `Services/LegacyVideoPipelineService.cs` | Add `[Obsolete]` attribute |
| `Services/GeminiVideoPipelineService.cs` | Add `[Obsolete]` attribute |

## ✅ Verify

- `dotnet build` → 0 errors, 4+ warnings naming the two obsolete services (expected, from `App.xaml.cs` and `MainWindow.xaml.cs` registration sites).
- Legacy UI tabs continue to function (no runtime behavior change).