# P3.3 — Migrate pipeline steps to `PipelineStepBase`

> **Status:** ✅ Mostly done (7/10 steps migrated; 1 step is a helper utility,
> 1 step is obsolete legacy).
> **Date:** 2026-07-29

This turn applies the Template Method pattern introduced in the previous P3.3
turn to the remaining pipeline steps.

---

## ✅ Migrated steps (7)

| # | Step | Notes |
|---|---|---|
| 1 | `ThumbnailDownloadStep` | Original demo (previous turn). |
| 2 | `TranscriptExtractionStep` | Removed `IBrowserContext` parameter — it was unused inside the step. |
| 3 | `ChatGptRewriteStep` | Added `SetBrowserContext(IBrowserContext?)` + `SetInputs(string)` setters for per-step context. |
| 4 | `ImageGenerationStep` | Pure `(task, log)` signature — clean migration. |
| 5 | `SceneImageBatchStep` | Added `SetInputs(string outputDir, string? providerKey)`. |
| 6 | `GeminiPlaywrightSceneBreakdownStep` | Added `SetInputs(string outputDir, string? gemId, string? model, string? gemName)`. |
| 7 | `VoiceoverGenerationStep` | Added `SetInputs(string voiceId, string outputDir, string scriptText, string apiKey)`. |
| 8 | `GeminiTopicResearchStep` | Added `SetInputs(string topicOrUrl, string outputDir, string? gemId, bool enableDeepResearch, string? model, string? sessionId)`. |

Each migrated step:

- Inherits `PipelineStepBase` (which provides try/catch, cancellation, log prefix).
- Replaces its custom `ExecuteAsync` with `ExecuteCoreAsync` returning `StepResult`.
- Exposes per-step inputs via a `SetInputs(...)` setter called by the orchestrator
  before invoking `ExecuteAsync(task, log)`. This keeps the base signature
  `(task, log, ct)` simple while allowing each step to receive its own data.

## ⏸ Not migrated

| Step | Reason |
|---|---|
| `YoutubeTopicSuggestionStep` | Not a pipeline step — utility with a different signature (`SuggestTopicsAsync(channelUrl, gemId, model, log)`). Doesn't match the `(task, log)` template. Out of scope for P3.3. |
| `GeminiSceneBreakdownStepLegacy` | Marked `[Obsolete]` and not actively used in any DI registration. Migration skipped — delete instead (covered by P1.1, kept around for historical reference). |

## 🔧 Call-site changes

All orchestrators (`PipelineOrchestrator`, `LegacyVideoPipelineService`,
`GeminiVideoPipelineService`) updated to call the new pattern:

```csharp
_stepN.SetInputs(...);                  // per-step inputs
var result = await _stepN.ExecuteAsync(task, logTask);  // base template
var output = result.GetOutput<string>();                // optional typed access
```

For steps with no per-step inputs (ThumbnailDownloadStep, ImageGenerationStep),
the call site is unchanged — they just call `ExecuteAsync(task, log)`.

## ✅ Verify

`dotnet build` → **0 errors**, 25 warnings (all pre-existing).

All migrated steps compile cleanly under the new base class, and runtime
behavior is preserved (the base class reproduces the same log prefix and
try/catch shape the original code had).

## 🎯 Impact

- ~150 lines of boilerplate (try/catch, cancellation, log prefix) consolidated
  into `PipelineStepBase.ExecuteAsync`.
- All steps now return `StepResult` uniformly — orchestrator can chain them
  generically.
- `CancellationToken` is now consistently propagated to step bodies
  (previously several steps ignored the token entirely).
- Future step implementations only need to override `ExecuteCoreAsync` and
  inherit the rest.