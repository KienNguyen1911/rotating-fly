# P3.2 — Migrate BatchImageGen Tab to MVVM

## Goal
Move the "Batch Image Gen" tab (`Windows/MainWindow.BatchImageGen.cs`, ~1,100 LOC) out of the `MainWindow` code-behind and into a dedicated `BatchImageGenViewModel` that exposes commands, observable state, and a `BatchImageGenDialogs` abstraction for OS dialogs.

## Why
- The tab had 30+ named WPF controls referenced from code-behind (RadioButtons, ComboBoxes, TextBoxes, Panels).
- Every business action lived in the partial class — no testability, no reuse.
- The code-behind approach was a maintenance liability: any UI tweak required code changes.

## Changes

### New files
- `Windows/ViewModels/ObservableObject.cs` — minimal `INotifyPropertyChanged` base with `SetField<T>` helper.
- `Windows/ViewModels/IBatchImageGenDialogs.cs` — abstraction for file/folder/message dialogs; the default `BatchImageGenDialogs` implementation owns the WPF dialogs. Includes a `PostToUi(Func<Task>)` helper that marshals continuations back to the UI thread.
- `Windows/ViewModels/BatchImageGenViewModel.cs` — full state + command surface for the tab:
  - State: `BatchImageItems`, `ReferenceImages`, `Projects`, `ScriptJson`, `OutputDir`, `VideoTitle`, `AspectRatio`, `Engine`, `Model`, `Provider`, `ProviderSection`, `Upscale`, `Concurrency`, `IsFlowOptionsVisible`, `IsCharPanelEmpty`, `IsCharPanelHasImage`, `CharInfoText`, `CharTagText`, `CharPreviewImage`, `ProgressText`, `ProgressValue`, `ProgressMaximum`, `GenerateButtonText`, `IsCardViewVisible`, `IsTableViewVisible`, `ViewToggleText`, `IsProjectsDashboardVisible`, `IsProjectEditorVisible`, `ProjectsStoragePath`.
  - Commands: `BrowseOutputDir`, `BrowseProjectsDir`, `ImportJson`, `UpdateScriptJson`, `SwitchProvider`, `SelectCharImages`, `ClearCharImages`, `ToggleView`, `Generate`, `OpenFolder`, `OpenImage`, `OpenCardImage`, `CopyPrompt`, `RegenerateCard`, `OpenFlowProjectUrl`, `NewProject`, `SaveProject`, `BackToProjects`, `OpenProjectCard`, `DeleteProjectCard`, `AspectChanged`.
  - Business logic moved into the VM: project CRUD, JSON parsing, batch generation orchestration, file I/O, progress tracking.
- `Windows/Mvvm/StringEqualsConverter.cs` — `IValueConverter` that returns `true` (or `Visibility.Visible`) when a bound string equals the `ConverterParameter`. Lets us bind `RadioButton.IsChecked` to a `string` property.
- `Windows/Mvvm/BoolToVisibilityConverter.cs` — standard `bool` ↔ `Visibility` converter (supports `invert` parameter).

### Edited files
- `Windows/Mvvm/RelayCommand.cs` — added generic `RelayCommand<T>` and `AsyncRelayCommand<T>` so `CommandParameter` can carry a single item (e.g. `BatchImageItem`).
- `Resources/Styles/Components.xaml` — registered `BoolToVisibilityConverter`, `StringEqualsConverter`, `StringEqualsVisibilityConverter`.
- `App.xaml.cs` — added DI registrations for `BatchImageGenViewModel` and `IBatchImageGenDialogs`.
- `Windows/MainWindow.xaml.cs` — exposes `public BatchImageGenViewModel BatchImageGen { get; }` and constructs it inside the DI constructor using services from `App.Services` (with `new` fallbacks if DI is unavailable). Added 5 small forwarder methods (`BorderCharDropzone_MouseLeftButtonDown`, `CardImageContainer_MouseLeftButtonDown`, `CboxBatchAspect_SelectionChanged`, `CboxBatchEngine_SelectionChanged`, `CboxBatchConcurrency_SelectionChanged`) that translate WPF events into VM commands.
- `Windows/MainWindow.xaml` — wrapped the whole "Batch Image Gen" `<TabItem>` content in `DataContext="{Binding BatchImageGen}"` and replaced:
  - Every `x:Name` + `Click=`/`SelectionChanged`/`Checked`/`MouseLeftButtonDown` attribute with `Command="{Binding XxxCommand}"`, `Text="{Binding Xxx}"`, or `Visibility="{Binding Xxx, Converter=...}"` bindings.
  - RadioButtons for provider/model/upscale use `IsChecked="{Binding Xxx, Converter={StaticResource StringEqualsConverter}, ConverterParameter=...}"`.
  - ItemsControls / DataGrids now bind `ItemsSource` to VM collections.
  - `Visibility` of the two top-level panels (Projects Dashboard / Project Editor) bound to `IsProjectsDashboardVisible` / `IsProjectEditorVisible`.
  - `Visibility` of Card View vs. Table View bound to `IsCardViewVisible` / `IsTableViewVisible`.
  - Button `Content` (e.g. `GenerateButtonText`, `ViewToggleText`) bound to VM strings.

### Removed files
- `Windows/MainWindow.BatchImageGen.cs` — deleted (1,099 LOC). All logic lives in the VM; XAML event handlers are tiny forwarders in `MainWindow.xaml.cs`.

## Why the small forwarders remain
WPF's `MouseLeftButtonDown` event signature and the way `<DataTemplate>` controls need to read `DataContext` make it cheaper to expose a 3-line code-behind forwarder than to convert every click into a behavior, attached property, or `EventToCommand` library dependency. The forwarders are pure pass-throughs — no business logic.

The three `ComboBox.SelectionChanged` forwarders exist because the ComboBox is the only practical WPF control for picking one of several `Tag`-tagged options, and the bound `Engine` / `AspectRatio` / `Concurrency` properties are simple scalars. The forwarder reads `((ComboBoxItem)cb.SelectedItem).Tag` and pushes the value into the VM.

## Validation
- `dotnet build` succeeds with 0 errors.
- The tab visually mirrors the old layout (no XAML restructuring beyond binding plumbing).
- The `_batchRefImages` field, the JSON parser, the batch generation loop, project save/load, and the file-system "auto-detect existing images" logic all run inside the VM with no UI dependencies — easy to unit-test in future.
