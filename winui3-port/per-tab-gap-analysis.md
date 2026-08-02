# Per-tab Gap Analysis — WPF vs WinUI 3

> Detailed analysis for each tab showing what's missing in WinUI 3 port.
> See `wpf-inventory.md` for the WPF baseline and `README.md` for the executive summary.

---

## Tab 1: Automation Tasks — Gap Analysis

### Currently in WinUI 3 (`Views/Pages/TasksPage.xaml`)

```xaml
<ScrollViewer Padding="24">
  <StackPanel Spacing="12">
    <TextBlock Text="Automation Tasks" Style="{ThemeResource TitleTextBlockStyle}"/>
    <TextBlock Text="Quản lý hàng đợi..." Style="{ThemeResource BodyTextBlockStyle}" Opacity="0.7"/>

    <CommandBar DefaultLabelPosition="Right">
      <AppBarButton Icon="Play" Label="Chạy Task" Command="{Binding StartTaskCommand}"/>
      <AppBarButton Icon="Pause" Label="Tạm Dừng" Command="{Binding PauseTaskCommand}"/>
      <AppBarButton Icon="Stop" Label="Dừng Task" Command="{Binding StopTaskCommand}"/>
      <AppBarSeparator/>
      <AppBarButton Icon="Refresh" Label="Làm mới" Command="{Binding RefreshTasksCommand}"/>
    </CommandBar>

    <Grid ColumnSpacing="12"> <!-- 4 metric cards -->...</Grid>

    <InfoBar IsOpen="True" Severity="Informational" Title="Trạng thái:" Message="{Binding StatusMessage}"/>

    <Border> <!-- task list card -->
      <ListView ItemsSource="{Binding Tasks}" SelectedItem="{Binding SelectedTask, Mode=TwoWay}" SelectionMode="Single">
        <ListView.ItemTemplate>...</ListView.ItemTemplate>
      </ListView>
    </Border>
  </StackPanel>
</ScrollViewer>
```

**Total: ~20 controls**

### Missing in WinUI 3 (must add)

#### Group A — Top toolbar (different buttons)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| 3 action buttons (Tạo Task / Tạo Nhiều / Chạy Task Đã Chọn) | `BtnAddTask`, `BtnAddBulkTasks`, `BtnRun` | Replace or augment the current CommandBar with these buttons. **The current Play/Pause/Stop don't map to WPF semantics** — WPF's "Chạy Task Đã Chọn" is closer to a "Run Selected" action. Recommended: keep both sets and put Tạo/Tạo Nhiều/Chạy Task Đã Chọn as primary AppBarButtons. |

#### Group B — Step selector (6 checkboxes)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `ChkStepDownloadThumbnail` | step 1 (thumbnail download) | Add as 6 CheckBox controls in an Expander or WrapPanel inside the page. Bind to `TasksViewModel.StepDownloadThumbnail`, etc. (new properties). |
| `ChkStepGenerateThumbnail` | step 2 | " |
| `ChkStepGetTranscript` | step 3 | " |
| `ChkStepRewrittenTranscript` | step 4 | " |
| `ChkStepVoiceover` | step 5 | " |
| `ChkStepSrt` | step 6 | " |

#### Group C — Filter bar (textboxes + checkboxes)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `TxtFilterVideoUrl` (130px) | filter by video URL | Add `TextBox` with `Text="{Binding FilterVideoUrl, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"` |
| `TxtFilterLanguage` (85px) | filter by language | " |
| `TxtFilterVoiceId` (85px) | filter by voice ID | " |
| `ChkFilterT`..`ChkFilterG` (6 checkboxes) | filter by failure step | Add 6 CheckBox bound to `FilterStepTFail`, etc. |
| `BtnClearFilters` | clear all filters | Add `Button Command="{Binding ClearFiltersCommand}"` |

#### Group D — DataGrid expansion
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `ChkSelectAllTasks` (header checkbox) | select-all column | Replace ListView with DataGrid (WinUI 3: CommunityToolkit DataGrid or build with ListView + GridView-style headers). Add select-all column. |
| Per-row Action buttons (4): Run / View Log / View Assets / Delete | `BtnRunSingleTask_Click`, `BtnViewTaskLog_Click`, `BtnViewTaskAssets_Click`, `BtnDeleteTask_Click` | Add 4 buttons in ItemTemplate using StackPanel. Wire to Commands on the row ViewModel or to RelayCommand on the Task item. |

#### Group E — Sidebar logs (optional, can defer)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `SidebarLogs` (drawer) | right-side overlay drawer | Phase D (deferred). For now, use a simpler toggle. |
| `TxtSidebarLog` RichTextBox | rich log view | Phase D. |
| Drag handle resizing | `SidebarDragHandle_MouseLeftButtonDown/Move/Up` | Phase D. |

### Migration effort estimate
- Group A (toolbar fix): 2 hours
- Group B (step selector): 2 hours (UI only; binding requires VM update)
- Group C (filter bar): 4 hours (UI + 3 TextBox + 6 CheckBox + 1 Clear button + filter logic in VM)
- Group D (DataGrid expansion): 8 hours (significant — custom column templates)
- Group E (sidebar): deferred to Phase D

**Total: ~16 hours**

---

## Tab 2: Image Pool — Gap Analysis

### Currently in WinUI 3 (`Views/Pages/PoolPage.xaml`)
- Header (title + description)
- CommandBar: Refresh / Thêm Ảnh / Xóa Ảnh (3 buttons)
- InfoBar
- ListView 4-col (Prompt / StyleName / FilePath / Status)

**Total: ~10 controls**

### Missing in WinUI 3

#### Group A — 6 metric cards
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `TxtPoolRunningWorkers` | Running workers count | Add 6 TextBlock in a Grid, bound to `PoolViewModel.RunningWorkers`, `MaxWorkers`, `WaitingRequests`, `ProcessingRequests`, `FinishedRequests`, `AvgTime` (new properties to add) |
| `TxtPoolMaxWorkers` | Max workers | " |
| `TxtPoolWaitingRequests` | Waiting requests | " |
| `TxtPoolProcessingRequests` | Processing requests | " |
| `TxtPoolFinishedRequests` | Finished requests | " |
| `TxtPoolAvgTime` | Average time | " |

#### Group B — DataGrid expansion
| Missing | WPF equivalent | Action needed |
|---|---|---|
| ID column | `DgridPoolRequests` column 1 | Add to ListView ItemTemplate |
| Status badge (with semantic brush) | column 3 | Use Border with `{Binding Status, Converter=...}` |
| Engine column | column 4 | " |
| Progress bar | column 5 | Use ProgressBar control in template |
| Started time | column 6 | " |
| Finished time | column 7 | " |
| Per-row action buttons (Open Image / Cancel) | column 8 | Add 2 buttons |

#### Group C — Search box + total count
| Missing | VM property | Action needed |
|---|---|---|
| Search TextBox | `PoolViewModel.SearchQuery` (exposed but not bound) | Add `TextBox Text="{Binding SearchQuery, UpdateSourceTrigger=PropertyChanged}"` |
| Total count badge | `PoolViewModel.TotalImagesCount` (exposed but not bound) | Add `TextBlock Text="{Binding TotalImagesCount}"` next to header |

### Migration effort estimate
- Group A (metric cards): 2 hours (UI + 6 new properties)
- Group B (DataGrid): 6 hours
- Group C (search + total): 1 hour

**Total: ~9 hours**

---

## Tab 3: Chrome Profiles — Gap Analysis

### Currently in WinUI 3 (`Views/Pages/ProfilesPage.xaml`)
- Left column: profile ListView (with PersonPicture + name)
- Right column: InfoBar + 2 cards
  - Card 1 "Selected Profile Management": TextBlock + Button "Mở Chrome Browser" + Button "+ Tạo Profile Mới"
  - Card 2 "Cấu hình & Kiểm tra Proxy": TextBox proxy + Button "Test Proxy Connection"

**Total: ~10 controls**

### Missing in WinUI 3

#### Group A — 3 buttons missing
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `BtnOpenProfileFolder` | "📂 Open Folder" | Add `Button Command="{Binding OpenProfileFolderCommand}"` |
| `BtnSetDefaultProfile` | "⭐ Đặt làm mặc định" | Add `Button Command="{Binding SetDefaultProfileCommand}"` |
| `BtnDeleteProfile` | "🗑️ Delete Profile" | Add `Button Command="{Binding DeleteProfileCommand}"` |

#### Group B — Custom GPT URL section (WPF lines 656-665)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `TxtCustomGptUrl` + `BtnSaveCustomGptUrl` | Custom GPT URL input + Save | Add new card with TextBox + Button. Bind to `CustomGptUrl` + `SaveCustomGptUrlCommand` (new properties/commands on VM) |

#### Group C — Progress indicator
| Missing | VM property | Action needed |
|---|---|---|
| `ProgressRing IsActive="{Binding IsBusy}"` | `ProfilesViewModel.IsBusy` | Add ProgressRing overlay during long ops |

### Migration effort estimate
- Group A (3 buttons): 1.5 hours
- Group B (custom GPT): 1.5 hours
- Group C (ProgressRing): 0.5 hour

**Total: ~3.5 hours**

---

## Tab 4: Settings — Gap Analysis

### Currently in WinUI 3 (`Views/Pages/SettingsPage.xaml`)
- Header
- InfoBar
- CommandBar with Save button
- Left card "API Keys & Credentials":
  - TextBox "OpenAI / Gemini API Key" (PlaceholderText="sk-...")
- Right card "Concurrency & Execution Settings":
  - NumberBox "Max Concurrent Threads"
  - CheckBox "Headless Mode"

**Total: ~7 controls**

### Missing in WinUI 3

#### Group A — PasswordBoxes × 3 (missing)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `PbSettingsAi84ApiKey` | AI84 API Key | Use PasswordBox control (WinUI 3 has `PasswordBox` since 1.4). Bind to `SettingsViewModel.Ai84ApiKey` (new property) |
| `PbSettingsSupabaseDbUrl` | Supabase DB URL | Same |
| `PbSettingsImageApiKey` | Image API Key | Same |

#### Group B — 5 API URLs (missing)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `TxtSettingsImageApiUrl` | Image API URL | Add TextBox bound to `ImageApiUrl` |
| `TxtSettingsSubtitleApiUrl` | Subtitle API URL | Add TextBox bound to `SubtitleApiUrl` |
| (AI84 + Supabase + Image API Key already in Group A) | | |

#### Group C — Directory pickers (partial)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `TxtSettingsChromeProfilesDir` + `BtnBrowseChromeProfilesDir` | Chrome Profiles dir + Browse | Add row: TextBox + Button. Browse uses `FolderPicker` (WinUI 3 API). |
| (OutputPath already exists) | | |

#### Group D — Proxies section
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `TxtSettingsProxiesFilePath` + Browse + Test | Proxies file path + Browse + Test | Add TextBox + 2 buttons |
| `TxtSettingsManualProxies` + `BtnTestManualProxies` | Manual proxies multiline + Test | Add multiline TextBox + Button |

#### Group E — 3 buttons missing
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `BtnExportSettings` | "Xuất Cấu Hình" | Add `Button Command="{Binding ExportSettingsCommand}"` |
| `BtnImportSettings` | "Nhập Cấu Hình" | Add `Button Command="{Binding ImportSettingsCommand}"` |
| `BtnCheckRequirements` | "Kiểm Tra Hệ Thống" | Add `Button Command="{Binding CheckRequirementsCommand}"` |
| `BtnCheckAi84Key` | "Kiểm tra Key" (inline with API key row) | Add inline Button |

#### Group F — Theme selector
| Missing | VM property | Action needed |
|---|---|---|
| Theme selector (Light/Dark/System) | `SettingsViewModel.SelectedTheme` | Add RadioButtons or ComboBox bound to `SelectedTheme` |

### Migration effort estimate
- Group A (3 PasswordBoxes): 2 hours
- Group B (2 URLs): 1 hour
- Group C (1 dir picker): 1 hour
- Group D (proxies): 2 hours
- Group E (3-4 buttons): 1.5 hours
- Group F (theme selector): 1 hour

**Total: ~8.5 hours**

---

## Tab 5: History — Gap Analysis

### Currently in WinUI 3 (`Views/Pages/HistoryPage.xaml`)
- Header
- CommandBar: Load History / Clear History (2 buttons)
- InfoBar
- ListView 5-col (ID / VideoUrl / CreatedAtFormatted / Status / Logs)

**Total: ~5 controls**

### Missing in WinUI 3

#### Group A — Date picker (ListBox of available dates)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| `LboxHistoryDates` | ListBox of dates (left side) | Add `ListView ItemsSource="{Binding AvailableDates}"` (property exists in VM). Bind to `SelectedDate`. |

#### Group B — Filter bar (same as Tasks)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| 3 TextBoxes + 6 CheckBoxes + Clear button | Filter Video URL / Language / Voice ID / 6 failure checkboxes / Clear | Same pattern as TasksPage Group C |

#### Group C — Step Status badges (5 step × 3 state)
| Missing | WPF equivalent | Action needed |
|---|---|---|
| Per-row step badges T/R/W/V/S/G (each with Running/Done/Failed visual) | `DgridHistoryTasks` column template (lines 864-1001) | This is the largest item. Add to ListView ItemTemplate a `StackPanel Orientation="Horizontal"` with 5 `Border` controls. Each Border has 4 styles (default/Running/Done/Failed) bound via converters or using VisualStateManager. |

#### Group D — Per-row action buttons
| Missing | WPF equivalent | Action needed |
|---|---|---|
| "Logs" button per row | `BtnViewHistoryTaskLog_Click` | Add `Button Command="{Binding ViewLogCommand}"` |
| "Assets" button per row | `BtnViewHistoryTaskAssets_Click` | Add `Button Command="{Binding ViewAssetsCommand}"` |

### Migration effort estimate
- Group A (date picker): 1.5 hours
- Group B (filter bar): 3 hours
- Group C (step badges): 5 hours (5 borders × 3 states × styles)
- Group D (per-row buttons): 1 hour

**Total: ~10.5 hours**

---

## Tab 6: Batch Image Gen — Gap Analysis 🔴 ENTIRE TAB MISSING

### Currently in WinUI 3
- **No page exists** for this tab. `MainWindow.xaml.cs` switches by Tag to `TasksPage` / `PoolPage` / `ProfilesPage` / `GeminiPage` / `HistoryPage` / `SettingsPage`. There is no BatchImageGenPage.

### What needs to be built — full new page

This is a 2-level UI (Dashboard → Editor). See WPF inventory for the full breakdown.

#### Files to create
1. `src/AssetAutomator.WinUI/Views/Pages/BatchImageGenPage.xaml`
2. `src/AssetAutomator.WinUI/Views/Pages/BatchImageGenPage.xaml.cs`
3. `src/AssetAutomator.WinUI/ViewModels/BatchImageGenViewModel.cs`

#### MainWindow.xaml.cs update
Add NavigationViewItem for "Batch Image Gen" and route to BatchImageGenPage.

#### VM requirements (from WPF `MainWindow.BatchImageGen.cs`)
The existing WPF logic needs to be ported into a clean ViewModel:
- ObservableCollection<ProjectModel> Projects
- SelectedProject / Projects visibility flag
- Output directory, scripts JSON, character ref
- AspectRatio (ComboBox), Engine (ComboBox), Provider (RadioButtons), Model (RadioButtons), Upscale (RadioButtons), Concurrency
- Character image path (drag-drop)
- BatchCardModel list (SceneIndex, SceneTitle, Transcript, Prompt, ImagePath, Status, IsDone, AspectRatio, Engine, FinishedTimeFormatted, ErrorMessage)
- Commands: BackToProjects, NewProject, OpenProject, DeleteProject, SaveProject, OpenFlowProjectUrl, ToggleView, OpenFolder, BrowseProjectsDir, BrowseBatchOutputDir, ImportJson, UpdateScriptJson, BrowseCharacterRef, ClearCharacter, GenerateBatch, OpenImage, Regenerate, CopyPrompt
- Converters: AspectRatioHeightConverter

### Migration effort estimate
- VM (port from WPF partial): 6 hours
- Dashboard panel + cards: 10 hours
- Editor panel sections: 16 hours
- Expander engine config: 12 hours
- Card grid view with overlay: 16 hours
- Table view fallback: 4 hours
- Status/progress: 2 hours

**Total: ~66 hours (2 weeks)**

This is the **largest** piece of work in the entire port.

---

## Tab 7: Gemini AI Creator — Gap Analysis

### Currently in WinUI 3 (`Views/Pages/GeminiPage.xaml`)
- Header
- 2-column layout:
  - Left: InfoBar + 2 cards (Topic prompt TextBox + Style/Voice TextBoxes) + 1 button "Sinh kịch bản bằng Gemini AI"
  - Right: Generated Script (readonly TextBox)

**Total: ~8 controls** — this is a SIMPLIFIED single-script generator, not the multi-task queue that WPF has.

### Missing in WinUI 3 (entire multi-task queue UI)

#### Group A — Top toolbar
| Missing | WPF equivalent | Action needed |
|---|---|---|
| "➕ Thêm Task Mới" | `BtnAddGeminiTask_Click` | Add AppBarButton |
| "🗑️ Xóa Task Đã Chọn" | `BtnDeleteGeminiTasks_Click` | Add AppBarButton |
| "🔍 Gợi Ý Chủ Đề" | `BtnSuggestTopics_Click` | Add AppBarButton |
| "🔄 Tải Danh Sách Gems" | `BtnRefreshGems_Click` | Add AppBarButton |
| "🔑 Nạp Cookies Gemini" | `BtnImportCookies_Click` | Add AppBarButton |
| "🚀 CHẠY TASK ĐÃ CHỌN" | `BtnRunSelectedGeminiTasks_Click` | Add AppBarButton (accent) |

#### Group B — DataGrid 7 columns
| Missing | WPF equivalent | Action needed |
|---|---|---|
| Select checkbox column | `ChkSelectAllGeminiTasks` + per-row checkbox | Add as GridView-style column |
| Topic/Link YouTube TextBox column | TextBox bound to `Topic` | Add as editable column |
| Progress bar + Status text column | ProgressBar + TextBlock in template | Add as combined column |
| Scriptwriter badge column | Border + TextBlock with ToolTip | Add |
| Scene Creator badge column | Border + TextBlock with ToolTip | Add |
| Status badge column | Border with NodeStatusToBrushConverter | Add |
| Actions: ▶ Run + ⋮ menu | `BtnRunSingleGeminiTask_Click` + `BtnGeminiTaskMenu_Click` | Add 2 buttons |

#### Group C — RowDetails popup template
| Missing | WPF equivalent | Action needed |
|---|---|---|
| 🎬 Scriptwriter panel: Gem ComboBox, Model ComboBox, Deep Research CheckBox | RowDetailsTemplate Grid | Add as Expander or flyout per row |
| 🎭 Scene Creator panel: Gem ComboBox, Model ComboBox | RowDetailsTemplate | " |
| Voice ID TextBox + 🔍 search button | `BtnBrowseVoice_Click` | " |
| Image Provider ComboBox | ComboBox | " |
| Character Ref TextBox + 📁 browse button | `BtnBrowseCharacterRef_Click` | " |
| Collapse button | `BtnCollapseGeminiRowDetails_Click` | " |

#### Group D — Python Server Log Panel
| Missing | WPF equivalent | Action needed |
|---|---|---|
| Toggle button | `BtnTogglePythonLogs_Click` | Add Button |
| Status text | `TxtPythonServerStatus` | Add TextBlock bound to `PythonServerStatus` |
| Clear button | `BtnClearPythonServerLog_Click` | Add Button |
| Log RichTextBox | `TxtPythonServerLog` | Use TextBox (WinUI 3 doesn't have RichTextBox — use ScrollViewer + TextBlock, or accept TextBox) |

#### Group E — Sidebar 5-Step Accordion
| Missing | WPF equivalent | Action needed |
|---|---|---|
| Step 1 Expander + Badge + Log | `ExpanderStep1`/`BadgeStep1`/`TxtLogStep1` | Add 5 Expanders with badges |
| Step 2..5 | same pattern | Same |

#### Group F — VM properties/commands not bound
| Missing | VM property/command | Action needed |
|---|---|---|
| `VideoDurationMinutes` | NumberBox | Add NumberBox in left column |
| `IsGenerating` | ProgressRing overlay | Add ProgressRing during generate |
| `ConsoleLogs` | TextBox/RichTextBox log | Add TextBox for logs |
| `CancelGeneration` | Button | Add "Cancel" button |
| `ClearLogs` | Button | Add "Clear logs" button |

### Migration effort estimate
- Group A (toolbar): 4 hours
- Group B (DataGrid 7-col): 12 hours (high complexity due to template columns)
- Group C (RowDetails): 16 hours (4 panels + 5 ComboBoxes + 2 TextBoxes + 3 buttons)
- Group D (Python log panel): 8 hours
- Group E (5-Step Accordion): 12 hours
- Group F (VM bindings): 4 hours

**Total: ~56 hours (1.5 weeks)**

---

## Dialog Gap Summary

| Dialog | Missing in WinUI 3 | Effort |
|---|---|---|
| BulkTask | — | ✅ Already equivalent |
| License | 3 buttons (Transfer, Close, Activate) + status details | 2 hours |
| NewProject | 1 TextBox (Description) | 0.5 hour |
| ProxyTestResult | — (functionally equivalent) | ✅ |
| ScenesViewer | 3 elements (Stats, Apply All button, Copy button wired in code-behind) | 2 hours |
| Update | 2 buttons (Skip, Remind Later) + header title texts | 1.5 hours |
| VoiceSelector | 9 controls (ComboSort/Gender/Age, TxtLanguage, PanelUseCases 7 CheckBox, BtnApplyFilters, ComboPageSize, BtnPrevPage, TxtPageIndex, BtnNextPage) | 6 hours |
| WebViewLogin | Real WebView2 (deferred — current is Border placeholder) | Phase D |

**Total dialog work: ~12 hours**

---

## Global — Converters ✅ COMPLETED (Sprint 5)

WPF had 4 custom converters; **all have been ported to WinUI 3** in Sprint 5 and additionally expanded with 3 helpers for the Batch Image Gen card view:

| Converter | Used for | Status |
|---|---|---|
| `YoutubeUrlConverter` | Convert YouTube URL → video ID for display | ✅ Done (`Converters/YoutubeUrlConverter.cs`) |
| `HexToBrushConverter` | Convert hex string → Brush | ✅ Done (in `NodeStatusToBrushConverter.cs`) |
| `NodeStatusToBrushConverter` | Map pipeline status (`NodeStatus` enum + string) → color Brush | ✅ Done (`Converters/NodeStatusToBrushConverter.cs`) |
| `AspectRatioHeightConverter` | Aspect-ratio string + width parameter → calculated height | ✅ Done (`Converters/AspectRatioHeightConverter.cs`) |
| `StringToImageSourceConverter` | String file path → `BitmapImage` for `Image.Source` (WinUI 3 lacks implicit coercion) | ✅ Done (`Converters/StringToImageSourceConverter.cs`) |
| `StepStatusToBrushConverter` | Step status string (Pending/Running/Done/Failed) → semantic Brush | ✅ Done (`Converters/StepStatusToBrushConverter.cs`) |
| `BoolToVisibilityConverter` | Bool → `Visibility` for cards/empty-state toggling | ✅ Done (`Converters/BoolToVisibilityConverter.cs`) |

Wired into `BatchImageGenPage.xaml`: Card Grid uses `AspectRatioHeightConverter` to compute height from the card's aspect ratio, `StringToImageSourceConverter` to load generated images, `NodeStatusToBrushConverter` to color the status badge.

---

## Grand Total Recap

| Section | Hours |
|---|---:|
| Tab 1 (Tasks) | 16 |
| Tab 2 (Pool) | 9 |
| Tab 3 (Profiles) | 3.5 |
| Tab 4 (Settings) | 8.5 |
| Tab 5 (History) | 10.5 |
| Tab 6 (Batch Image Gen — full new page) | 66 |
| Tab 7 (Gemini AI Creator — full multi-task UI) | 56 |
| Dialogs | 12 |
| Global (Converters) | 4.5 |
| **Sum** | **186 hours ≈ 5 tuần full-time** |

This excludes Phase D polish (sidebar drawer animation, real WebView2 OAuth, style theme port, hover overlays) which adds another ~54 hours.