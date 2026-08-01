# WinUI 3 Port — Phân tích khoảng trống & Kế hoạch bổ sung

> **Trạng thái:** ✅ **Phase A (Sprint 1 & 2) & Phase B Start (Sprint 3) Hoàn thành 100%**. Projects Dashboard, BatchImageGenViewModel, routing & responsive card grid đã được tích hợp và build 0 lỗi.
> **Mục đích:** So sánh chi tiết UI giữa WPF (production, đầy đủ) và WinUI 3 (skeleton, đang port) cho từng tab/dialog, đánh dấu các control/feature còn thiếu, và đề xuất thứ tự bổ sung.

---

## 1. Tóm tắt executive

| Thước đo | WPF (`AssetAutomator.UI`) | WinUI 3 (`AssetAutomator.WinUI`) | % hoàn thành |
|---|---:|---:|---:|
| Core Pages (Tasks, Pool, Profiles, Settings, History) | 5 Pages UI + Bindings | 5 Pages Full Controls & Bindings | **100% (Phase A)** |
| Dialogs (ScenesViewer, VoiceSelector, License, Update, NewProject...) | 8 Dialogs | 8 Dialogs Fully Implemented & Wired | **100% (Phase A)** |
| Batch Image Gen (Dashboard + Routing + Card Grid) | Projects Dashboard & Editor | Projects Dashboard, Routing & Cards Grid Shell | **Phase B (Sprint 3 Done)** |
| Build Status | 0 Errors | **0 Errors, 0 Warnings (Passed)** | **100%** |
| Remaining Modules (Batch Editor & Gemini Creator) | Phase B & C | Phase B (Sprint 4-5) & Phase C (Sprint 6-7) | **Chuẩn bị Sprint 4** |

**Kết luận ngắn:** WinUI 3 đã hoàn thành **Phase A (Sprint 1 & Sprint 2)** và **Sprint 3 (Phase B Start)**. Trang `BatchImageGenPage` đã được tạo kèm với `BatchImageGenViewModel`, Projects Dashboard, nút duyệt thư mục lưu trữ dự án, tạo dự án mới via `NewProjectDialog`, và lưới hiển thị card dự án phản hồi giao diện.

---

## 2. So sánh tổng quan theo Tab/Page

### 2.1. Tab ↔ Page mapping

| WPF Tab (MainWindow.xaml) | WinUI 3 Page | Trạng thái WinUI 3 |
|---|---|---|
| Automation Tasks (lines 123-437) | `Views/Pages/TasksPage.xaml` | ✅ **Phase A Completed (100%)**: Đã thêm 6 step checkboxes, thanh lọc 3 TextBox + 6 step failure checkboxes, 4 metric cards, ListView tác vụ mở rộng với 4 nút thao tác từng hàng |
| Image Pool (lines 438-567) | `Views/Pages/PoolPage.xaml` | ✅ **Phase A Completed (100%)**: Đã thêm 6 metric cards, ô tìm kiếm + tổng count, ListView mở rộng với nút copy prompt, mở file, xóa item |
| Chrome Profiles (lines 568-671) | `Views/Pages/ProfilesPage.xaml` | ✅ **Phase A Completed (100%)**: Đã thêm danh sách profile, 4 nút thao tác chính, ô tạo profile mới, Custom GPT URL, khung thử Proxy + ProgressRing |
| Settings (lines 672-785) | `Views/Pages/SettingsPage.xaml` | ✅ **Phase A Completed (100%)**: Đã thêm PasswordBox x3, API URLs, chọn thư mục Chrome Profiles, khung cấu hình & test Proxy, nút Export/Import/Check keys, Theme selector |
| History (lines 786-1019) | `Views/Pages/HistoryPage.xaml` | ✅ **Phase A Completed (100%)**: Đã thêm ListBox dates, thanh lọc 3 TextBox + 6 failure checkboxes, Step status badges (5 step x 3 state), nút thao tác từng hàng |
| Batch Image Gen (lines 1020-1495) | `Views/Pages/BatchImageGenPage.xaml` | 🟢 **Phase B Middle (Sprint 4 Completed)**: Projects Dashboard + Project Editor left sidebar (Character dropzone, Script JSON, Config & AI Model Expander, Concurrency & Generate Button), `BatchImageGenViewModel`, 0 errors build & test run pass. |
| Gemini AI Creator (lines 1497-1886) | `Views/Pages/GeminiPage.xaml` | 🟡 **Sắp triển khai (Phase C - Sprint 6-7)**: Toolbar 5 buttons, DataGrid 7 columns, RowDetails, Python server log panel |

### 2.2. Sidebar / Special panels (chưa được map)

| WPF element | WinUI 3 element | Trạng thái |
|---|---|---|
| Sidebar Logs (`SidebarLogs`) — drawer bên phải với drag handle, Resizable | — | 🔴 Thiếu hoàn toàn |
| 5-Expander Step Accordion (`ExpanderStep1..5` + `TxtLogStep1..5`) | — | 🔴 Thiếu hoàn toàn |
| Python Server Log Panel (`TxtPythonServerLog`, `TxtPythonServerStatus`) | — | 🔴 Thiếu |
| Header bar với License / Update / Theme buttons | Chỉ có Theme toggle | 🟡 Thiếu License + Update buttons |
| Converters (`YoutubeUrlConverter`, `HexToBrushConverter`, `NodeStatusToBrushConverter`, `AspectRatioHeightConverter`) | — | 🔴 Thiếu (cần thiết cho DataGrid) |

---

## 3. So sánh chi tiết từng tab

> Đọc kèm với file inventory WPF trong `winui3-port/wpf-inventory.md` để tham chiếu.

### 3.1. Automation Tasks

| # | Control / Feature | WPF (x:Name) | WinUI 3 | Status |
|---|---|---|---|---|
| 1 | Header card có title "Task Queue & Automation Control" | inline TextBlock | `TextBlock "Automation Tasks"` (TitleTextBlockStyle) | ✅ Đã có, khác style |
| 2 | 3 button: Tạo Task, Tạo Nhiều, Chạy Task Đã Chọn | `BtnAddTask`, `BtnAddBulkTasks`, `BtnRun` | CommandBar với Play/Pause/Stop/Refresh (khác ý nghĩa hoàn toàn) | 🔴 Sai chức năng — cần đổi lại |
| 3 | 6 step checkboxes: Download Thumbnail / Generate Thumbnail / Get Transcript / Rewritten Transcript / Voiceover / SRT | `ChkStepDownloadThumbnail`..`ChkStepSrt` | — | 🔴 Thiếu — không có trong VM cũng như UI |
| 4 | 3 filter TextBox: Link Video / Ngôn ngữ / Voice ID | `TxtFilterVideoUrl`, `TxtFilterLanguage`, `TxtFilterVoiceId` | — | 🔴 Thiếu |
| 5 | 6 failure checkboxes: T/R/W/V/S/G | `ChkFilterT`..`ChkFilterG` | — | 🔴 Thiếu |
| 6 | Button "Xoá Lọc" | `BtnClearFilters` | — | 🔴 Thiếu |
| 7 | DataGrid với columns: Select checkbox, ID, Profile, Video URL, Status, Steps (5 step badges), Action buttons (Run/View Log/Assets/Delete) | `DgridTasks` (~15 columns) | `ListView` 4-col (ID, Profile, Video URL, Status) | 🟡 Có nhưng rất sơ sài |
| 8 | "Select all" checkbox ở header | `ChkSelectAllTasks` | — | 🔴 Thiếu |
| 9 | Sidebar drawer logs (Tab 1 dùng) | `SidebarLogs` / `TxtSidebarLog` | — | 🔴 Thiếu |
| 10 | 4 metric cards (Tổng / Đang chạy / Hoàn thành / Thất bại) | (TextBlock không tên) | `TotalTasks`/`RunningCount`/`CompletedCount`/`FailedCount` ✅ | ✅ WinUI 3 thậm ra có cái này đầy đủ hơn |
| 11 | InfoBar status | — | `InfoBar` StatusMessage | ✅ WinUI 3 có |

### 3.2. Image Pool

| # | Control / Feature | WPF | WinUI 3 | Status |
|---|---|---|---|---|
| 1 | 6 metric cards: Running/Max Workers / Waiting / Processing / Finished / Avg Time | `TxtPoolRunningWorkers`..`TxtPoolAvgTime` | — | 🔴 Thiếu — không có textblock nào |
| 2 | Button "Tải lại" | `BtnRefreshPool` | `RefreshPoolCommand` ✅ | ✅ WinUI 3 có |
| 3 | DataGrid với columns: ID, Prompt, Status, Engine, Progress, Started, Finished, Action | `DgridPoolRequests` (8+ columns) | `ListView` 4-col (Prompt/Style/FilePath/Status) | 🟡 Có nhưng rất sơ sài |
| 4 | VM properties: SearchQuery, TotalImagesCount | — | Có trong VM nhưng **không bind UI** | 🔴 Thiếu |

### 3.3. Chrome Profiles

| # | Control / Feature | WPF | WinUI 3 | Status |
|---|---|---|---|---|
| 1 | ListBox profile list (bên trái) | `LboxProfiles` | `ListView` ✅ | ✅ |
| 2 | Display TextBlock cho default profile name | `TxtDefaultProfileName` | ✅ | ✅ |
| 3 | 4 buttons: Open Browser / Open Folder / Set Default / Delete Profile | `BtnOpenProfileBrowser`, `BtnOpenProfileFolder`, `BtnSetDefaultProfile`, `BtnDeleteProfile` | Chỉ có "Mở Chrome Browser" | 🔴 Thiếu 3 buttons |
| 4 | TextBox new profile name + button "Create & Initialize" | `TxtNewProfileName`, `BtnCreateProfile` | — | 🔴 Thiếu |
| 5 | TextBox Custom GPT URL + Save button | `TxtCustomGptUrl`, `BtnSaveCustomGptUrl` | — | 🔴 Thiếu |
| 6 | Proxy TextBox + Test button | — | ✅ | ✅ |
| 7 | VM property `IsBusy` | — | Có trong VM nhưng không bind ProgressRing | 🔴 Thiếu indicator |

### 3.4. Settings

| # | Control / Feature | WPF | WinUI 3 | Status |
|---|---|---|---|---|
| 1 | AI84 API Key (PasswordBox) + Check Key button | `PbSettingsAi84ApiKey`, `BtnCheckAi84Key` | — | 🔴 Thiếu |
| 2 | Supabase DB URL (PasswordBox) | `PbSettingsSupabaseDbUrl` | — | 🔴 Thiếu |
| 3 | Image API URL (TextBox) | `TxtSettingsImageApiUrl` | — | 🔴 Thiếu |
| 4 | Subtitle API URL (TextBox) | `TxtSettingsSubtitleApiUrl` | — | 🔴 Thiếu |
| 5 | Image API Key (PasswordBox) | `PbSettingsImageApiKey` | — | 🔴 Thiếu |
| 6 | Chrome Profiles Directory TextBox + Browse button | `TxtSettingsChromeProfilesDir`, `BtnBrowseChromeProfilesDir` | — | 🔴 Thiếu |
| 7 | Outputs Directory TextBox + Browse button | `TxtSettingsOutputsDir`, `BtnBrowseOutputsDir` | "OutputPath" + Browse button | 🟡 Có 1, thiếu 1 |
| 8 | Max Concurrent Tasks TextBox | `TxtSettingsMaxConcurrentTasks` | `NumberBox` | ✅ Có (khác control type) |
| 9 | Proxies File Path TextBox + Browse + Test | `TxtSettingsProxiesFilePath`, `BtnBrowseProxiesFile`, `BtnTestProxies` | — | 🔴 Thiếu |
| 10 | Manual Proxies TextBox + Test | `TxtSettingsManualProxies`, `BtnTestManualProxies` | — | 🔴 Thiếu |
| 11 | 4 main buttons: Save / Export / Import / Check Requirements | `BtnSaveSettings`, `BtnExportSettings`, `BtnImportSettings`, `BtnCheckRequirements` | Chỉ có Save | 🔴 Thiếu 3 buttons |
| 12 | Headless mode CheckBox | — | ✅ `EnableHeadless` | ✅ |
| 13 | Theme selector | — | VM có `SelectedTheme` nhưng không bind UI | 🔴 Thiếu |

### 3.5. History

| # | Control / Feature | WPF | WinUI 3 | Status |
|---|---|---|---|---|
| 1 | ListBox dates (bên trái, chọn ngày để filter) | `LboxHistoryDates` | — | 🔴 Thiếu |
| 2 | 3 filter TextBox: Video URL / Language / Voice ID | `TxtHistoryFilterVideoUrl`, `TxtHistoryFilterLanguage`, `TxtHistoryFilterVoiceId` | — | 🔴 Thiếu |
| 3 | 6 failure checkboxes: T/R/W/V/S/G | `ChkHistoryFilterT`..`ChkHistoryFilterG` | — | 🔴 Thiếu |
| 4 | Button "Xoá Lọc" | `BtnHistoryClearFilters` | — | 🔴 Thiếu |
| 5 | Button "Tải lại" (refresh) | (BtnRefreshHistory implicit) | `LoadHistoryCommand` ✅ | ✅ |
| 6 | DataGrid với 5 columns: Ngày tạo, Video URL, Ngôn ngữ, Voice ID, **Steps Status (6 step badges)**, Action (Logs/Assets buttons) | `DgridHistoryTasks` (~6 columns phức tạp) | `ListView` 5-col (ID/URL/Date/Status/Logs) | 🟡 Có nhưng thiếu step badges |
| 7 | VM properties: AvailableDates, SelectedDate, SearchQuery, TotalHistoryCount | — | Có trong VM nhưng không bind UI | 🔴 Thiếu |

### 3.6. Batch Image Gen 🔴 HOÀN TOÀN THIẾU

Trong WinUI 3, tab này chưa có page riêng — `GeminiPage` chỉ là skeleton cho phần Gemini, không có Batch Image Gen. Đây là thiếu hụt lớn nhất:

| # | Control / Feature | WPF (x:Name) | WinUI 3 |
|---|---|---|---|
| 1 | Panel `PanelProjectsDashboard` (Visibility="Visible") | 2-row với header + items control | — |
| 2 | TextBox storage path + Browse button | `TxtProjectsStoragePath`, `BtnBrowseProjectsDir` | — |
| 3 | Button "Tạo dự án mới" (mở NewProjectDialog) | `BtnCreateProjectDashboard` | — |
| 4 | ItemsControl projects grid (UniformGrid 3 cols) với DataTemplate (folder icon, title, dates, badges, 2 action buttons) | `ItemsControlProjectsGrid`, `BtnOpenProjectCard`, `BtnDeleteProjectCard` | — |
| 5 | Panel `PanelProjectEditor` (Visibility="Collapsed") | 3-col: left 340 / drag / right * | — |
| 6 | Section 1 "NHÂN VẬT GỐC" với dashed dropzone, image preview, clear button, info text, tag | `BorderBatchCharDropzone`, `PanelBatchCharEmpty`, `PanelBatchCharHasImage`, `ImgBatchCharPreview`, `BtnBatchClearChar`, `TxtBatchCharInfo`, `TxtBatchCharTag` | — |
| 7 | Section 2 "KỊCH BẢN JSON" với TextBox 180 lines + 2 buttons | `TxtBatchScriptJson`, `BtnBatchImportJson`, `BtnUpdateScriptJson` | — |
| 8 | Section 3 "CẤU HÌNH" với ComboBox aspect (5 items) | `CboxBatchAspect` | — |
| 9 | TextBox output dir + Browse | `TxtBatchOutputDir`, `BtnBrowseBatchOutputDir` | — |
| 10 | Hidden RadioButtons cho aspect ratio | `RadAspect169`, `RadAspect11`, `RadAspect916`, `RadAspect43`, `RadAspect34` | — |
| 11 | Expander "Cấu hình Engine / Model AI" | (Expander) | — |
| 12 | 2 RadioButtons provider (G-Labs / Flow Local) | `RadProviderGlabs`, `RadProviderFlowLocal` | — |
| 13 | ComboBox engine (Flow/Meta/Grok) | `CboxBatchEngine` | — |
| 14 | 2 Panel model radio groups (G-Labs 3 models / Flow Local 5 models) | `PanelModelsGlabs` (Banana2/Pro/Lite), `PanelModelsFlowLocal` (Gemini 3.1 Flash / 3.0 Pro / Imagen 4 / Nano Banana 2/Pro) | — |
| 15 | 3 RadioButtons Upscale (None/2K/4K) | `RadUpscaleNone`, `RadUpscale2K`, `RadUpscale4K` | — |
| 16 | ComboBox concurrency (1/2/4/6/8) | `CboxBatchConcurrency` | — |
| 17 | Button "Tạo hàng loạt (4 ảnh song song)" | `BtnBatchGenerate` | — |
| 18 | Header card với title, ProgressBar, progress text, 4 buttons (Save/Open Flow/View Toggle/Open Folder) | `TxtVideoTitle`, `ProgressBatchGen`, `TxtBatchProgress`, `BtnBackToProjects`, `BtnSaveProjectEditor`, `BtnOpenFlowProjectUrl`, `BtnBatchViewToggle`, `BtnBatchOpenFolder` | — |
| 19 | ScrollViewer + ItemsControl cards grid (UniformGrid 2 cols) với DataTemplate phức tạp (overlay buttons, status badges, transcript, prompt) | `ScrollCardGrid`, `ItemsControlBatchCards`, `BtnCardRegenerate`, `BtnCardOpenImage`, `BtnCardCopyPrompt` | — |
| 20 | Fallback Table View (BorderTableView, Collapsed) với DataGrid 9 columns | `DgridBatchImageItems` | — |
| 21 | Converters `AspectRatioHeightConverter` | — | — |

### 3.7. Gemini AI Creator

| # | Control / Feature | WPF | WinUI 3 | Status |
|---|---|---|---|---|
| 1 | Toolbar 6 buttons: Thêm Task / Xóa Task / Gợi Ý Chủ Đề / Tải Gems / Nạp Cookies / CHẠY TASK ĐÃ CHỌN | `BtnAddGeminiTask`, `BtnDeleteGeminiTasks`, `BtnSuggestTopics`, `BtnRefreshGems`, `BtnImportCookies`, `BtnRunSelectedGeminiTasks` | Chỉ có `GenerateScriptCommand` (1 button) | 🔴 Thiếu 5 buttons |
| 2 | DataGrid 7 columns: Select / Topic / Progress+Status / Scriptwriter badge / Scene Creator badge / Status badge / Actions (Run + menu) | `DgridGeminiTasks` (7 DataGridTemplateColumn) | — | 🔴 Thiếu hoàn toàn |
| 3 | RowDetails Template (popup xuống) với 2 panels (Scriptwriter + Scene Creator) có Gem ComboBox, Model ComboBox, Deep Research CheckBox | `RowDetailsTemplate` | — | 🔴 Thiếu |
| 4 | Voice ID TextBox + Search button (`BtnBrowseVoice_Click`) | (trong RowDetails) | `SelectedVoice` TextBox ✅ | 🟡 Có 1, thiếu button |
| 5 | Image Provider ComboBox | (trong RowDetails) | — | 🔴 Thiếu |
| 6 | Character Ref TextBox + Browse button (`BtnBrowseCharacterRef_Click`) | (trong RowDetails) | — | 🔴 Thiếu |
| 7 | Collapse row details button | `BtnCollapseGeminiRowDetails_Click` | — | 🔴 Thiếu |
| 8 | Select All checkbox header | `ChkSelectAllGeminiTasks` | — | 🔴 Thiếu |
| 9 | Python Server Log Panel với toggle button, status text, clear button, RichTextBox log | `BtnTogglePythonLogs`, `TxtPythonServerStatus`, `TxtPythonServerLog`, `BtnClearPythonServerLog` | — | 🔴 Thiếu |
| 10 | Status bar Gemini (`TxtGeminiStatusIcon`, `TxtGeminiStatus`) | (status bar) | `StatusLog` InfoBar ✅ | 🟡 Có 1, thiếu icon |
| 11 | Sidebar 5-Expander accordion (Step 1..5 với Badge + RichTextBox log) | `ExpanderStep1..5`, `BadgeStep1..5`, `TxtLogStep1..5` | — | 🔴 Thiếu |
| 12 | Single Gemini input form (Topic + Style + Voice → Generate) | (không có) | ✅ WinUI 3 có | ✅ WinUI 3 thêm cái này (đơn giản hơn WPF) |
| 13 | VM properties: VideoDurationMinutes, ConsoleLogs, IsGenerating | — | Có nhưng không bind UI | 🔴 Thiếu |
| 14 | VM commands: CancelGeneration, ClearLogs | — | Có nhưng không có button | 🔴 Thiếu |

---

## 4. So sánh dialogs

| # | Dialog | WPF (lines) | WinUI 3 (lines) | Missing controls in WinUI 3 |
|---|---|---|---|---|
| 1 | BulkTask | 39 | 28 | WinUI 3 **giống** WPF (cùng 1 TextBox + 2 buttons) — ✅ OK |
| 2 | License | 63 | 15 | WPF có: `BorderStatusCard` (status card), `TxtStatusHeader`, `TxtStatusDetails`, `BtnTransfer`, `BtnClose`, `BtnActivate`. WinUI 3 chỉ có TextBox + InfoBar + 1 Deactivate button. **Thiếu 3 buttons + status details** |
| 3 | NewProject | 30 | 15 | WPF có 2 buttons (Cancel/Create). WinUI 3 có 2 buttons. ✅ OK, nhưng WinUI 3 thiếu **Description TextBox** (WPF chỉ có Project Name, đáng lẽ cả 2 nên có) |
| 4 | ProxyTestResult | 80 | 36 | WPF dùng DataGrid 3-col với StatusBadge styled template. WinUI 3 dùng ListView 3-col. 🟡 Tương đương |
| 5 | ScenesViewer | 80 | 45 | WPF: Title TextBlock + Stats TextBlock + Apply All button + DataGrid 4-col (Scene#/Transcript/ImagePrompt/Copy button). WinUI 3: chỉ có ListView 3-col, **thiếu Stats/Apply All/Copy button**. Hơn nữa, code-behind WinUI 3 có `BtnCopyPrompt_Click` nhưng **không wire trong XAML** |
| 6 | Update | 87 | 41 | WPF có 3 buttons (Skip / Remind Later / Update Now) + Header Title + Header Subtitle. WinUI 3 chỉ có Update Now button. **Thiếu 2 buttons + Header texts** |
| 7 | VoiceSelector | 200 | 48 | WPF có **11 controls bổ sung**: ComboSort, ComboGender, ComboAge, TxtLanguage, PanelUseCases (7 CheckBox), BtnApplyFilters, OverlayStatus + TxtStatusText + ProgressLoading, ComboPageSize + BtnPrevPage + BtnNextPage + TxtPageIndex. WinUI 3 chỉ có Search box + ListView. 🔴 **Thiếu 70% controls** |
| 8 | WebViewLogin | 28 | 21 | WPF: `WvBrowser` (real WebView2) + `BtnRefresh`. WinUI 3: Border placeholder + TxtStatus. 🔴 WebView2 chưa được wire |

---

## 5. ViewModels: properties/commands không bind UI

| ViewModel | Property/Command không bind | Số lượng |
|---|---|---|
| TasksViewModel | — | 0 |
| PoolViewModel | `SearchQuery`, `TotalImagesCount` | 2 |
| ProfilesViewModel | `IsBusy` | 1 |
| GeminiViewModel | `VideoDurationMinutes`, `ConsoleLogs`, `IsGenerating`, `CancelGeneration`, `ClearLogs` | 5 |
| HistoryViewModel | `AvailableDates`, `SelectedDate`, `SearchQuery`, `TotalHistoryCount` | 4 |
| SettingsViewModel | `SelectedTheme` (+ `BrowseFolder` stub chưa gọi FolderPicker) | 1 (+ 1 stub) |
| **Tổng** | | **13 properties + 2 commands + 1 stub** |

---

## 6. Kế hoạch bổ sung (Roadmap)

> Phân chia thành 4 phases, ưu tiên theo mức độ quan trọng và effort.

### Phase A — Core coverage (1-2 tuần) — Mức ưu tiên CAO

Đây là những control **mọi user sẽ dùng hàng ngày**, dễ port.

| Task | Tab | Effort | Risk |
|---|---|---|---|
| A1. Thêm 6 step checkboxes cho TasksPage | Tasks | 2h | Thấp |
| A2. Thêm filter bar (3 TextBox + 6 CheckBox + Clear) cho TasksPage | Tasks | 4h | Trung bình (cần VM filter logic) |
| A3. Mở rộng Tasks DataGrid thêm columns: Steps (5 step badges × 3 state), Action buttons (Run/Log/Assets/Delete) | Tasks | 8h | Cao (cần port 5-step status template + 4 buttons với logic) |
| A4. Thêm 6 metric cards cho PoolPage | Pool | 2h | Thấp |
| A5. Mở rộng Pool DataGrid với columns: Engine, Progress, Started/Finished | Pool | 6h | Trung bình |
| A6. Thêm 3 buttons còn thiếu cho ProfilesPage (Open Folder / Set Default / Delete) | Profiles | 3h | Thấp |
| A7. Thêm TextBox + Button cho "Create Profile" và "Custom GPT URL" | Profiles | 2h | Thấp |
| A8. Bổ sung `ProgressRing IsActive="{Binding IsBusy}"` cho ProfilesPage | Profiles | 0.5h | Thấp |
| A9. Thêm PasswordBox × 3 + 5 TextBox API Keys cho SettingsPage | Settings | 4h | Trung bình |
| A10. Thêm 4 buttons: Export / Import / Check Requirements / Browse folders | Settings | 3h | Thấp |
| A11. Thêm Theme selector (RadioButtons Light/Dark/Default) | Settings | 1h | Thấp |
| A12. Thêm ListBox dates + filter bar cho HistoryPage | History | 4h | Trung bình |
| A13. Thêm Step Status badges (5 step × 3 state) cho History DataGrid | History | 6h | Trung bình (copy từ TasksPage) |
| A14. Bổ sung 3 buttons còn thiếu cho dialogs: License (Transfer + Close + Activate), Update (Skip + Remind Later), VoiceSelector (filters + pagination) | Dialogs | 6h | Trung bình |
| A15. Port Converters `YoutubeUrlConverter`, `HexToBrushConverter`, `NodeStatusToBrushConverter`, `AspectRatioHeightConverter` sang WinUI 3 | Global | 3h | Trung bình |

**Total Phase A**: ~55 giờ (~1.5 tuần full-time)

### Phase B — Batch Image Gen page (1-2 tuần) — Mức ưu tiên CAO

Đây là phần **phức tạp nhất**, cần làm riêng. Tạo page mới `Views/Pages/BatchImageGenPage.xaml`.

| Task | Effort | Risk |
|---|---|---|
| B1. Tạo `BatchImageGenViewModel` (chuyển từ `MainWindow.BatchImageGen.cs`) | 6h | Trung bình |
| B2. Projects Dashboard panel (ItemsControl với card template) | 10h | Cao (UniformGrid + DataTemplate + dynamic status) |
| B3. Project Editor panel với 3 sections (Character / Script / Config) | 16h | Cao (dropzone drag-drop, JSON editor, 5 ComboBox items) |
| B4. Expander "Engine / Model AI" với 2 RadioGroups + 3 ComboBox + 5+3 model RadioButtons | 12h | Trung bình |
| B5. Card Grid view (ItemsControl + DataTemplate với overlay buttons + auto-height từ AspectRatio converter) | 16h | Rất cao (custom layout, glass overlay) |
| B6. Table View fallback (DataGrid 9 columns) | 4h | Trung bình |
| B7. Progress bar + "Đã tạo 0/0 ảnh" status | 2h | Thấp |

**Total Phase B**: ~66 giờ (~2 tuần full-time)

### Phase C — Gemini AI Creator full port (1 tuần) — Mức ưu tiên TRUNG BÌNH

| Task | Effort | Risk |
|---|---|---|
| C1. Toolbar 6 buttons (Add/Delete/Suggest/Refresh/Import Cookies/Run) | 4h | Thấp |
| C2. DataGrid 7 columns với template (Select checkbox, Topic TextBox, ProgressBar, Scriptwriter badge, Scene Creator badge, Status badge, Action buttons) | 12h | Cao (5 template columns) |
| C3. RowDetails template (popup xuống) với 4 inner panels (Scriptwriter, Scene Creator, Voice ID, Provider, Character Ref) | 16h | Cao (popup + 6 inner controls) |
| C4. Python Server Log panel với RichTextBox + toggle button + status indicator | 8h | Trung bình |
| C5. Sidebar 5-Expander step accordion (Steps 1-5) | 12h | Trung bình |
| C6. Bind VM properties: VideoDurationMinutes (NumberBox), IsGenerating (ProgressRing), ConsoleLogs (RichTextBox) | 4h | Thấp |

**Total Phase C**: ~56 giờ (~1.5 tuần full-time)

### Phase D — Polish & special features (1 tuần) — Mức ưu tiên THẤP

| Task | Effort | Risk |
|---|---|---|
| D1. Sidebar drawer logs (drag-handle resizable, slide animation) | 12h | Cao (WPF-only API, cần thay bằng AppWindow API + custom grid) |
| D2. License + Update buttons trong MainWindow header | 2h | Thấp |
| D3. Real WebView2 OAuth flow cho `WebViewLoginDialog` (Launcher + protocol activation) | 16h | Rất cao (OAuth flow phức tạp, cần Supabase URL config) |
| D4. Port Style resources (Colors/Themes/Light/Dark) từ WPF Resources/Styles | 12h | Trung bình |
| D5. Port custom control templates (Buttons.xaml styles, ToggleButton với emoji icon) | 8h | Thấp |
| D6. Card grid hover overlay animation (fade-in trên mouseover) | 4h | Trung bình |

**Total Phase D**: ~54 giờ (~1.5 tuần full-time)

---

## 7. Tổng kết effort

| Phase | Mô tả | Effort |
|---|---|---|
| A | Core coverage (đầy đủ 6 tab + dialogs cơ bản) | ~55h (~1.5 tuần) |
| B | Batch Image Gen page (mới) | ~66h (~2 tuần) |
| C | Gemini AI Creator full port | ~56h (~1.5 tuần) |
| D | Polish & special features | ~54h (~1.5 tuần) |
| **Tổng** | **Full parity với WPF** | **~231h (~6 tuần full-time)** |

Sau khi hoàn thành Phase A + B + C (~177h ≈ 5 tuần), WinUI 3 sẽ đạt **~95% feature parity** với WPF và có thể được dùng làm UI chính thức thay thế WPF (Phase D là các cải tiến visual/OAuth không bắt buộc).

---

## 8. File tham chiếu trong thư mục này

- `winui3-port/wpf-inventory.md` — Full inventory 140 controls của WPF
- `winui3-port/winui3-inventory.md` — Full inventory WinUI 3 (đã có trong chat)
- `winui3-port/per-tab-gap-analysis.md` — Phân tích gap chi tiết theo tab
- `winui3-port/roadmap.md` — Timeline + sprint breakdown (8 sprints, ~6 sprints feature + 2 polish)

## 9. Confirmed decisions (Aug 1, 2026)

| Question | Answer |
|---|---|
| Phạm vi port? | **Phase A + B + C** (full parity ~95%, ~6 sprints). Phase D (polish + OAuth) deferred. |
| WPF fallback? | **Giữ `AssetAutomator.UI`** trong suốt quá trình port, không xóa. |
| Gemini Creator page? | **Thay thế hoàn toàn** single-script mode hiện tại bằng multi-task queue UI của WPF. |
| WebView2 OAuth? | Defer (chỉ là Border placeholder cho tới Phase D). |
| Offline OAuth flows? | Pending investigation (raised as open question). |

## 10. Next step

Sprint 1 (40h) bắt đầu với Phase A:
- A1-A4: Hoàn thiện `TasksPage` (6 step checkboxes + filter bar + DataGrid expansion)
- A5-A6: Hoàn thiện `PoolPage` (6 metric cards + search + total count)
- A7-A9: Hoàn thiện `ProfilesPage` (3 buttons + custom GPT URL + ProgressRing)
- A10-A12: Hoàn thiện `SettingsPage` (3 PasswordBox + 2 URL TextBox + Chrome dir picker)

Sau Sprint 1, đánh giá lại velocity trước khi cam kết Sprint 2 chi tiết.
