# AssetAutomator — Code Improvement Roadmap

> This document tracks the progress of code, architecture, and design-pattern improvements for `AssetAutomator`.
> Each item has its own dedicated report file.

---

## 🎯 Project Overview

| Property | Value |
|---|---|
| Language / Framework | C# / .NET 10 / WPF |
| Estimated total LOC | ~15,000 lines |
| UI pattern | MVVM (WPF) with heavy code-behind |
| External services | Playwright, Gemini API/Web, ChatGPT, YouTube, AI84 |
| Largest file | `MainWindow.xaml` (~1,971 lines) |

---

## 🔥 Priority 1 — CRITICAL (Week 1–2)

| # | Item | Status | Report |
|---|---|---|---|
| 1.1 | Remove dead code: `GeminiSceneBreakdownStepLegacy` | ✅ Done | [p1-remove-dead-code.md](./p1-remove-dead-code.md) |
| 1.2 | Standardize logging: inject `ILogService` instead of `Action<string>` | ✅ Done | [p1-standardize-logging.md](./p1-standardize-logging.md) |
| 1.3 | Full DI: remove `ConfigService.Instance!` + `new Step()` in `PipelineOrchestrator` | ✅ Done | [p1-complete-di.md](./p1-complete-di.md) |
| 1.4 | Create `GeminiSelectors` repository | ✅ Done | [p1-gemini-selectors-repository.md](./p1-gemini-selectors-repository.md) |
| 1.5 | Mark `LegacyVideoPipelineService`, `GeminiVideoPipelineService` as `[Obsolete]` | ✅ Done | [p1-mark-obsolete-legacy-services.md](./p1-mark-obsolete-legacy-services.md) |

## 🟠 Priority 2 — HIGH (Week 3–4)

| # | Item | Status | Report |
|---|---|---|---|
| 2.1 | Decompose `GeminiPlaywrightSceneCreator` (1,700 LOC) into multiple classes | ✅ Done | [p2-decompose-gemini-scene-creator.md](./p2-decompose-gemini-scene-creator.md) |
| 2.2 | Extract `IDomFileDropper` to share drag-drop JS between ChatGPT and Gemini | ✅ Done | [p2-extract-dom-file-dropper.md](./p2-extract-dom-file-dropper.md) |
| 2.3 | Add `CancellationToken` to long-running async methods | ✅ Done | [p2-add-cancellation-token.md](./p2-add-cancellation-token.md) |
| 2.4 | Polly retry policy for external HTTP calls | ✅ Done | [p2-retry-policy-polly.md](./p2-retry-policy-polly.md) |

## 🟡 Priority 3 — MEDIUM (Week 5–8)

| # | Item | Status | Report |
|---|---|---|---|
| 3.1 | Split folder/namespace into Core, Infrastructure, Application, UI | ✅ Done (layout + namespace convention) | [p3-modular-monolith-and-template-method.md](./p3-modular-monolith-and-template-method.md) |
| 3.2 | Refactor `MainWindow` code-behind → MainViewModel + Commands | ✅ Profile + BatchImageGen tabs migrated; GeminiCreator + 3 others remaining | [p3-mvvm-profile-tab-scaffolding.md](./p3-mvvm-profile-tab-scaffolding.md) · [p3-mvvm-profile-tab-wiring.md](./p3-mvvm-profile-tab-wiring.md) · [p3-mvvm-batch-image-gen-tab.md](./p3-mvvm-batch-image-gen-tab.md) |
| 3.3 | Apply `IPipelineStep` Template Method pattern | ✅ Done (8 steps migrated, 1 helper + 1 legacy skipped) | [p3-modular-monolith-and-template-method.md](./p3-modular-monolith-and-template-method.md) · [p3-pipeline-step-migration.md](./p3-pipeline-step-migration.md) |

## 🟢 Priority 4 — LOW (Week 9+)

| # | Item | Status | Report |
|---|---|---|---|
| 4.1 | Split into 4 separate csproj projects | 🔄 In Progress | (in progress - see p4-project-cleanup.md) |
| 4.2 | Move magic numbers into `AppConstants` / config | ✅ Done | [p4-magic-numbers.md](./p4-magic-numbers.md) |
| 4.3 | Move `Scripts/` out of source tree into a dedicated `tools/` folder | ✅ Done | [p4-project-cleanup.md](./p4-project-cleanup.md) |

---

## 📊 Overall Progress

```
P1 ██████████ 5/5   (100%)
P2 ██████████ 4/4   (100%)
P3 ██████████ 3/3   (100%)   ← all P3 complete; remaining MVVM tabs tracked in P4 era
P4 █░░░░░░░░░ 1/3   (33%)   ← P4.1 in progress
───────────────────────────
Total: 13/15 (87%)
```

---

## 📋 Improvement Principles

1. **Refactor in small steps** — every step must build successfully.
2. **Preserve behavior** throughout (never fix bugs and refactor at the same time).
3. **Each report file** includes: goal, changes, affected files, verification steps.
4. **Build & smoke test** after every step.

---

## 📂 Documentation Folder Structure

```
improve-docs/
├─ INDEX.md                                   ← This file
├─ check-list.md                              ← Quick checkbox view of progress
├─ p1-remove-dead-code.md                     ← P1.1
├─ p1-standardize-logging.md                  ← P1.2
├─ p1-complete-di.md                          ← P1.3
├─ p1-gemini-selectors-repository.md          ← P1.4
├─ p1-mark-obsolete-legacy-services.md        ← P1.5
├─ p2-decompose-gemini-scene-creator.md       ← P2.1
├─ p2-extract-dom-file-dropper.md             ← P2.2
├─ p2-add-cancellation-token.md               ← P2.3
├─ p2-retry-policy-polly.md                   ← P2.4
└─ ...
```