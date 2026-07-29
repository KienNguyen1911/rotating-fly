# P3.2 — MVVM scaffolding for `MainWindow` (Profile tab)

> **Status:** 🟡 Partial (infra + `ProfileViewModel` ready, XAML not yet migrated)
> **Date:** 2026-07-29
> **Scope:** Introduce `RelayCommand` / `AsyncRelayCommand` infrastructure and
> a fully working `ProfileViewModel` for the Chrome-Profiles tab. The legacy
> code-behind in `Windows/MainWindow.Profiles.cs` is preserved untouched, so
> the build is green and runtime behavior is unchanged. The next turn will
> migrate the XAML bindings to the new VM.

---

## 🎯 Goal

`MainWindow.xaml.cs` is a **partial class with ~5,000 LOC across 7 files**.
Full MVVM migration is a multi-turn effort. This turn:

1. Adds the small MVVM primitives (`RelayCommand`, `AsyncRelayCommand`).
2. Implements one full `ProfileViewModel` (no UI dependencies — pure logic).
3. Documents the XAML migration recipe for the next turn and the other tabs.

The VM is **unused at runtime today** — calling code in `MainWindow.Profiles.cs`
still drives the UI. The build proves the VM compiles correctly and can be
wired into the tab without changes to existing services.

---

## 📦 New files

| File | Purpose |
|---|---|
| `Windows/Mvvm/RelayCommand.cs` | `RelayCommand` + `AsyncRelayCommand` (`ICommand` impls). Small, dependency-free. |
| `Windows/ViewModels/ProfileViewModel.cs` | Full VM for the Profiles tab: 7 commands, 4 bindable properties, 0 UI dependencies. |

---

## 🧩 `RelayCommand` / `AsyncRelayCommand`

```csharp
// Synchronous
public sealed class RelayCommand : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null);
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null);
    public void RaiseCanExecuteChanged();
}

// Asynchronous, re-entrancy guarded
public sealed class AsyncRelayCommand : ICommand
{
    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null, Action<Exception>? onError = null);
}
```

`AsyncRelayCommand` flips a busy flag for the lifetime of the awaited task so
WPF auto-disables the bound button while work is in flight — no manual
`IsEnabled = false / true` plumbing in every handler.

---

## 🧩 `ProfileViewModel`

Constructed with the existing services:

```csharp
new ProfileViewModel(IBrowserService browserService, Action<string> logCallback)
```

### Properties (bindable)

| Property | Type | Notes |
|---|---|---|
| `ProfileList` | `ObservableCollection<string>` | folder names |
| `NewProfileName` | `string` | bound to `TxtNewProfileName` |
| `SelectedProfile` | `string?` | bound to `LboxProfiles.SelectedItem` |
| `CustomGptUrl` | `string` | bound to `TxtCustomGptUrl` |
| `DefaultProfileName` | `string` | bound to `TxtDefaultProfileName.Text` |

### Commands

| Command | Type | Replaces handler |
|---|---|---|
| `RefreshProfilesCommand` | `RelayCommand` | `BtnRefreshProfiles_Click` |
| `OpenProfileFolderCommand` | `RelayCommand` | `BtnOpenProfileFolder_Click` |
| `OpenProfileBrowserCommand` | `AsyncRelayCommand` | `BtnOpenProfileBrowser_Click` |
| `CreateProfileCommand` | `AsyncRelayCommand` | `BtnCreateProfile_Click` |
| `DeleteProfileCommand` | `AsyncRelayCommand` | `BtnDeleteProfile_Click` |
| `SetDefaultProfileCommand` | `RelayCommand` | `BtnSetDefaultProfile_Click` |
| `SaveCustomGptUrlCommand` | `RelayCommand` | `BtnSaveCustomGptUrl_Click` |

The legacy `LboxProfiles_SelectionChanged` is replaced by binding
`SelectedItem="{Binding SelectedProfile}"` — the VM re-loads `CustomGptUrl`
and raises `CanExecuteChanged` on dependent commands whenever the selection
changes.

### Zero UI dependencies

The VM imports **only** `System.*`, `AssetAutomator.Core`,
`AssetAutomator.Services`, `AssetAutomator.Windows.Mvvm`, and
`Microsoft.Playwright` (for `IBrowserContext`). It contains **no `MessageBox`,
no `Window`, no `UserControl`** — making it unit-testable as soon as we add a
test project.

---

## 📂 Affected Files (summary)

| File | Change |
|---|---|
| `Windows/Mvvm/RelayCommand.cs` | **New** |
| `Windows/ViewModels/ProfileViewModel.cs` | **New** |
| `Windows/MainWindow.Profiles.cs` | **Untouched** — kept as fallback until XAML is migrated |
| `Windows/MainWindow.xaml` | **Untouched** — migration recipe below |

---

## ✅ Verify

- `dotnet build` → **0 errors**, 25 warnings (all pre-existing).
- `ProfileViewModel` compiles standalone — instantiated nowhere yet, so no
  runtime behavior change.

---

## 🔭 Next Turn — XAML migration recipe for the Profiles tab

Once `MainWindow` decides to consume the VM, the recipe is:

1. **Expose VM on MainWindow**:
   ```csharp
   public ProfileViewModel Profiles { get; } = new ProfileViewModel(
       App.Services.GetRequiredService<IBrowserService>(), Log);
   ```

2. **Scope the DataContext**: wrap the Profiles tab content in a `ContentControl`
   or `Grid` whose `DataContext` is `{Binding Profiles}` (the existing
   `DataContext = this` on the window stays for everything else).

3. **XAML edits** (8 elements):
   ```xml
   <!-- ListBox: drop x:Name + SelectionChanged, bind SelectedItem -->
   <ListBox ItemsSource="{Binding ProfileList}"
            SelectedItem="{Binding SelectedProfile, Mode=TwoWay}"
            ... />

   <!-- All buttons: drop x:Name + Click, bind Command -->
   <Button Content="🌐 Open Browser"
           Command="{Binding OpenProfileBrowserCommand}" ... />

   <!-- TextBoxes: drop x:Name, bind Text two-way -->
   <TextBox Text="{Binding NewProfileName, UpdateSourceTrigger=PropertyChanged}" ... />
   <TextBox Text="{Binding CustomGptUrl, UpdateSourceTrigger=PropertyChanged}" ... />
   <TextBlock Text="{Binding DefaultProfileName}" ... />
   ```

4. **Delete** `Windows/MainWindow.Profiles.cs`.

5. **Drop** the legacy `ProfileList` property on `MainWindow` (it's now in the
   VM) — but keep `LanguageList`, `HistoryDates`, `Tasks` (other tabs).

6. **Re-register** any references in `MainWindow.xaml.cs` (`ProfileList` →
   `Profiles.ProfileList`).

After Profiles is migrated, the same recipe applies to the other tabs
(`History`, `Tasks`, `GeminiCreator`, `BatchImageGen`, `AutomationSteps`),
each with its own VM under `Windows/ViewModels/`.

---

## ⚠️ Risks

- The `MainWindow` constructor currently uses `new BrowserService(Log)` (not DI).
  The VM receives `IBrowserService` directly, so a future DI migration of
  `BrowserService` will drop in without touching the VM.
- `ConfigService.Instance!` is still used inside the VM (singleton accessor).
  Migrating to `IConfigService` injection is a small follow-up — kept out of
  scope here to avoid coupling this turn to P3.1's unfinished work.