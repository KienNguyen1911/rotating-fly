# WPF UI Inventory — AssetAutomator

> Comprehensive inventory of every named UI element in the WPF `AssetAutomator.UI` project.
> Used as the reference baseline for WinUI 3 porting (see `winui3-port/README.md`).

## File structure

```
src/AssetAutomator.UI/
├── MainWindow.xaml                     (2,071 lines) ← All 7 tabs in one TabControl
├── MainWindow.xaml.cs                  (main partial)
├── MainWindow.AutomationSteps.cs       (partial — Task pipeline logic)
├── MainWindow.BatchImageGen.cs         (partial — Batch Image Gen logic)
├── MainWindow.GeminiCreator.cs         (partial — Gemini AI Creator logic)
├── MainWindow.History.cs               (partial — History logic)
├── MainWindow.Profiles.cs              (partial — Chrome Profiles logic)
├── MainWindow.Tasks.cs                 (partial — Tasks logic)
└── Windows/
    ├── BulkTaskWindow.xaml             (39 lines)
    ├── LicenseWindow.xaml              (63 lines)
    ├── NewProjectWindow.xaml           (30 lines)
    ├── ProxyTestResultWindow.xaml      (80 lines)
    ├── ScenesViewerWindow.xaml         (80 lines)
    ├── UpdateWindow.xaml               (87 lines)
    ├── VoiceSelectorWindow.xaml        (200 lines)
    └── WebViewLoginWindow.xaml         (28 lines)

Total: ~2,678 lines of XAML across 9 files.
```

---

## Tab 1: Automation Tasks (MainWindow.xaml lines 123-437)

### Top Toolbar
- `BtnAddTask` Click=`BtnAddTask_Click` — "Tạo Task", Style=GreenButton
- `BtnAddBulkTasks` Click=`BtnAddBulkTasks_Click` — "Tạo Nhiều", Style=PrimaryButton
- `BtnRun` Click=`BtnRun_Click` — "Chạy Task Đã Chọn", Style=PrimaryButton

### Step Selector (WrapPanel, 6 checkboxes)
- `ChkStepDownloadThumbnail` — Content="Download Thumbnail", IsChecked=True
- `ChkStepGenerateThumbnail` — Content="Generate Thumbnail", IsChecked=True
- `ChkStepGetTranscript` — Content="Get Transcript", IsChecked=True
- `ChkStepRewrittenTranscript` — Content="Rewritten Transcript", IsChecked=True
- `ChkStepVoiceover` — Content="Voiceover", IsChecked=True
- `ChkStepSrt` — Content="SRT", IsChecked=True

### Filters Bar (WrapPanel)
- `TxtFilterVideoUrl` TextChanged=`FilterFields_TextChanged`, Width=130
- `TxtFilterLanguage` TextChanged=`FilterFields_TextChanged`, Width=85
- `TxtFilterVoiceId` TextChanged=`FilterFields_TextChanged`, Width=85
- `ChkFilterT` Checked/Unchecked=`FilterCheckbox_Changed` — Content="T"
- `ChkFilterR` Checked/Unchecked=`FilterCheckbox_Changed` — Content="R"
- `ChkFilterW` Checked/Unchecked=`FilterCheckbox_Changed` — Content="W"
- `ChkFilterV` Checked/Unchecked=`FilterCheckbox_Changed` — Content="V"
- `ChkFilterS` Checked/Unchecked=`FilterCheckbox_Changed` — Content="S"
- `ChkFilterG` Checked/Unchecked=`FilterCheckbox_Changed` — Content="G"
- `BtnClearFilters` Click=`BtnClearFilters_Click` — "Xoá Lọc"

### DataGrid: DgridTasks
- Header checkbox: `ChkSelectAllTasks` Click=`ChkSelectAllTasks_Click`
- Per-row action buttons (template): Run, View Log, View Assets, Delete
  - `BtnRunSingleTask_Click`, `BtnViewTaskLog_Click`, `BtnViewTaskAssets_Click`, `BtnDeleteTask_Click`

### Sidebar (overlay)
- `SidebarLogs` — drawer bên phải, drag-handle resizable
- `TxtSidebarLog` — RichTextBox log content
- SidebarDragHandle_MouseLeftButtonDown/Move/Up — drag resize
- `BtnCloseSidebar_Click` — close button

---

## Tab 2: Image Pool (lines 438-567)

### Metric Cards (6)
- `TxtPoolRunningWorkers` — Text="0", FontSize=20, Bold
- `TxtPoolMaxWorkers` — Text="0"
- `TxtPoolWaitingRequests` — Text="0"
- `TxtPoolProcessingRequests` — Text="0", Foreground=Primary
- `TxtPoolFinishedRequests` — Text="0", Foreground=SemanticUp (green)
- `TxtPoolAvgTime` — Text="0.0"

### Refresh
- `BtnRefreshPool` Click=`BtnRefreshPool_Click` — "Tải lại"

### DataGrid: DgridPoolRequests
- Columns: ID, Prompt, Status, Engine, Progress, Started, Finished, Action
- Per-row action buttons (Open Image / Cancel)

---

## Tab 3: Chrome Profiles (lines 568-671)

### Profile List (left)
- `LboxProfiles` SelectionChanged=`LboxProfiles_SelectionChanged`, ItemsSource=`{Binding ProfileList}`, SelectedIndex=0

### Selected Profile Panel
- `TxtDefaultProfileName` — Text="None"
- `BtnOpenProfileBrowser` Click=`BtnOpenProfileBrowser_Click` — "🌐 Open Browser"
- `BtnOpenProfileFolder` Click=`BtnOpenProfileFolder_Click` — "📂 Open Folder"
- `BtnSetDefaultProfile` Click=`BtnSetDefaultProfile_Click` — "⭐ Đặt làm mặc định"
- `BtnDeleteProfile` Click=`BtnDeleteProfile_Click` — "🗑️ Delete Profile"

### Create Profile
- `TxtNewProfileName` — empty TextBox
- `BtnCreateProfile` Click=`BtnCreateProfile_Click` — "Create & Initialize Profile"

### Custom GPT URL
- `TxtCustomGptUrl` — empty TextBox
- `BtnSaveCustomGptUrl` Click=`BtnSaveCustomGptUrl_Click` — "Lưu URL"

---

## Tab 4: Settings (lines 672-785)

### API Keys & Credentials
- `PbSettingsAi84ApiKey` — PasswordBox
- `BtnCheckAi84Key` Click=`BtnCheckAi84Key_Click` — "Kiểm tra Key"
- `PbSettingsSupabaseDbUrl` — PasswordBox
- `TxtSettingsImageApiUrl` — TextBox
- `TxtSettingsSubtitleApiUrl` — TextBox
- `PbSettingsImageApiKey` — PasswordBox

### Directories
- `TxtSettingsChromeProfilesDir` + `BtnBrowseChromeProfilesDir` Click=`BtnBrowseChromeProfilesDir_Click` — "Browse..."
- `TxtSettingsOutputsDir` + `BtnBrowseOutputsDir` Click=`BtnBrowseOutputsDir_Click` — "Browse..."

### Concurrency
- `TxtSettingsMaxConcurrentTasks` — Text="4", Width=80

### Proxies
- `TxtSettingsProxiesFilePath` + `BtnBrowseProxiesFile` Click=`BtnBrowseProxiesFile_Click` + `BtnTestProxies` Click=`BtnTestProxies_Click` — "Test Proxies"
- `TxtSettingsManualProxies` — multiline TextBox
- `BtnTestManualProxies` Click=`BtnTestManualProxies_Click` — "Kiểm tra Proxy thủ công"

### Action Buttons
- `BtnSaveSettings` Click=`BtnSaveSettings_Click` — "Lưu Cấu Hình", Style=GreenButton
- `BtnExportSettings` Click=`BtnExportSettings_Click` — "Xuất Cấu Hình"
- `BtnImportSettings` Click=`BtnImportSettings_Click` — "Nhập Cấu Hình"
- `BtnCheckRequirements` Click=`BtnCheckRequirements_Click` — "Kiểm Tra Hệ Thống"

---

## Tab 5: History (lines 786-1019)

### Date Picker (left)
- `LboxHistoryDates` SelectionChanged=`LboxHistoryDates_SelectionChanged`, ItemsSource=`{Binding HistoryDates}`

### Filter Bar
- `TxtHistoryFilterVideoUrl`, `TxtHistoryFilterLanguage`, `TxtHistoryFilterVoiceId` — TextChanged=`HistoryFilterFields_TextChanged`
- `ChkHistoryFilterT`..`ChkHistoryFilterG` — Checked/Unchecked=`HistoryFilterCheckbox_Changed`
- `BtnHistoryClearFilters` Click=`BtnHistoryClearFilters_Click` — "Xoá Lọc"

### DataGrid: DgridHistoryTasks
- Columns: Ngày tạo, Link Video, Ngôn ngữ, Voice ID, Steps Status (template with 5 step badges: T, R, W, V, S, G — each with Running/Done/Failed states), Action (Logs / Assets buttons)
- Action buttons: `BtnViewHistoryTaskLog_Click`, `BtnViewHistoryTaskAssets_Click`

---

## Tab 6: Batch Image Gen (lines 1020-1495) — 2-level UI

### Level 1: Projects Dashboard Panel (`PanelProjectsDashboard` Visibility=Visible)
- Header:
  - `TxtProjectsStoragePath` — readonly TextBox, Width=380
  - `BtnBrowseProjectsDir` Click=`BtnBrowseProjectsDir_Click` — "Browse..."
  - `BtnCreateProjectDashboard` Click=`BtnNewProject_Click` — "➕ Tạo dự án mới"
- Cards grid:
  - `ItemsControlProjectsGrid` với UniformGrid 3-col, DataTemplate (project card: folder icon, title, dates, output dir, 2 badges, 2 buttons)
  - `BtnOpenProjectCard` Click=`BtnOpenProjectCard_Click` — "📂 Mở Dự Án"
  - `BtnDeleteProjectCard` Click=`BtnDeleteProjectCard_Click` — "🗑️ Xóa"

### Level 2: Project Editor Panel (`PanelProjectEditor` Visibility=Collapsed)
- Left sidebar (340px):
  - Section 1 "NHÂN VẬT GỐC":
    - `BorderBatchCharDropzone` — dashed border, Cursor=Hand, MouseLeftButtonDown=`BtnBatchSelectChar_Click`
    - `PanelBatchCharEmpty` (empty state) / `PanelBatchCharHasImage` Visibility=Collapsed (preview state)
    - `ImgBatchCharPreview` — Image control
    - `BtnBatchClearChar` Click=`BtnBatchClearChar_Click` — "✕"
    - `TxtBatchCharInfo` — info text
    - `TxtBatchCharTag` — "@character"
  - Section 2 "KỊCH BẢN (JSON OBJECT/ARRAY)":
    - `TxtBatchScriptJson` — multiline TextBox, Height=180, FontFamily=Consolas
    - `BtnBatchImportJson` Click=`BtnBatchImportJson_Click` — "📄 Chọn file JSON"
    - `BtnUpdateScriptJson` Click=`BtnUpdateScriptJson_Click` — "{ } Cập nhật kịch bản"
  - Section 3 "CẤU HÌNH":
    - `CboxBatchAspect` SelectionChanged=`CboxBatchAspect_SelectionChanged` — 5 items (16:9, 9:16, 1:1, 4:3, 3:4)
    - `TxtBatchOutputDir` + `BtnBrowseBatchOutputDir` Click=`BtnBrowseBatchOutputDir_Click`
    - Hidden RadioButtons: `RadAspect169`, `RadAspect11`, `RadAspect916`, `RadAspect43`, `RadAspect34`
    - Expander "Cấu hình Engine / Model AI":
      - Provider RadioButtons: `RadProviderGlabs` (IsChecked=True), `RadProviderFlowLocal` — Checked=`RadProvider_Checked`
      - `CboxBatchEngine` SelectionChanged=`CboxBatchEngine_SelectionChanged` — 3 items (Flow/Meta/Grok)
      - `PanelBatchFlowOptions`:
        - `PanelModelsGlabs`: `RadModelBanana2` (IsChecked=True), `RadModelBananaPro`, `RadModelBananaLite`
        - `PanelModelsFlowLocal` Visibility=Collapsed: `RadModelGemini31Flash`, `RadModelGemini30Pro`, `RadModelImagen4Preview`, `RadModelNanoBanana2Flow`, `RadModelNanoBananaProFlow`
        - Upscale RadioButtons: `RadUpscaleNone` (IsChecked=True), `RadUpscale2K`, `RadUpscale4K`
      - `CboxBatchConcurrency` SelectionChanged=`CboxBatchConcurrency_SelectionChanged` — 5 items (1/2/4/6/8)
- Bottom fixed button:
  - `BtnBatchGenerate` Click=`BtnBatchGenerate_Click` — "⚡ Tạo hàng loạt (4 ảnh song song)"
- Right main panel:
  - Header:
    - `BtnBackToProjects` Click=`BtnBackToProjects_Click` — "⬅️ Danh sách Dự án"
    - `TxtVideoTitle` — project title
    - `ProgressBatchGen` — ProgressBar
    - `TxtBatchProgress` — "Đã tạo 0/0 ảnh (0%)"
    - `BtnSaveProjectEditor` Click=`BtnSaveProject_Click` — "💾 Lưu dự án"
    - `BtnOpenFlowProjectUrl` Click=`BtnOpenFlowProjectUrl_Click` — "🌐 Google Flow Web"
    - `BtnBatchViewToggle` Click=`BtnBatchViewToggle_Click` — "📋 Table Queue View"
    - `BtnBatchOpenFolder` Click=`BtnBatchOpenFolder_Click` — "📁 Open Folder"
  - Card grid (`ScrollCardGrid` + `ItemsControlBatchCards`):
    - UniformGrid 2 cols, DataTemplate per scene card with overlay buttons (`BtnCardRegenerate`, `BtnCardOpenImage`, `BtnCardCopyPrompt`) + status badges + transcript + prompt snippet
    - `CardImageContainer_Click` — MouseLeftButtonDown handler
  - Fallback table view (`BorderTableView` Visibility=Collapsed + `DgridBatchImageItems`):
    - 9 columns: #, Scene, Prompt, Status, Engine, Aspect, Finished, Error/Info, Action
    - Action button: `BtnBatchOpenImage_Click`

---

## Tab 7: Gemini AI Creator (lines 1497-1886)

### Top Toolbar
- "➕ Thêm Task Mới" Click=`BtnAddGeminiTask_Click`
- "🗑️ Xóa Task Đã Chọn" Click=`BtnDeleteGeminiTasks_Click`
- `BtnSuggestTopics` Click=`BtnSuggestTopics_Click` — "🔍 Gợi Ý Chủ Đề"
- `BtnRefreshGems` Click=`BtnRefreshGems_Click` — "🔄 Tải Danh Sách Gems"
- `BtnImportCookies` Click=`BtnImportCookies_Click` — "🔑 Nạp Cookies Gemini"
- "🚀 CHẠY TASK ĐÃ CHỌN" Click=`BtnRunSelectedGeminiTasks_Click`

### DataGrid: DgridGeminiTasks
- Header checkbox: `ChkSelectAllGeminiTasks`
- Columns (7):
  1. Select CheckBox (template)
  2. Topic/Link TextBox (editable in row, binding to Topic)
  3. Progress bar + status text (template with custom ProgressBar style)
  4. Scriptwriter badge (template with ToolTip + Text)
  5. Scene Creator badge (template)
  6. Status badge (template with NodeStatusToBrushConverter)
  7. Actions: ▶ Run button + ⋮ menu button (`BtnRunSingleGeminiTask_Click`, `BtnGeminiTaskMenu_Click`)

### Row Details Template (popup xuống khi chọn row)
- Collapse button: `BtnCollapseGeminiRowDetails_Click`
- 🎬 Scriptwriter panel:
  - Gem ComboBox binding `AvailableScriptwriterGems`, `SelectedScriptwriterGem`
  - Model ComboBox binding `AvailableAiModels`, `ScriptwriterModel`
  - CheckBox "Bật Deep Research" binding `EnableDeepResearch`
- 🎭 Scene Creator panel:
  - Gem ComboBox binding `AvailableSceneCreatorGems`, `SelectedSceneCreatorGem`
  - Model ComboBox binding `AvailableAiModels`, `SceneCreatorModel`
- Voice ID + Provider + Character Ref row:
  - Voice ID TextBox binding `VoiceId` + 🔍 search button `BtnBrowseVoice_Click`
  - Image Provider ComboBox binding `AvailableImageProviders`, `SelectedImageProvider`
  - Character Ref TextBox binding `CharacterRef` + 📁 browse button `BtnBrowseCharacterRef_Click`

### Python Server Log Panel (collapsible, Row 2)
- `BtnTogglePythonLogs` Click=`BtnTogglePythonLogs_Click` — toggle expand/collapse
- `TxtPythonServerStatus` — "⚪ Unknown"
- `BtnClearPythonServerLog_Click` — "🗑️ Clear"
- `TxtPythonServerLog` — RichTextBox, IsReadOnly, MinHeight=120, MaxHeight=280, FontFamily=Consolas

### Status Bar (Row 3)
- `TxtGeminiStatusIcon` — "💡"
- `TxtGeminiStatus` — "Sẵn sàng — Nhấn 'Tải Danh Sách Gems' để làm mới hoặc 'Nạp Cookies' để đăng nhập Gemini."

### Sidebar 5-Step Accordion (`GridGeminiStepAccordion` Visibility=Collapsed)
- Step 1: Gemini Deep Research & Script
  - `ExpanderStep1` IsExpanded=True
  - `BadgeStep1` — badge background
  - `TxtBadgeStep1` — "⚪ Chờ"
  - `TxtLogStep1` — RichTextBox log
- Step 2: Voiceover & SRT Subtitles (`ExpanderStep2`, `BadgeStep2`, `TxtBadgeStep2`, `TxtLogStep2`)
- Step 3: Scene Breakdown & Prompts (`ExpanderStep3`, `BadgeStep3`, `TxtBadgeStep3`, `TxtLogStep3`)
- Step 4: Batch Image Generation (`ExpanderStep4`, `BadgeStep4`, `TxtBadgeStep4`, `TxtLogStep4`)
- Step 5: Export & Packaging (`ExpanderStep5`, `BadgeStep5`, `TxtBadgeStep5`, `TxtLogStep5`)

---

## Header (lines 22-118)

- `BtnLicense` Click=`BtnLicense_Click` — "🔑 Bản Quyền"
- `BtnCheckUpdate` Click=`BtnCheckUpdate_Click` — "🔄 Check Update"
- `BtnThemeToggle` Click=`BtnThemeToggle_Click` — toggle light/dark mode
  - `TxtThemeIcon` — "🌙"
  - `TxtStatusLabel` — "Dark Mode"

---

## Dialogs (Windows/)

### BulkTaskWindow (39 lines)
- `TxtBulkInput` — multiline TextBox (paste list of YouTube URL + Voice ID per line)
- `BtnCancel` Click=`BtnCancel_Click`
- `BtnCreate` Click=`BtnCreate_Click`

### LicenseWindow (63 lines)
- `TxtLicenseKey` — TextBox FontFamily=Consolas
- `BorderStatusCard` — status card
- `TxtStatusHeader` — "Trạng thái: Chưa kích hoạt"
- `TxtStatusDetails` — explanatory text
- `BtnDeactivate` Click=`BtnDeactivate_Click` Visibility=Collapsed — "Hủy kích hoạt"
- `BtnTransfer` Click=`BtnTransfer_Click` — "Chuyển máy"
- `BtnClose` Click=`BtnClose_Click` — "Đóng"
- `BtnActivate` Click=`BtnActivate_Click` — "Kích hoạt"

### NewProjectWindow (30 lines)
- `TxtProjectName` — empty TextBox
- `BtnCancel` Click=`BtnCancel_Click`
- `BtnCreate` Click=`BtnCreate_Click`

### ProxyTestResultWindow (80 lines)
- `TxtSummary` — "Tổng cộng: 0 ONLINE, 0 OFFLINE"
- `GridResults` — DataGrid 3-col (Proxy Address / Trạng thái badge / Chi tiết)
- "Đóng" button Click=`BtnClose_Click`

### ScenesViewerWindow (80 lines)
- `TxtVideoTitle` — video title
- `TxtSceneStats` — "Total scenes: 0"
- `BtnApplyAllToBatch` Click=`BtnApplyAllToBatch_Click` — "📥 Import All Prompts to Queue"
- `DgridScenes` — DataGrid 4-col (Scene # / Transcript / Image Prompt / Action)
- Per-row "Copy" button Click=`BtnCopyPrompt_Click`
- "Close Window" button Click=`BtnClose_Click`

### UpdateWindow (87 lines)
- `TxtHeaderTitle` — "Đã có phiên bản cập nhật mới!"
- `TxtHeaderSubtitle` — explanatory
- `TxtCurrentVersion` — "v1.2.4.0"
- `TxtNewVersion` — "v1.2.5"
- `TxtReleaseNotes` — multiline TextBox, IsReadOnly
- `BtnSkipVersion` Click=`BtnSkipVersion_Click` — "🚫 Bỏ qua bản này"
- `BtnRemindLater` Click=`BtnRemindLater_Click` — "⏰ Nhắc tôi sau"
- `BtnUpdateNow` Click=`BtnUpdateNow_Click` — "🚀 Cập nhật ngay"

### VoiceSelectorWindow (200 lines) — most complex dialog
- Filter sidebar:
  - `TxtSearch` — search name/label
  - `ComboSort` — 4 items (Trending/Created Date/Cloned Count/1 Year Usage)
  - `ComboGender` — 4 items (All/Male/Female/Neutral)
  - `ComboAge` — 4 items (All/Young/Middle/Old)
  - `TxtLanguage` — ISO 639-1 language code
  - `PanelUseCases` — 7 CheckBoxes (Conversational/Narrative Story/Social Media/Characters Animation/Informative Educational/Advertisement/Entertainment TV)
  - `BtnApplyFilters` Click=`BtnApplyFilters_Click`
- Voices grid:
  - `DgridVoices` SelectionChanged=`DgridVoices_SelectionChanged` — 5-col (Name/Gender/Language/Description/Voice ID)
  - `OverlayStatus` Visibility=Collapsed
  - `TxtStatusText` — "Loading voices..."
  - `ProgressLoading` — ProgressBar IsIndeterminate
- Pagination:
  - `ComboPageSize` SelectionChanged=`ComboPageSize_SelectionChanged` — 4 items (15/30/50/100)
  - `BtnPrevPage` Click=`BtnPrevPage_Click`
  - `TxtPageIndex` — "Page 1"
  - `BtnNextPage` Click=`BtnNextPage_Click`
- Footer:
  - `TxtSelectedVoiceId` — "None"
  - `BtnCancel` Click=`BtnCancel_Click`
  - `BtnConfirm` Click=`BtnConfirm_Click` — "Select Voice"

### WebViewLoginWindow (28 lines)
- `TxtStatus` — "Đang khởi tạo trình duyệt..."
- "Tải lại (Refresh)" button Click=`BtnRefresh_Click`
- `WvBrowser` SourceChanged=`WvBrowser_SourceChanged` — real WebView2 control

---

## Total count

- **Header buttons:** 3 (License, Update, Theme)
- **Tab 1 (Tasks):** 3 main buttons + 6 step checkboxes + 3 filter TextBoxes + 6 failure checkboxes + 1 clear button + DataGrid (~10 columns) + 4 per-row action buttons + 1 sidebar + 1 log RichTextBox = **~35 controls**
- **Tab 2 (Pool):** 6 metric TextBlocks + 1 refresh button + DataGrid (~8 columns) = **~16 controls**
- **Tab 3 (Profiles):** 1 ListBox + 4 buttons + 2 TextBoxes + 2 buttons = **~9 controls**
- **Tab 4 (Settings):** 3 PasswordBoxes + 4 TextBoxes + 4 directory rows (TextBox+Browse) + 1 concurrency TextBox + 1 proxies row + 1 manual proxies TextBox + 4 main buttons = **~22 controls**
- **Tab 5 (History):** 1 ListBox + 3 filter TextBoxes + 6 failure checkboxes + 1 clear button + DataGrid (~6 columns + 2 per-row buttons) = **~20 controls**
- **Tab 6 (Batch):** 2-panel: dashboard (header card + items control) + editor (3 sections, 5 ComboBoxes, 16 RadioButtons, 4 TextBoxes, 12 buttons, 1 Expander) + card grid + fallback DataGrid = **~50 controls**
- **Tab 7 (Gemini):** 6 toolbar buttons + DataGrid (7 columns + 2 per-row buttons) + RowDetails template (4 inner panels + 5 ComboBoxes + 2 TextBoxes + 2 buttons + 1 CheckBox) + Python log panel (4 controls) + Status bar (2 controls) + 5-Expander sidebar (5 Expanders + 5 badges + 5 RichTextBox) = **~55 controls**
- **Dialogs:** 8 dialogs with total ~30 controls

**Grand total: ~140 named controls + ~80 event handlers across the WPF UI**