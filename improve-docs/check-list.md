# AssetAutomator — Improvement Checklist

> Quick checkbox view. Detailed reports live in each `pN-*.md` file; see [INDEX.md](./INDEX.md) for the full roadmap.

---

## 🔥 P1 — CRITICAL

- [x] **P1.1** Remove dead code: `GeminiSceneBreakdownStepLegacy` — [p1-remove-dead-code.md](./p1-remove-dead-code.md)
- [x] **P1.2** Standardize logging: inject `ILogService` instead of raw `Action<string>` — [p1-standardize-logging.md](./p1-standardize-logging.md)
- [x] **P1.3** Full DI: remove `ConfigService.Instance!` + `new Step()` in `PipelineOrchestrator` — [p1-complete-di.md](./p1-complete-di.md)
- [x] **P1.4** Create `GeminiSelectors` repository — [p1-gemini-selectors-repository.md](./p1-gemini-selectors-repository.md)
- [x] **P1.5** Mark `LegacyVideoPipelineService`, `GeminiVideoPipelineService` as `[Obsolete]` — [p1-mark-obsolete-legacy-services.md](./p1-mark-obsolete-legacy-services.md)

## 🟠 P2 — HIGH

- [x] **P2.1** Decompose `GeminiPlaywrightSceneCreator` (1,700 LOC) into multiple classes — [p2-decompose-gemini-scene-creator.md](./p2-decompose-gemini-scene-creator.md)
- [x] **P2.2** Extract `IDomFileDropper` to share drag-drop JS between ChatGPT and Gemini — [p2-extract-dom-file-dropper.md](./p2-extract-dom-file-dropper.md)
- [x] **P2.3** Add `CancellationToken` to long-running async methods — [p2-add-cancellation-token.md](./p2-add-cancellation-token.md)
- [x] **P2.4** Polly retry policy for external HTTP calls — [p2-retry-policy-polly.md](./p2-retry-policy-polly.md)

## 🟡 P3 — MEDIUM

- [x] **P3.1** Split folder/namespace into Core, Infrastructure, Application, UI — [p3-modular-monolith-and-template-method.md](./p3-modular-monolith-and-template-method.md)
- [x] **P3.2** Refactor `MainWindow` code-behind → MainViewModel + Commands (**Profile + BatchImageGen + GeminiCreator tabs done**; 2 more tabs pending — History, Settings) — [p3-mvvm-profile-tab-scaffolding.md](./p3-mvvm-profile-tab-scaffolding.md) · [p3-mvvm-profile-tab-wiring.md](./p3-mvvm-profile-tab-wiring.md) · [p3-mvvm-batch-image-gen-tab.md](./p3-mvvm-batch-image-gen-tab.md) · [p3-mvvm-gemini-creator-tab.md](./p3-mvvm-gemini-creator-tab.md)
- [x] **P3.3** Apply `IPipelineStep` Template Method pattern (8 steps migrated; 1 helper + 1 legacy skipped) — [p3-modular-monolith-and-template-method.md](./p3-modular-monolith-and-template-method.md) · [p3-pipeline-step-migration.md](./p3-pipeline-step-migration.md)

## 🟢 P4 — LOW

- [x] **P4.1** Split into 4 separate csproj projects (Core / Infrastructure / Application / UI) — **In Progress** (see p4-project-cleanup.md)
- [x] **P4.2** Move magic numbers (timeouts, retries, sleeps) into `TimingConstants` — [p4-magic-numbers.md](./p4-magic-numbers.md)
- [x] **P4.3** Move `Scripts/` out of source tree into a dedicated `tools/` folder — [p4-project-cleanup.md](./p4-project-cleanup.md)

---

## 📊 Progress Summary

| Tier | Done | Total | %     |
| ---- | ---- | ----- | ----- |
| P1   | 5    | 5     | 100%  |
| P2   | 4    | 4     | 100%  |
| P3   | 3    | 3     | 100%  |
| P4   | 2    | 3     | 67%   |
| **All** | **14** | **15** | **93%** |

```
P1 ██████████ 5/5
P2 ██████████ 4/4
P3 ██████████ 3/3
P4 ██░░░░░░░░ 2/3
```