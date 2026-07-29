# P3.2 — Migrate XAML Profile tab to MVVM

> **Status:** ✅ Done
> **Date:** 2026-07-29
> **Scope:** Wire up `ProfileViewModel` (created in previous turn) into
> `MainWindow.xaml`, replace all `Click=` / `x:Name` handlers with `Command=`
> / bindings, delete `Windows/MainWindow.Profiles.cs`.

---

## 🎯 Goal

Eliminate the 9 code-behind handlers in `Windows/MainWindow.Profiles.cs`
(~12.6 KB, ~310 LOC) by binding the Chrome-Profiles tab to
`ProfileViewModel`.

## 📦 Changes

### `Windows/MainWindow.xaml`
- Wrapped the Profiles tab content in
  `<Grid DataContext="{Binding Profiles}">`.
- Replaced every `x:Name=...` reference with a binding (no names → no
  name-resolution, no maintenance drag).
- Replaced every `Click="..."` handler with `Command="{Binding ...Command}"`.
- Replaced `{Binding ElementName=LboxProfiles, Path=SelectedItem}` with
  `{Binding SelectedProfile}` (now resolves via the VM's DataContext).
- Updated the Tasks-tab ComboBox from
  `ItemsSource="{Binding ProfileList, ElementName=RootWindow}"` to
  `ItemsSource="{Binding Profiles.ProfileList, RelativeSource=... AncestorType=Window}"`.

### `Windows/Profile*.cs` → completely removed
- Deleted `Windows/MainWindow.Profiles.cs`.

### `Windows/MainWindow.xaml.cs`
- Added `public ProfileViewModel Profiles { get; }` property.
- Added a second constructor `MainWindow(BrowserService browserService, IBrowserService browserServiceForVmInjection)`
  for DI; the parameterless one delegates to it with sensible defaults so
  the legacy `new MainWindow()` call sites still work.
- Removed the obsolete `public ObservableCollection<string> ProfileList` property.
- Replaced four `LoadProfiles()` call sites with `Profiles.RefreshProfiles()`.

### `Windows/MainWindow.Tasks.cs`
- Replaced references to the old `ProfileList` property with
  `Profiles.ProfileList` (two call sites in the task-default logic).

### `Windows/MainWindow.GeminiCreator.cs`
- Replaced one `LoadProfiles()` call site with `Profiles.RefreshProfiles()`.

---

## 📂 Affected Files

| File | Change |
|---|---|
| `Windows/MainWindow.xaml` | Bindings migration (~11 elements) |
| `Windows/MainWindow.xaml.cs` | Added `Profiles` property + DI ctor |
| `Windows/MainWindow.Tasks.cs` | 2× `ProfileList` → `Profiles.ProfileList` |
| `Windows/MainWindow.GeminiCreator.cs` | 1× `LoadProfiles()` → `Profiles.RefreshProfiles()` |
| `Windows/MainWindow.Profiles.cs` | **Deleted** |

---

## ✅ Verify

- `dotnet build` → **0 errors**, 25 warnings (all pre-existing, no new ones).
- Runtime behavior unchanged:
  - `Profiles.RefreshProfiles()` is called in the ctor (was `LoadProfiles()`).
  - `OpenProfileBrowserCommand`, `CreateProfileCommand`, etc., reproduce
    the exact handler logic (verified by reading each command in
    `ProfileViewModel.cs`).
  - `SelectedProfile` TwoWay binding replaces the manual
    `LboxProfiles_SelectionChanged` handler, and the VM's setter triggers
    `LoadCustomGptUrlForSelection()` automatically.

---

## 📊 Impact

- **~310 LOC code-behind deleted**.
- Tab is now testable: `ProfileViewModel` accepts injected services and
  exposes observable state; unit tests can drive commands without spinning
  up a WPF window.
- All other 6 tabs still on legacy code-behind; recipe in this file
  (wrap content in `DataContext=...` grid, swap `Click=` → `Command=`,
  drop `x:Name=`) applies to each.

---

## 🔭 Next Steps

The recipe above generalizes to the other tabs. Order of priority by complexity
(lowest first, building confidence in the pattern):

1. **History tab** (`MainWindow.History.cs`, ~80 LOC, 4 handlers) — smallest.
2. **Settings tab** (pure configuration persistence — straightforward).
3. **Automation Tasks tab** (the largest — multiple sub-flows).
4. **Batch Image Gen tab** (UI flow + service integration).
5. **Gemini AI Creator tab** (multi-step orchestration UI).

After the remaining 5 tabs are migrated, `MainWindow.xaml.cs` will shrink
significantly and any unit-test project (added during P4.1) can target the
view-models directly.