# WinUI 3 Port Roadmap — Sprint Breakdown

> Timeline and sprint breakdown for porting all WPF features to WinUI 3.
> See `README.md` for executive summary and `per-tab-gap-analysis.md` for detailed tasks.

## Capacity assumptions

- **Working hours per sprint**: 40h (1 week full-time)
- **Working hours per day**: ~8h
- **Review buffer**: 10% per sprint for code review, fixes, slack
- **Velocity estimate**: ~36 hours of feature work per sprint after buffer 

## Phasing strategy

```
Phase A (Core coverage)   ████████████ Sprint 1-2
Phase B (Batch Image Gen) ████████████████████████ Sprint 3-4
Phase C (Gemini Creator)  ████████████████████ Sprint 5-6
Phase D (Polish)          ████████████████████████ Sprint 7-8
```

After Phase A+B+C (~6 sprints), WinUI 3 reaches ~95% feature parity with WPF and can be used as the primary UI. Phase D is optional visual polish + OAuth.

---

## Sprint 1 (40h) — Phase A start — ✅ COMPLETED

| ID | Task | Tab | Effort | Status | Owner | Dependencies |
|---|---|---|---|---|---|---|
| A1 | Add 6 step checkboxes (UI + VM) | Tasks | 2h | ✅ Done | AI Assistant | — |
| A2 | Add filter bar UI (3 TextBox + 6 CheckBox + Clear) | Tasks | 4h | ✅ Done | AI Assistant | — |
| A3 | Add filter logic in TasksViewModel | Tasks | 3h | ✅ Done | AI Assistant | — |
| A4 | Expand Tasks DataGrid (Steps template + 4 action buttons) | Tasks | 8h | ✅ Done | AI Assistant | A1 |
| A5 | Add 6 metric cards (UI + 6 new VM properties) | Pool | 2h | ✅ Done | AI Assistant | — |
| A6 | Add Search TextBox + total count TextBlock (bind existing VM props) | Pool | 1h | ✅ Done | AI Assistant | — |
| A7 | Add 3 buttons (Open Folder, Set Default, Delete) | Profiles | 1.5h | ✅ Done | AI Assistant | — |
| A8 | Add Create Profile + Custom GPT URL section | Profiles | 2h | ✅ Done | AI Assistant | — |
| A9 | Add ProgressRing IsBusy | Profiles | 0.5h | ✅ Done | AI Assistant | — |
| A10 | Add 3 PasswordBoxes (AI84, Supabase, Image API Key) | Settings | 2h | ✅ Done | AI Assistant | — |
| A11 | Add 2 URL TextBoxes (Image API URL, Subtitle API URL) | Settings | 1h | ✅ Done | AI Assistant | — |
| A12 | Add Chrome Profiles dir picker | Settings | 1h | ✅ Done | AI Assistant | — |
| **Total** | | | **28h** | **100%** | | |

---

## Sprint 2 (40h) — Phase A finish — ✅ COMPLETED

| ID | Task | Tab | Effort | Status |
|---|---|---|---|---|
| A13 | Add proxies section (file path + manual + 3 buttons) | Settings | 2h | ✅ Done |
| A14 | Add 4 buttons (Export, Import, Check Requirements, Check AI84 Key) | Settings | 1.5h | ✅ Done |
| A15 | Add Theme selector (Light/Dark/System) | Settings | 1h | ✅ Done |
| A16 | Add ListBox dates (History) | History | 1.5h | ✅ Done |
| A17 | Add History filter bar (3 TextBox + 6 CheckBox + Clear) | History | 3h | ✅ Done |
| A18 | Add Step Status badges (5 step × 3 state) | History | 5h | ✅ Done |
| A19 | Add per-row action buttons (Logs, Assets) | History | 1h | ✅ Done |
| A20 | Expand Pool DataGrid (8 columns + 2 buttons) | Pool | 6h | ✅ Done |
| A21 | License dialog: add 3 buttons + status details | Dialog | 2h | ✅ Done |
| A22 | Update dialog: add 2 buttons + header titles | Dialog | 1.5h | ✅ Done |
| A23 | ScenesViewer dialog: add Stats + Apply All + Copy button wired | Dialog | 2h | ✅ Done |
| A24 | VoiceSelector dialog: add 9 controls (filters + pagination) | Dialog | 6h | ✅ Done |
| A25 | NewProject dialog: add Description TextBox | Dialog | 0.5h | ✅ Done |
| **Total** | | | **33h** | **100%** |

**Buffer:** 7h slack.

---

## Sprint 3 (40h) — Phase B start (Batch VM + dashboard) — ✅ COMPLETED

| ID | Task | Tab | Effort | Status | Owner |
|---|---|---|---|---|---|
| B1 | Create `BatchImageGenViewModel` (port from `MainWindow.BatchImageGen.cs`) | — | 6h | ✅ Done | AI Assistant |
| B2 | Create `BatchImageGenPage.xaml` + code-behind shell with NavigationView routing | — | 2h | ✅ Done | AI Assistant |
| B3 | Build Projects Dashboard panel (header card + storage path + Browse + Create) | Batch | 4h | ✅ Done | AI Assistant |
| B4 | Build Projects Cards Grid (ItemsControl + UniformGrid + DataTemplate) | Batch | 10h | ✅ Done | AI Assistant |
| **Total** | | | **22h** | **100%** | |

**Buffer:** 18h slack for the dashboard's complex card template.

---

## Sprint 4 (40h) — Phase B middle (Editor panel) — ✅ COMPLETED

| ID | Task | Tab | Effort | Status | Owner |
|---|---|---|---|---|---|
| B5 | Build Editor panel left sidebar (3 sections) — Character dropzone, Script JSON, Config | Batch | 16h | ✅ Done | AI Assistant |
| B6 | Build Engine/Model Expander with 2 RadioGroups + 3 ComboBox + 5+3 model RadioButtons | Batch | 12h | ✅ Done | AI Assistant |
| B7 | Add concurrency ComboBox + bottom Generate button | Batch | 2h | ✅ Done | AI Assistant |
| **Total** | | | **30h** | **100%** | |

**Buffer:** 10h slack.

---

## Sprint 5 (40h) — Phase B finish + Phase C start — ✅ COMPLETED

| ID | Task | Tab | Effort | Status | Owner |
|---|---|---|---|---|---|
| B8 | Build Card Grid view (ItemsControl + ItemsWrapGrid 360px + DataTemplate with overlay, aspect-ratio-driven height via `AspectRatioHeightConverter`) | Batch | 16h | ✅ Done | AI Assistant |
| B9 | Build Fallback Table View (ListView 9 columns) | Batch | 4h | ✅ Done | AI Assistant |
| B10 | Add Progress bar + status text + 4 header buttons (Save, Google Flow Web, View Toggle, Open Folder) | Batch | 2h | ✅ Done | AI Assistant |
| B11 | Port Converters (YoutubeUrlConverter, HexToBrushConverter, NodeStatusToBrushConverter, AspectRatioHeightConverter, StringToImageSourceConverter, StepStatusToBrushConverter, BoolToVisibilityConverter) | Global | 4.5h | ✅ Done | AI Assistant |
| **Total** | | | **26.5h** | **100%** | |

**Buffer:** 13.5h slack.

---

## Sprint 6 (40h) — Phase C (Gemini AI Creator) — ✅ COMPLETED

| ID | Task | Tab | Effort | Status | Owner |
|---|---|---|---|---|---|
| C1 | Add 6 toolbar buttons (Add, Delete, Suggest, Refresh Gems, Import Cookies, Run Selected) + VideoDuration NumberBox | Gemini | 4h | ✅ Done | AI Assistant |
| C2 | Build ListView queue with 7 columns (Select / Topic / Progress / Scriptwriter badge / Scene Creator badge / Status badge / Actions) using shared header Grid + DataTemplate row | Gemini | 12h | ✅ Done | AI Assistant |
| C3 | Build RowDetails Flyout with 4 inner panels (Scriptwriter gem+model+deep-research / Scene Creator gem+model / Voice+Provider+CharacterRef / Topic suggestion + Run/Save/Open/Delete actions) | Gemini | 16h | ✅ Done | AI Assistant |
| C4 | Bind VM properties (VideoDurationMinutes NumberBox, IsGenerating ProgressRing, ConsoleLogs ScrollViewer+TextBlock) | Gemini | 2h | ✅ Done | AI Assistant |
| C5 | Add Cancel (cancels CancellationTokenSource) + ClearLogs buttons | Gemini | 2h | ✅ Done | AI Assistant |
| **Total** | | | **36h** | **100%** | |

---

## Sprint 7 (40h) — Phase C finish + Phase D start — ✅ COMPLETED

| ID | Task | Tab | Effort | Status | Owner |
|---|---|---|---|---|---|
| C6 | Build Python Server Log panel (toggle button, status indicator, clear button, log textbox) | Gemini | 8h | ✅ Done | AI Assistant |
| C7 | Build Sidebar 5-Step Accordion (5 Expanders with status badges + per-step logs of the selected task) | Gemini | 12h | ✅ Done | AI Assistant |
| D1 | Add License + Update buttons in MainWindow header (with click handlers opening LicenseDialog / checking GitHub releases) | Shell | 2h | ✅ Done | AI Assistant |
| **Total** | | | **22h** | **100%** | |

**Buffer:** 18h slack.

**Sprint 7 notes**
- New converter `StepStatusToTextConverter` (NodeStatus → text label like "⏳ Đang chạy...") added in `Converters/StepStatusToTextConverter.cs`.
- `NodeStatusToBrushConverter` extended to handle the `Idle`/`Pending`/`Waiting` states (slate-600 #475569) so the 5-step accordion badges show the WPF-matching grey when a step has not started.
- `GeminiViewModel` now routes `LogCategory.PythonServer` log entries into the new `PythonServerLog` buffer (instead of the generic `ConsoleLogs` panel) and derives a `PythonServerStatus` indicator ("✅ Running" / "❌ Crashed" / "⚪ Unknown").
- Safe step-status / step-log accessors (`CurrentStep1Status` … `CurrentStep5Status`, `CurrentStep1Log` …) added so the accordion XAML does not need to dereference a possibly-null `SelectedTask`.
- MainWindow D1 buttons:
  - `BtnLicense` → opens the existing `LicenseDialog` (with `XamlRoot` set to the main window's root grid).
  - `BtnCheckUpdate` → fetches the latest GitHub release of `KienNguyen1911/AssetAutomator-Releases`; if the version differs from the running app's assembly version, shows `UpdateDialog`; otherwise shows a "Đã cập nhật" content dialog.

---

## Sprint 8 (40h) — Phase D finish — ✅ COMPLETED

| ID | Task | Tab | Effort | Status | Owner |
|---|---|---|---|---|---|
| D2 | Build Sidebar drawer logs (drag-handle, slide animation) | Shell | 12h | ✅ Done | AI Assistant |
| D3 | Port Style resources (Colors/Themes/Light/Dark) | Global | 12h | ✅ Done | AI Assistant |
| D4 | Port custom control templates (Buttons.xaml styles) | Global | 8h | ✅ Done | AI Assistant |
| **Total** | | | **32h** | **100%** | |

**Buffer:** 8h slack.

**Sprint 8 notes**

- **D3 — Style resources** (`Styles/Theme.xaml`, `Styles/Theme.Light.xaml`, `Styles/Theme.Dark.xaml`)
  - New `Theme.xaml` exposes stable semantic colour tokens (`Color.Canvas`, `Color.Ink`, `Color.Success`, …).
  - `Theme.Light.xaml` and `Theme.Dark.xaml` provide `SolidColorBrush` instances under keys like `Surface.DefaultBrush`, `Border.DefaultBrush`, `Action.PrimaryBrush`, etc. plus legacy aliases (`CanvasBrush`, `InkBrush`, `HairlineBrush`, …) for 1:1 parity with the WPF dictionary.
  - Registered via `App.xaml` `ResourceDictionary.ThemeDictionaries` so the framework automatically swaps dark/light when the active theme changes.

- **D4 — Button styles** (`Styles/ButtonStyles.xaml`)
  - `Button.Base` + implicit `Style TargetType="Button"` apply rounded 8 px corners, themed background/border/foreground and `SemiBold 12 px` typography to every Button.
  - Named variants: `PrimaryButton` (filled dark), `SecondaryButton` (subtle), `GreenButton`, `DeleteButton`, `IconButton` (28×28), `RoundCloseButton` (round red hover), `HeaderPillButton` (white pill used by the title bar).
  - MainWindow D1 + new D2 "Logs" toggle now use `HeaderPillButton` for visual consistency.

- **D2 — Sidebar drawer** (`ViewModels/SidebarViewModel.cs`, `Controls/ResizableDragHandle.cs`, `MainWindow.xaml/.cs`)
  - `SidebarViewModel` is a singleton (`App.Services`) with `IsOpen`, `Title`, `Logs`, `DrawerWidth` and helper methods `Show(title, initialLogs)`, `AppendLog(line)`, `AppendLogs(lines)`, `Clear()`, `ToggleCommand`, `CloseCommand`.
  - New sidebar overlay (`Grid Row 1`) slides in from the right via `TranslateTransform` + `Storyboard` (220 ms `CubicEase.EaseOut` open, 180 ms `EaseIn` close).
  - `ResizableDragHandle` (subclassed from `Grid` because `Border` is sealed) sets the West-East resize cursor via the protected `UIElement.ProtectedCursor` and exposes `ResetCursor()`.
  - Drag handle clamps width between `SidebarViewModel.MinWidth` (320 px) and `MaxWidthRatio` (80 % of window width), persisting the result back to `Sidebar.DrawerWidth` so the `x:Bind` keeps the drawer's `Width` in sync.
  - New "Logs" header pill (`BtnToggleSidebar`) opens the drawer; in-page VM access to `SidebarViewModel` allows any page to push logs via `Show`/`AppendLog`.
  - Round ✕ close button uses the new `RoundCloseButton` style.

**Build & smoke-test result:** Build succeeded with 0 errors via MSBuild.exe (`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`). Smoke test confirms the app launches, MainWindow handle is non-zero (window visible) and no stderr.

---

## Sprint 9 (optional, 40h) — Phase D finish + OAuth

| ID | Task | Tab | Effort |
|---|---|---|---|
| D5 | Real WebView2 OAuth flow (WebViewLoginDialog) | Dialog | 16h |
| D6 | Card grid hover overlay animation | Batch | 4h |
| D7 | Polish + bug fixes + documentation | — | 20h |

---

## Velocity chart

```
Hours per sprint (planned)
40 ┤█████████████████████████████████████
   │
36 ┤██████████████████████████
   │
30 ┤████████████████████
   │
22 ┤██████████████
   │
 0 ┤────────────────────────────────────────
   S1  S2  S3  S4  S5  S6  S7  S8  S9
```

Burn-down shows we can realistically finish at Sprint 7 (full parity with WPF for all 6 tabs), then use Sprint 8 for polish.

---

## Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Batch Image Gen card template is too complex (aspect-ratio-driven height + hover overlay) | High | High | Prototype in isolation first; consider using simpler card layout if blocked |
| Gemini RowDetails popup is hard in WinUI 3 (no native RowDetails like WPF) | High | Medium | Use Flyout attached to row instead |
| PasswordBox in WinUI 3 doesn't support placeholder by default | Low | Low | Add a TextBlock overlay or use AppBarButton with PasswordRevealMode=Visible |
| FolderPicker in WinUI 3 requires window handle (HWND) | Low | Low | Use WinRT.Interop.InitializeWithWindow helper |
| RichTextBox not available in WinUI 3 | Medium | Medium | Use ScrollViewer + TextBox or TextBlock; lose rich text formatting |
| WebView2 element causes XamlCompiler.exe silent fail (already encountered) | Low | High | Already worked around with Border placeholder; revisit in Phase D |
| Dependency property / converter portability (WPF MultiBinding, etc.) | High | Medium | Replace MultiBinding with custom attached properties or value converters |

---

## Definition of Done per sprint

- [ ] All committed code compiles (no warnings)
- [ ] Each new control has a unique `x:Name` and is bound to a ViewModel property/command
- [ ] Each new button has a wired handler that calls into a service or VM command
- [ ] Light + Dark theme both render correctly
- [ ] NavigationView still navigates correctly
- [ ] No regression in existing pages
- [ ] Update `winui3-port/README.md` "Status" column
- [ ] Update `HOW-TO-RUN.md` if new dependencies or commands added

---

## Confirmed decisions (Aug 1, 2026)

- ✅ **Scope**: Phase A + B + C (full parity, ~6 sprints). Phase D deferred.
- ✅ **WPF fallback**: Keep `AssetAutomator.UI` project as fallback throughout all sprints. Delete only after WinUI 3 reaches production-ready state.
- ✅ **Gemini AI Creator**: Replace the current simplified single-script `GeminiPage` entirely with the WPF multi-task queue UI. Do not keep both modes.

## Open questions

1. ~~Should we keep WPF project as fallback during port, or delete it after Phase C completes?~~ → Keep as fallback (confirmed).
2. ~~Should we wire WebView2 in Phase D, or just leave Border placeholder indefinitely?~~ → Deferred (Phase D not in scope).
3. Do we need to support offline mode for any of the OAuth-related flows? (Deferred — needs investigation)
4. ~~Should the Gemini Creator page support the simpler single-script mode that WinUI 3 currently has, or replace it entirely with the multi-task queue?~~ → Replace entirely with multi-task queue (confirmed).