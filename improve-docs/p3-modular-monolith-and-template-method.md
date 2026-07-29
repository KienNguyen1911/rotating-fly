# P3 — Folder/namespace layout, Template Method, MVVM scaffolding

> **Status:** ✅ Partially done (P3.1 + P3.3 demo; P3.2 deferred)
> **Date:** 2026-07-29
> **Scope:** Document the modular-monolith folder layout, introduce an
> `IPipelineStep` Template-Method base, and migrate one step
> (`ThumbnailDownloadStep`) to demonstrate the pattern.

---

## 🎯 Goal

Three things were planned for P3:

1. **P3.1** — split folder/namespace into Core / Infrastructure / Application / UI.
2. **P3.2** — refactor `MainWindow` code-behind into `MainViewModel` + Commands.
3. **P3.3** — apply `IPipelineStep` Template-Method to the pipeline steps.

This turn delivers P3.1 (full) and P3.3 (infrastructure + one step migrated). P3.2 is
deferred because the `MainWindow*.cs` partial-class surface is ~5,000 LOC across 7
files and a single MVVM refactor would touch every XAML binding; that work is large
enough to warrant its own dedicated turn (see "Next steps").

---

## 📦 P3.1 — Folder / Namespace Layout

### New / moved files

| File | Change |
|---|---|
| `Core/AppConstants.cs` | Namespace `AssetAutomator` → `AssetAutomator.Core` |
| `Core/ARCHITECTURE.md` | **New** — codifies folder layout, namespace convention, dependency rules |
| `Core/Abstractions/` | **New folder** (reserved for future cross-cutting interfaces) |
| `Windows/MainWindow.xaml.cs` | + `using AssetAutomator.Core;` |
| `Windows/MainWindow.Tasks.cs` | + `using AssetAutomator.Core;` |
| `MainViewModel.cs` | + `using AssetAutomator.Core;` |
| `Services/GeminiCreatorService.cs` | + `using AssetAutomator.Core;` |

### Architectural rules (recorded in `Core/ARCHITECTURE.md`)

```
Core        → no outward dependencies
Models      → may depend on Core
Helpers     → pure utilities (no Services)
Services    → may depend on Models, Core
Windows     → may depend on Core; should consume VMs/Commands (P3.2)
```

The layout remains a **modular monolith** inside the single
`AssetAutomator.csproj`. Splitting into separate `.csproj` projects is planned for
**P4.1**, at which point the same dependency rules apply physically via project
references.

---

## 📦 P3.3 — `IPipelineStep` Template-Method

### New files

| File | Purpose |
|---|---|
| `Services/Steps/IPipelineStep.cs` | Marker interface (`StepName`) + `StepResult` record |
| `Services/Steps/PipelineStepBase.cs` | Abstract base that handles try/catch, cancellation, log prefix |

### Migrated step

| File | Change |
|---|---|
| `Services/Steps/ThumbnailDownloadStep.cs` | Now `: PipelineStepBase`; only business logic in `ExecuteCoreAsync`; boilerplate removed (~10 lines less). Also gained `CancellationToken` propagation (was missing). |

### Template-method contract

```csharp
public abstract class PipelineStepBase : IPipelineStep
{
    public abstract string StepName { get; }

    public async Task<StepResult> ExecuteAsync(
        AutomationTask task,
        Action<AutomationTask, string> log,
        CancellationToken cancellationToken = default)
    {
        log(task, $"[STEP] {StepName}: start");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await ExecuteCoreAsync(task, log, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            log(task, $"[STEP] {StepName}: {(result.Success ? "ok" : "fail")}");
            return result;
        }
        catch (OperationCanceledException) { log(...); throw; }
        catch (Exception ex) { log(...); return StepResult.Fail(ex.Message); }
    }

    protected abstract Task<StepResult> ExecuteCoreAsync(...);
}
```

`StepResult` is a small record so the orchestrator can chain steps uniformly
without knowing each step's specific output type. `GetOutput<T>()` provides a
typed accessor.

---

## 📂 Affected Files (summary)

| Layer | Files |
|---|---|
| New docs | `Core/ARCHITECTURE.md`, `Services/Steps/IPipelineStep.cs`, `Services/Steps/PipelineStepBase.cs` |
| Namespace move | `Core/AppConstants.cs` |
| Consumers | `Windows/MainWindow.xaml.cs`, `Windows/MainWindow.Tasks.cs`, `MainViewModel.cs`, `Services/GeminiCreatorService.cs` |
| Step migrated | `Services/Steps/ThumbnailDownloadStep.cs` |

---

## ✅ Verify

- `dotnet build` → **0 errors**, 25 warnings (all pre-existing, no new ones).
- `ThumbnailDownloadStep` still constructed via `new ThumbnailDownloadStep()` in
  `MainWindow.xaml.cs` and registered as singleton in `App.xaml.cs` — no DI
  changes required.
- Call sites (`await _step1.ExecuteAsync(task, logTask)`) remain source-compatible
  because C# permits discarding the `Task<StepResult>` return value silently.

---

## 🔭 Next Steps (deferred)

### P3.2 — MVVM refactor of `MainWindow` (NOT done in this turn)
The `MainWindow*.cs` partial-class files total ~5,000 LOC across 7 files. A full
MVVM migration would:

- extract tab-specific `ViewModels` (Profiles, Tasks, History, BatchImageGen, GeminiCreator, AutomationSteps),
- introduce `RelayCommand` / `AsyncRelayCommand`,
- re-bind every `Click` / `SelectionChanged` handler in `MainWindow.xaml` to commands,
- update DI registrations in `App.xaml.cs`.

This is **large enough to warrant a dedicated turn**. Suggested approach: pick
the smallest bounded tab (Profiles or History) and migrate it end-to-end as a
template for the other tabs.

### P3.3 — Migrate remaining 9 steps to `PipelineStepBase`
Steps still on the old `Task ExecuteAsync(...)` shape:

- `TranscriptExtractionStep`
- `ChatGptRewriteStep`
- `VoiceoverGenerationStep`
- `ImageGenerationStep`
- `SceneImageBatchStep`
- `GeminiTopicResearchStep`
- `GeminiPlaywrightSceneBreakdownStep`
- `YoutubeTopicSuggestionStep`
- `GeminiSceneBreakdownStepLegacy` (kept `[Obsolete]`)

Migration is mechanical: derive from `PipelineStepBase`, move body into
`ExecuteCoreAsync`, replace `void` return with `StepResult`. Build remains green
because legacy call sites still compile (return value may be ignored).

### P3.1 — Future namespace cleanup
Once `Windows` no longer references concrete services directly (after P3.2), the
interfaces can physically move to `Core/Abstractions/`. Today they live next to
their implementations because moving them would create circular references
between `Windows` and `Core`.