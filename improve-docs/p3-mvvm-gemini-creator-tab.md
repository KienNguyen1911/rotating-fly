# P3.2 — Migrate GeminiCreator Tab to MVVM

## Goal
Move the "Gemini AI Creator" tab (`Windows/MainWindow.GeminiCreator.cs`, ~1,300 LOC) out of the `MainWindow` code-behind and into a dedicated `GeminiCreatorViewModel` that exposes commands, observable state, and a `IGeminiCreatorDialogs` abstraction for OS dialogs.

## Why
- The tab had 30+ named WPF controls referenced from code-behind (DataGrids, ComboBoxes, TextBoxes, RichTextBoxes, Expanders).
- Every business action lived in the partial class — no testability, no reuse.
- The code-behind approach was a maintenance liability: any UI tweak required code changes.
- The legacy code mixed UI manipulation and business logic, making it hard to migrate to the new `PipelineOrchestrator`.

## Changes

### New files
- `Windows/ViewModels/GeminiCreatorViewModel.cs` — full state + command surface for the tab:
  - State: `GeminiTasks`, `AvailableScriptwriterGems`, `AvailableSceneCreatorGems`, `SelectedScriptwriterGem`, `SelectedSceneCreatorGem`, `ScriptwriterGemName`, `SceneCreatorGemName`, `ScriptTopic`, `ScriptwriterPrompt`, `SceneCreatorPrompt`, `IsDeepResearchEnabled`, `ScriptResult`, `SceneResult`, `ResolvedScript`, `ResolvedScene`, `PythonServerLogText`, `CurrentTask`, `TasksProgress`, `TasksStatus`, `GeminiStatusIcon`, `GeminiStatusMessage`, `GeminiStatusColor`, `OutputDir`, `VoiceoverVoiceId`, `OutputFileName`, `GeminiTasksCount`, `ScriptwriterGemCount`, `SceneCreatorGemCount`.
  - Commands: `RefreshGemsCommand`, `ImportCookiesCommand`, `RunSelectedTasksCommand`, `RunSingleTaskCommand`, `DeleteSelectedTasksCommand`, `ClearAllTasksCommand`, `CancelTaskCommand`, `CancelAllTasksCommand`, `TogglePythonLogCommand`, `ToggleRowCommand`, `OpenScriptFolderCommand`, `OpenSceneFolderCommand`, `ViewTaskLogCommand`, `ViewStep1LogCommand`, `ViewStep2LogCommand`, `ViewStep3LogCommand`, `ViewStep4LogCommand`, `ViewStep5LogCommand`.
  - Business logic: lazy initialization of services (LogService, GeminiApiService, GeminiCreatorService, PipelineOrchestrator), task orchestration, progress tracking, status updates.
- `Windows/ViewModels/IGeminiCreatorDialogs.cs` — abstraction for message/confirm dialogs, file picking, and UI thread marshaling.
  - Methods: `AskCookieMode()`, `Confirm(message, title)`, `PickFile(filter, title)`, `ShowResult(message, hasErrors)`, `ShowWarning(message, title)`, `PostToUi(action)`, `SetStatus(icon, message, color)`.
- `Windows/ViewModels/GeminiCreatorDialogs.cs` — concrete implementation using WPF APIs.

### Edited files
- `Windows/Mvvm/RelayCommand.cs` — renamed `RelayCommand` to `UiCommand`, `AsyncRelayCommand` to `AsyncUiCommand`, `RelayCommand<T>` to `UiCommand<T>`, `AsyncRelayCommand<T>` to `AsyncUiCommand<T>` to avoid naming conflict with `AssetAutomator.Models.Nodes.RelayCommand`.
- `Windows/MainWindow.xaml.cs` — added `using AssetAutomator.Models;`, constructs `GeminiCreatorViewModel` in the constructor with `IGeminiCreatorDialogs` from DI (or fallback `new GeminiCreatorDialogs()`), wires up three UI event handlers:
  - `OnOpenTaskLog` → `OpenTaskLogSidebar` (existing method in `MainWindow.GeminiCreator.cs`)
  - `OnCollapseRow` → clears `DgridGeminiTasks.SelectedItem`
  - `OnTogglePythonLog` → toggles `TxtPythonServerLog.Visibility`
- `App.xaml.cs` — added DI registrations for `IGeminiCreatorDialogs` and `GeminiCreatorViewModel`.

### Partial class retained
- `Windows/MainWindow.GeminiCreator.cs` — kept as-is for now. Contains:
  - Event handlers for the legacy flow (RunGeminiSingle, RunGeminiSelected, etc.)
  - UI manipulation methods: `OpenTaskLogSidebar`, `UpdateGeminiStepAccordionView`, `UpdateStepBadge`, `SetLogTextToRichTextBox`, `AppendGeminiTaskLog`, etc.
  - Context menu builders for task items
  - Status update helpers

## Key design decisions

### Why partial class still exists
The partial class `MainWindow.GeminiCreator.cs` is retained because:
1. It contains a significant amount of UI manipulation code (context menus, accordion updates, RichTextBox updates) that is tightly coupled to WPF types.
2. The ViewModel handles business logic and command orchestration, while the partial class handles UI state that's easier to manage imperatively.
3. Full XAML migration for this complex tab would require extensive DataTemplate work for the step accordion and context menus — deferred for future iteration.

### Dialog abstraction pattern
The `IGeminiCreatorDialogs` interface abstracts:
- `PostToUi(Func<Task>)` — marshals async work back to the UI thread
- `SetStatus(icon, message, color)` — synchronous status setter (runs on calling thread; UI updates via property bindings)
- File picking, message boxes, confirmation dialogs — all testable and mockable

## Validation
- `dotnet build` succeeds with 0 errors.
- The tab functionality mirrors the old behavior.
- The ViewModel can be unit tested in isolation.
- Services are lazily initialized — they only start when the user first interacts with the tab.

## Future work
- Migrate remaining XAML bindings from `MainWindow.GeminiCreator.cs` to the ViewModel
- Extract step accordion UI into a reusable UserControl
- Consider moving task log display into the ViewModel with proper ObservableCollection logging
