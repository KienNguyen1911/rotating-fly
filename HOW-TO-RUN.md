# AssetAutomator – Hướng dẫn chạy & vận hành

Tài liệu này mô tả cách dự án **AssetAutomator** hoạt động sau khi hoàn tất migrate từ WPF sang WinUI 3, và cách build/run trên máy Windows.

---

## 1. Tổng quan

**AssetAutomator** là ứng dụng **WinUI 3** (.NET 10) phục vụ cho quy trình tạo video tự động với sự hỗ trợ của nhiều AI providers (Gemini, ChatGPT, G-Labs, Flow Local, ElevenLabs…).

| UI | Trạng thái | Mô tả |
| --- | --- | --- |
| **WinUI 3** (`AssetAutomator.WinUI`) | ✅ Chính thức | UI thế hệ mới dùng Fluent Design + Mica backdrop |

5 tab chính:

| Tab | Chức năng |
| --- | --- |
| **Quản lý Tab** | Tab Tổng hợp – quản lý dự án (Task), điều phối pipeline cũ (`LegacyVideoPipelineService`) |
| **Kịch bản & Video** | Công cụ tái sử dụng script/video YouTube, rewrite bằng ChatGPT, voice-over, thumbnail |
| **Hình ảnh** | Tạo ảnh theo lô với nhiều provider (G-Labs webhook :8765, Flow Local :8787) |
| **Gemini Creator** | Đồ thị node kéo-thả, tạo video end-to-end từ Gemini (Topic → Voiceover → Scene → Image) |
| **Lịch sử** | Xem lại các task đã chạy |

---

## 2. Kiến trúc solution

```
AssetAutomator.sln
│
├── src/
│   ├── AssetAutomator.Core/              ← Domain models, interfaces, constants
│   │   ├── Constants/                    (AppConstants, AppLanguageList, TimingConstants…)
│   │   ├── Interfaces/                   (IConfigService, ILogService, IBrowserService…)
│   │   ├── Models/                       (AppSettings, BatchProjectModel, GeminiTaskModel…)
│   │   └── AssetAutomator.Core.csproj
│   │
│   ├── AssetAutomator.Infrastructure/    ← Triển khai kỹ thuật (I/O, network, OS helpers)
│   │   ├── Helpers/                      (ConfigService, PythonServerManager, YoutubeHelper…)
│   │   ├── Logging/                      (LogService)
│   │   ├── Services/                     (BrowserService)
│   │   └── AssetAutomator.Infrastructure.csproj
│   │
│   ├── AssetAutomator.Application/       ← Business logic + Pipeline orchestration
│   │   ├── Services/                     (GeminiApiService, GeminiCreatorService,
│   │   │                                  PipelineOrchestrator, HistoryService, LicenseService…)
│   │   ├── Steps/                        (Voiceover, SceneBreakdown, ImageGen, Transcript…)
│   │   └── AssetAutomator.Application.csproj
│   │
│   └── AssetAutomator.WinUI/             ← WinUI 3 UI (presentation layer)
│       ├── App.xaml(.cs)                 ← Composition root (Microsoft.Extensions.Hosting),
│       │                                  Mica backdrop, custom titlebar, NavigationView
│       ├── MainWindow.xaml(.cs)          ← Cửa sổ chính 1240×700 + 6 trang + 8 dialogs
│       ├── app.manifest                  ← PerMonitorV2 DPI awareness
│       ├── Package.appxmanifest          ← MSIX manifest (chỉ dùng khi packaged)
│       ├── Assets/                       (StoreLogo, Square44x44, SplashScreen…)
│       ├── Properties/
│       │   ├── launchSettings.json       ← VS debug profiles (Package / Unpackaged)
│       │   └── PublishProfiles/          ← win-x64 / win-x86 / win-arm64 pubxml
│       ├── Styles/                       (CardStyles, ButtonStyles, Theme, Theme.Light/Dark)
│       ├── Controls/                     (ResizableDragHandle)
│       ├── Converters/                   (NodeStatusToBrushConverter, InvertBool, AspectRatioHeight)
│       ├── ViewModels/                   (Tasks/Pool/Profiles/Gemini/History/Settings/SidebarViewModel)
│       ├── Views/
│       │   ├── Pages/                   (Tasks, Pool, Profiles, Gemini, History, Settings)
│       │   └── Dialogs/                 (ContentDialog: BulkTask, BulkTaskWizard, License,
│       │                                  NewProject, ProxyTestResult, ScenesViewer, Update,
│       │                                  VoiceSelector, WebViewLogin)
│       └── AssetAutomator.WinUI.csproj   ← TFM: net10.0-windows10.0.26100.0, UseWinUI=true
│
├── Resources/                            ← Logo gốc (.ico/.png) – tham chiếu bởi csproj
├── tools/                                ← (PythonEmbed, Scripts…) copy khi build UI
├── improve-docs/                         ← Tài liệu cải tiến P1–P4
├── AssetAutomator.csproj                 ← Project top-level (build cả solution nhanh)
└── HOW-TO-RUN.md                         ← File bạn đang đọc
```

### Layer dependencies

```
WinUI 3 (AssetAutomator.WinUI)
        │
        ▼
  Application ──► Core
        │
        └────► Infrastructure ──► Core
```

- **Core**: không tham chiếu project nào khác, chỉ chứa POCO + interfaces + constants.
- **Infrastructure**: tham chiếu Core. Cài implementations cho OS, HTTP, proxy, Python embedded.
- **Application**: tham chiếu Core + Infrastructure. Chứa business logic, pipeline steps.
- **WinUI**: tham chiếu cả 3. Composition root (App.xaml.cs) đăng ký DI ở đây.

---

## 3. Yêu cầu môi trường

| Thành phần | Yêu cầu |
| --- | --- |
| **OS** | Windows 10/11 (64-bit) – do dùng WinUI 3 + WebView2 |
| **.NET SDK** | .NET 10 SDK – tải từ https://dot.net |
| **WebView2 Runtime** | Đã cài sẵn trên Windows 10/11 (Edge) |
| **Chrome / Chromium** | Bắt buộc – phục vụ `BrowserService` (Playwright/puppeteer) |
| **Python Embedded** | Tự động cài bởi `tools/Scripts/Setup-PythonEmbed.ps1` (~10 MB Python + ~150 MB Chromium). App sẽ tự chạy script này ở lần đầu nếu thiếu. End-user **không cần cài Python system**. |
| **Windows App Runtime** | Windows App Runtime 1.7 – xem §3.1 |
| **RAM tối thiểu** | 8 GB |

Kiểm tra SDK:
```bash
dotnet --list-sdks
# cần thấy 10.0.x
```

### 3.1. Windows App Runtime 1.7

WinUI 3 yêu cầu Windows App Runtime 1.7 phải được cài trên máy.

Kiểm tra:
```powershell
Get-AppxPackage -Name "*WindowsAppRuntime*1.7*"
# phải thấy Microsoft.WindowsAppRuntime.1.7 với version >= 7000.785.x
```

Cài đặt nếu chưa có:
```powershell
winget install Microsoft.WindowsAppRuntime.1.7
```
Hoặc tải từ [aka.ms/windowsappsdk/1.7](https://aka.ms/windowsappsdk/1.7/latest/windowsappruntimeinstall-x64.exe).

---

## 4. Build

### Build cả solution
```bash
cd D:\Dev\AssetAutomator
dotnet build AssetAutomator.sln
```

Kết quả mong đợi: `Build succeeded. 0 Error(s)`. Một số **warning** (NU1902 AngleSharp, MVVMTK0045 AOT, CS860x nullable) là bình thường – không ảnh hưởng chức năng.

### Build chỉ project WinUI 3
```bash
dotnet build src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
```

> **Lưu ý:** Luôn truyền `-p:Platform=x64` vì unpackaged WinUI 3 không hỗ trợ AnyCPU.

### Clean rebuild (khi cache bị lệch)
```bash
dotnet clean AssetAutomator.sln
dotnet build AssetAutomator.sln
```

### Build release
```bash
dotnet build AssetAutomator.sln -c Release
```

Output:
- WinUI 3: `src/AssetAutomator.WinUI/bin/Debug/net10.0-windows10.0.26100.0/AssetAutomator.WinUI.exe`

---

## 5. Run

### 5.1. dotnet CLI
```bash
cd D:\Dev\AssetAutomator
dotnet run --project src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
```

### 5.2. Chạy exe đã build
```bash
src\AssetAutomator.WinUI\bin\Debug\net10.0-windows10.0.26100.0\AssetAutomator.WinUI.exe
```

### 5.3. Visual Studio
1. Mở `AssetAutomator.sln`
2. Set `AssetAutomator.WinUI` làm **Startup Project**
3. Trong Configuration Manager chọn **Platform = x64**, **Configuration = Debug**
4. Trong launchSettings.json chọn profile **`AssetAutomator.WinUI (Unpackaged)`**
5. Nhấn **F5**

### Khi nào thì coi như thành công?
- Cửa sổ WinUI 3 bật lên với **title "AssetAutomator"**, kích thước `1240×700`, có **Mica backdrop** (gradient xanh tối đặc trưng Fluent), custom titlebar (logo + toggle Dark/Light).
- NavigationView bên trái có 6 mục: **Automation Tasks / Image Pool / Chrome Profiles / Gemini AI Creator / History / Settings**.
- Trang mặc định là **Automation Tasks** với các card metrics và CommandBar.

### File log
- Log UI realtime hiển thị ở phần dưới của MainWindow.

---

## 6. Cấu hình (AppSettings)

App dùng JSON ở `%APPDATA%\AssetAutomator\settings.json` (hoặc đường dẫn tương đương trong `IConfigService`). Các key quan trọng:

| Key | Mô tả |
| --- | --- |
| `GeminiApiBaseUrl` | URL server Python (mặc định `http://localhost:8000`) |
| `G LabsApiKey` | API key cho G-Labs webhook |
| `FlowLocalApiKey` | API key cho Flow Local API |
| `SelectedScriptwriterGem` / `SelectedSceneCreatorGem` | ID model Gemini mặc định |
| `OpenAIApiKey` | API key cho ChatGPT rewrite |
| `ProxyList` | Danh sách proxy (mỗi dòng 1 proxy) |

Chỉnh sửa trong UI: **Settings** page, hoặc sửa JSON trực tiếp khi app đang tắt.

---

## 7. Dependency Injection (DI)

WinUI dùng **Microsoft.Extensions.DependencyInjection** + **Microsoft.Extensions.Hosting**.

### Composition root: `src/AssetAutomator.WinUI/App.xaml.cs`
```csharp
_host = Host.CreateDefaultBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<Core.Interfaces.IConfigService, Infrastructure.Helpers.ConfigService>();
        services.AddSingleton<Core.Interfaces.ILogService, Infrastructure.Logging.LogService>();
        services.AddSingleton<Application.Services.HistoryService>();
        services.AddSingleton<Application.Services.LicenseService>();
        services.AddSingleton<Application.Services.ChatGptService>();
    })
    .Build();
```

---

## 8. Pipeline chạy như thế nào?

### 8.1. Legacy YouTube pipeline (Tab Tổng hợp / Kịch bản & Video)
`LegacyVideoPipelineService` chạy tuần tự 5 step:
1. **TranscriptExtractionStep** – tải transcript YouTube (qua Python `yt-dlp`)
2. **ChatGptRewriteStep** – viết lại kịch bản tiếng Việt
3. **VoiceoverGenerationStep** – tạo voice-over (ElevenLabs)
4. **GeminiSceneBreakdownStep** – tách thành các scene
5. **SceneImageBatchStep** – sinh ảnh cho từng scene

### 8.2. Gemini Creator pipeline (Tab Gemini)
- **PipelineOrchestrator** (multi-task, 4-stage matrix với rate-limit) — chạy nhiều task song song, mỗi task đi qua 4 giai đoạn:
  1. **GeminiTopicResearchStep** – nghiên cứu chủ đề
  2. **VoiceoverGenerationStep** – tạo voice-over
  3. **GeminiPlaywrightSceneBreakdownStep** – tách scene (dùng Playwright headless)
  4. **SceneImageBatchStep** – sinh ảnh
- **GeminiVideoPipelineService** (legacy) – vẫn giữ cho tương thích ngược (đánh dấu `[Obsolete]`).

### 8.3. Hình ảnh lô (Tab Hình ảnh)
- `BatchImageGenService` gọi provider qua `IImageGenProvider` factory:
  - `FlowLocalImageGenProvider` (Flow Local API, port 8787)
  - `GlabsImageGenProvider` (G-Labs webhook, port 8765)
- `BatchProjectService` quản lý project lô (`BatchProjectModel`).

---

## 9. Mở rộng / Add service mới

1. Đăng ký interface trong `Core/Interfaces/`
2. Implement trong `Application/Services/` (hoặc `Infrastructure/Helpers/`)
3. Inject trong `App.xaml.cs`:
   ```csharp
   services.AddSingleton<IMyService, MyService>();
   ```
4. Resolve ở ViewModel:
   ```csharp
   var svc = App.Services.GetRequiredService<IMyService>();
   ```

---

## 10. Cấu trúc chi tiết WinUI 3

### 10.1. Đặc điểm kỹ thuật

| Thành phần | Giá trị |
| --- | --- |
| UI framework | **WinUI 3** (Fluent Design) trên nền Windows App SDK |
| TFM | `net10.0-windows10.0.26100.0` |
| `TargetPlatformMinVersion` | `10.0.17763.0` |
| Runtime | **Unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`) — không cần MSIX |
| Platform build | `x64` (bắt buộc — unpackaged WinUI 3 không hỗ trợ AnyCPU) |
| Package chính | `Microsoft.WindowsAppSDK 1.7.250310001`, `CommunityToolkit.Mvvm 8.4.2`, `Microsoft.Extensions.Hosting 9.0.0` |
| DI container | `Microsoft.Extensions.Hosting.IHost` |
| Theme | Fluent mặc định + **Mica backdrop** + custom titlebar (toggle Dark/Light runtime) |
| App shell | `NavigationView` 6 menu + `Frame` content |

### 10.2. Cấu trúc thư mục

```
src/AssetAutomator.WinUI/
├── AssetAutomator.WinUI.csproj    # TFM: net10.0-windows10.0.26100.0, UseWinUI=true, WindowsPackageType=None
├── App.xaml(.cs)                  # Composition root: IHost DI + Mica backdrop
├── MainWindow.xaml(.cs)           # 1240×700, custom titlebar, NavigationView, theme toggle
├── app.manifest                   # PerMonitorV2 DPI awareness
├── Package.appxmanifest           # MSIX manifest (chỉ dùng khi packaged, hiện tại không dùng)
├── Assets/                        # StoreLogo, Square44x44, SplashScreen…
├── Properties/
│   ├── launchSettings.json        # 2 profiles: "MsixPackage" và "Project (Unpackaged)"
│   └── PublishProfiles/           # win-x64 / win-x86 / win-arm64 pubxml
├── Styles/
│   ├── CardStyles.xaml            # Card background style (ThemeResource brushes)
│   ├── ButtonStyles.xaml          # Custom Button styles
│   ├── Theme.xaml                 # Semantic color tokens
│   └── Theme.Light.xaml / Theme.Dark.xaml  # Theme variants
├── Controls/
│   └── ResizableDragHandle.cs     # Custom drag handle control
├── Converters/
│   ├── NodeStatusToBrushConverter.cs
│   ├── InvertBoolConverter.cs
│   └── AspectRatioHeightConverter.cs
├── ViewModels/                    # 7 ViewModel: Tasks, Pool, Profiles, Gemini, History, Settings, Sidebar
│                                  # dùng CommunityToolkit.Mvvm [ObservableProperty]/[RelayCommand]
├── Views/
│   ├── Pages/                     # 6 Page: Tasks, Pool, Profiles, Gemini, History, Settings
│   │                              # mỗi Page có file .xaml.cs kèm code-behind lấy VM qua App.Services
│   └── Dialogs/                   # 9 ContentDialog: BulkTask, BulkTaskWizard, License,
│                                  # NewProject, ProxyTestResult, ScenesViewer, Update,
│                                  # VoiceSelector, WebViewLogin
└── publish/                       # Output của dotnet publish (tạo khi publish)
```

### 10.3. Mapping control WPF → WinUI 3

Mapping các control chính (tham khảo khi port thêm UI):

| WPF | WinUI 3 | Ghi chú |
| --- | --- | --- |
| `<DataGrid>` | `ListView` + `DataTemplate` | WinUI 3 không có DataGrid sẵn; dùng `CommunityToolkit.WinUI.UI.Controls.DataGrid` nếu cần grid columns. |
| `<TabControl>` | `NavigationView` + `Frame` (page-based) | Hiện tại WinUI 3 dùng page-based navigation. |
| `<ComboBox>` | `ComboBox` (WinUI 3) | Cú pháp giống WPF, chỉ khác template. |
| `<Label>` | `TextBlock` | |
| `<Window Height/Width>` | `AppWindow.Resize()` trong code-behind | Xem `MainWindow.SetSize()`. |
| `<Style.Triggers>` | `VisualStateManager` | Khi port trigger WPF sang WinUI 3 phải viết lại. |
| `<RelativeSource AncestorType>` | `RelativeSource { Mode=FindAncestor, AncestorType=... }` | |
| `DropShadowEffect` | `Microsoft.UI.Xaml.Media.ThemeShadow` | |
| `OpenFileDialog` (WinForms) | `FileOpenPicker` (WinRT) | WinUI 3 không hỗ trợ WinForms. |

### 10.4. Publish (đóng gói thư mục chạy được)

> **Bước 0 (chỉ trên dev machine)**: chạy Setup script 1 lần để có `tools/PythonEmbed/` + `tools/PythonSource/` sẵn sàng trước khi publish. End-user sẽ KHÔNG cần — app tự chạy script ở lần đầu nếu thiếu, nhưng nếu bundle sẵn thì lần đầu nhanh hơn nhiều.
>
> ```bash
> pwsh -File tools/Scripts/Setup-PythonEmbed.ps1
> ```
>
> Mất ~5-10 phút (tải Python Embedded 10 MB + pip install ~50 MB + Playwright Chromium 150 MB).

```bash
# Publish self-contained, win-x64
dotnet publish src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:PublishSingleFile=false
```

Output: `src/AssetAutomator.WinUI/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/`

> Self-contained = true sẽ bundle cả .NET runtime + Windows App Runtime bootstrap + `tools/PythonEmbed/` + `tools/PythonSource/` vào thư mục publish, người dùng cuối không cần cài thêm gì (trừ Edge WebView2 Runtime). Kích thước khoảng **200–300 MB** đã bao gồm Python.

Có sẵn 3 publish profile trong `Properties/PublishProfiles/`:
- `win-x64.pubxml`
- `win-x86.pubxml`
- `win-arm64.pubxml`

Dùng trong VS: chuột phải project → **Publish** → chọn profile.

### 10.5. Known issues (đang còn)

| Vấn đề | Trạng thái | Workaround |
| --- | --- | --- |
| `<controls:WebView2>` trong `WebViewLoginDialog.xaml` không resolve được khi compile vì WinAppSDK 1.7 không include WebView2 trong default XAML namespace | Đã tạm thời thay bằng `<Border>` placeholder | Cần implement OAuth flow qua `Windows.System.Launcher.LaunchUriAsync` + protocol activation, hoặc upgrade lên WinAppSDK bản có sẵn WebView2 XAML. |
| `MVVMTK0045` AOT warnings từ CommunityToolkit.Mvvm 8.4.2 | Không blocking | Có thể fix sau bằng cách chuyển sang `partial property` syntax. |
| Warnings về nullable reference types (CS860x) | Không blocking | Có thể fix sau bằng cách thêm null checks. |

---

## 11. Troubleshooting

| Triệu chứng | Nguyên nhân / Cách xử lý |
| --- | --- |
| Tab Gemini không load gem list | Server Python chưa chạy. Kiểm tra `PythonServerManager` & cổng 8000. |
| Ảnh lô lỗi "API key invalid" | Mở Settings → nhập lại API key cho provider tương ứng. |
| `CS0234 GemOptionItem ambiguous` | Kiểm tra cả 2 file `Models/Nodes/GeminiGraphModels.cs` và `Core/Models/GemOptionItem.cs`. Chỉ nên dùng Core version. |
| AngleSharp warning `NU1902` | Cảnh báo transitive dependency; không ảnh hưởng runtime. Có thể bỏ qua. |
| **`NETSDK1140: 10.0.28000.0 is not a valid TargetPlatformVersion`** | TFM trong csproj đang dùng version không hợp lệ. Sửa `TargetFramework` thành `net10.0-windows10.0.26100.0`. |
| **`XamlCompiler.exe exited with code 1`** (silent fail, không in lỗi ra console) | XamlCompiler không nói rõ file nào lỗi. Cách debug: tạm thời đặt `<Page Remove="..." />` cho từng nhóm XAML trong csproj để cô lập. Lỗi thường do: (a) dùng control không có trong WinAppSDK default XAML namespace (vd: `WebView2`); (b) `RootNamespace` trong csproj không khớp với `x:Class` trong XAML. |
| **App crash ngay khi mở với lỗi `Microsoft.WindowsAppRuntime.Bootstrap` not found** | Windows App Runtime chưa được cài. Cài bằng `winget install Microsoft.WindowsAppRuntime.1.7` hoặc tải từ link trong §3.1. |
| **Lỗi "Google Flow Local không khởi động được" / `tools/PythonEmbed/python.exe not found`** | App tự chạy `tools/Scripts/Setup-PythonEmbed.ps1` ở lần đầu nếu thiếu. Nếu auto-setup fail (do mạng/PowerShell chặn), chạy thủ công: `pwsh -File tools/Scripts/Setup-PythonEmbed.ps1`. Mất ~5-10 phút. Nếu vẫn fail, xem `tools/PythonEmbed/.installed-marker` — xóa file này để force setup lại. |
| **Lỗi `ModuleNotFoundError: No module named 'X'` khi Flow Local start** | Setup script chưa chạy xong hoặc thiếu dep. Xóa `tools/PythonEmbed/.installed-marker`, sau đó chạy lại `pwsh -File tools/Scripts/Setup-PythonEmbed.ps1 -Force`. |
| **`dotnet build` cảnh báo `MSB3061: file used by another process`** | VS đang mở và lock file DLL của Core/Infrastructure/Application. Đóng VS trước khi build CLI, hoặc build trong VS. |
| **Lỗi `MSB4019: imported project not found` cho `Microsoft.WindowsAppSDK.targets`** | NuGet chưa restore đúng. Chạy `dotnet restore src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj` trước. |

---

## 12. Tài liệu liên quan

- `WINUI3_PLAN.md` – kế hoạch port UI sang WinUI 3 (phase 1 skeleton + phase 2 port toàn bộ)
- `WINUI3_CHECKLIST.md` – checklist tiến độ port WinUI 3
- `improve-docs/INDEX.md` – danh sách phase cải tiến P1–P4
- `improve-docs/check-list.md` – checklist các đầu việc đã hoàn thành
- `improve-docs/p4-project-cleanup.md` – chi tiết tái cấu trúc P4 (tách Core/Infrastructure/Application/UI)

---

**Trạng thái**:
- ✅ WinUI 3: Build pass (0 errors, warnings về AOT nullable), app khởi động thành công với Mica backdrop + NavigationView.
- ✅ Migration: WPF UI đã được xóa hoàn toàn, chỉ còn WinUI 3.
