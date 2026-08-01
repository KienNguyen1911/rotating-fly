# AssetAutomator – Hướng dẫn chạy & vận hành

Tài liệu này mô tả cách dự án **AssetAutomator** hoạt động sau khi hoàn tất phase tái cấu trúc (P1–P4) + migrate UI, và cách build/run trên máy Windows.

> **Lưu ý:** Solution hiện có **2 UI song song** — WPF (`AssetAutomator.UI`) là UI chính ổn định, WinUI 3 (`AssetAutomator.WinUI`) là UI mới đang trong giai đoạn port. Xem §10 để biết cách build/run WinUI 3.

---

## 1. Tổng quan

**AssetAutomator** là ứng dụng **WPF + WinUI 3** (.NET 10) phục vụ cho quy trình tạo video tự động với sự hỗ trợ của nhiều AI providers (Gemini, ChatGPT, G-Labs, Flow Local, ElevenLabs…). Hiện tại solution chạy song song 2 UI:

| UI | Trạng thái | Mô tả |
| --- | --- | --- |
| **WPF** (`AssetAutomator.UI`) | ✅ Ổn định, dùng cho production | UI gốc, đã qua P1–P4 cleanup, đầy đủ theme, animation, tất cả tab/dialog chạy đúng. |
| **WinUI 3** (`AssetAutomator.WinUI`) | 🟡 Đang port (build pass, app chạy được) | UI thế hệ mới dùng Fluent Design + Mica backdrop. Một số dialog tạm thời là placeholder (xem §10.8). |

5 tab chính (cả 2 UI đều có, khác biệt về cách trình bày):

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
│   │   ├── Services/                     (BrowserService ở đây vì cần WPF)
│   │   └── AssetAutomator.Infrastructure.csproj
│   │
│   ├── AssetAutomator.Application/       ← Business logic + Pipeline orchestration
│   │   ├── Services/                     (GeminiApiService, GeminiCreatorService,
│   │   │                                  PipelineOrchestrator, HistoryService, LicenseService…)
│   │   ├── Steps/                        (Voiceover, SceneBreakdown, ImageGen, Transcript…)
│   │   └── AssetAutomator.Application.csproj
│   │
│   └── AssetAutomator.UI/                ← WPF UI (presentation layer)
│       ├── App.xaml(.cs)                 ← Composition root, DI bootstrap
│       ├── MainWindow.xaml(.cs)          ← Cửa sổ chính + 6 partial classes:
│       │                                  MainWindow.{Tasks,AutomationSteps,History,
│       │                                  BatchImageGen,GeminiCreator,Profiles}.cs
│       ├── ConfigService.cs              ← Static bridge → IConfigService (cho field initializers)
│       ├── Converters/                   (YoutubeUrlConverter, NodeStatusToBrushConverter…)
│       ├── Models/                       (ProxyTestResultItem, Nodes/…)
│       ├── Windows/                      (NewProjectWindow, LicenseWindow, BulkTaskWindow,
│       │                                  ScenesViewerWindow, ProxyTestResultWindow,
│       │                                  VoiceSelectorWindow, UpdateWindow, WebViewLoginWindow)
│       ├── Resources/                    (app_logo.png + Styles/{Colors,Metrics,Typography,
│       │                                  Buttons,Controls,Components,Themes/{Light,Dark}}.xaml)
│       └── AssetAutomator.UI.csproj
│
│   └── AssetAutomator.WinUI/             ← WinUI 3 UI (thế hệ mới, unpackaged)
│       ├── App.xaml(.cs)                 ← Composition root (Microsoft.Extensions.Hosting),
│       │                                  Mica backdrop, custom titlebar, NavigationView
│       ├── MainWindow.xaml(.cs)          ← Cửa sổ chính 1240×700 + 6 trang + 8 dialogs
│       ├── app.manifest                  ← PerMonitorV2 DPI awareness
│       ├── Package.appxmanifest          ← MSIX manifest (chỉ dùng khi packaged)
│       ├── Assets/                       (StoreLogo, Square44x44, SplashScreen…)
│       ├── Properties/
│       │   ├── launchSettings.json       ← VS debug profiles (Package / Unpackaged)
│       │   └── PublishProfiles/          ← win-x64 / win-x86 / win-arm64 pubxml
│       ├── Styles/                       (CardStyles.xaml — shared Fluent card style)
│       ├── ViewModels/                   (Tasks/Pool/Profiles/Gemini/History/SettingsViewModel)
│       ├── Views/
│       │   ├── Pages/                    (Tasks, Pool, Profiles, Gemini, History, Settings)
│       │   └── Dialogs/                  (ContentDialog: BulkTask, License, NewProject,
│       │                                  ProxyTestResult, ScenesViewer, Update,
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
WPF UI (AssetAutomator.UI)         WinUI 3 (AssetAutomator.WinUI)
        │                                   │
        └─────────────┬─────────────────────┘
                      ▼
            Application ──► Core
                  │
                  └────► Infrastructure ──► Core
                      ▲
                      │
            (WPF BrowserService dùng trực tiếp từ Infrastructure)
```

- **Core**: không tham chiếu project nào khác, chỉ chứa POCO + interfaces + constants.
- **Infrastructure**: tham chiếu Core. Cài implementations cho OS, HTTP, proxy, Python embedded.
- **Application**: tham chiếu Core + Infrastructure. Chứa business logic, pipeline steps.
- **UI**: tham chiếu cả 3. Composition root (App.xaml.cs) đăng ký DI ở đây.

---

## 3. Yêu cầu môi trường

| Thành phần | Yêu cầu |
| --- | --- |
| **OS** | Windows 10/11 (64-bit) – do dùng WPF + WebView2 |
| **.NET SDK** | .NET 10 SDK (preview) – tải từ https://dot.net |
| **WebView2 Runtime** | Đã cài sẵn trên Windows 10/11 (Edge) |
| **Chrome / Chromium** | Bắt buộc – phục vụ `BrowserService` (Playwright/puppeteer) |
| **Python Embedded** | Đặt ở `tools/PythonEmbed/` – app tự khởi động server Python khi cần |
| **RAM tối thiểu** | 8 GB |

Kiểm tra SDK:
```bash
dotnet --list-sdks
# cần thấy 10.0.x
```

---

## 4. Build

### Build cả solution (WPF + WinUI 3)
```bash
cd D:\Dev\AssetAutomator
dotnet build AssetAutomator.sln
```

Kết quả mong đợi: `Build succeeded. 0 Error(s)`. Một số **warning** (NU1902 AngleSharp, MVVMTK0045 AOT, CS0618 obsolete `GeminiVideoPipelineService` / `LegacyVideoPipelineService`) là bình thường – không ảnh hưởng chức năng.

### Build chỉ project UI (WPF)
```bash
dotnet build src/AssetAutomator.UI/AssetAutomator.UI.csproj
```

### Build chỉ project WinUI 3
```bash
dotnet build src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
```

> **Lưu ý WinUI 3:** luôn truyền `-p:Platform=x64` vì unpackaged WinUI 3 không hỗ trợ AnyCPU. Xem §10 để biết chi tiết.

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
- WPF: `src/AssetAutomator.UI/bin/Debug/net10.0-windows/AssetAutomator.UI.exe`
- WinUI 3: `src/AssetAutomator.WinUI/bin/x64/Debug/net10.0-windows10.0.26100.0/AssetAutomator.WinUI.exe`

---

## 5. Run

### 5.1. WPF UI (khuyến nghị dùng cho production hiện tại)

#### Cách 1: dotnet CLI
```bash
cd D:\Dev\AssetAutomator
dotnet run --project src/AssetAutomator.UI/AssetAutomator.UI.csproj
```

#### Cách 2: chạy exe đã build
```bash
src\AssetAutomator.UI\bin\Debug\net10.0-windows\AssetAutomator.UI.exe
```

#### Cách 3: Visual Studio
1. Mở `AssetAutomator.sln`
2. Set `AssetAutomator.UI` làm **Startup Project**
3. Nhấn **F5**

#### Khi nào thì coi như thành công?
- Cửa sổ WPF bật lên với **title "AssetAutomator"**, kích thước `1240×700`, icon "A" màu xanh.
- Tab mặc định là **Tab Tổng hợp**.
- Không có hộp thoại exception, không có `crash.log` được tạo ở thư mục exe.

#### File log
- `crash.log` ở thư mục exe khi có unhandled exception.
- Log UI realtime hiển thị ở phần dưới của MainWindow.

### 5.2. WinUI 3 UI (đang trong giai đoạn port, xem §10)

#### Cách 1: dotnet CLI
```bash
cd D:\Dev\AssetAutomator
dotnet run --project src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
```

#### Cách 2: chạy exe đã build
```bash
src\AssetAutomator.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\AssetAutomator.WinUI.exe
```

#### Cách 3: Visual Studio
1. Mở `AssetAutomator.sln`
2. Set `AssetAutomator.WinUI` làm **Startup Project**
3. Trong Configuration Manager chọn **Platform = x64**, **Configuration = Debug**
4. Trong launchSettings.json chọn profile **`AssetAutomator.WinUI (Unpackaged)`**
5. Nhấn **F5**

#### Khi nào thì coi như thành công?
- Cửa sổ WinUI 3 bật lên với **title "AssetAutomator"**, kích thước `1240×700`, có **Mica backdrop** (gradient xanh tối đặc trưng Fluent), custom titlebar (logo + toggle Dark/Light).
- NavigationView bên trái có 6 mục: **Automation Tasks / Image Pool / Chrome Profiles / Gemini AI Creator / History / Settings**.
- Trang mặc định là **Automation Tasks** với 4 card metrics (Tổng số Tasks / Đang chạy / Hoàn thành / Thất bại-Dừng), CommandBar, InfoBar trạng thái và ListView 2 task mẫu.

#### Yêu cầu runtime
- **Windows App Runtime 1.7** phải được cài trên máy (xem §10.2). App sẽ fail ngay khi mở với lỗi `Microsoft.WindowsAppRuntime.Bootstrap` nếu thiếu.

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

Chỉnh sửa trong UI: **Tab Tổng hợp** → Settings panel, hoặc sửa JSON trực tiếp khi app đang tắt.

---

## 7. Dependency Injection (DI)

UI dùng **Microsoft.Extensions.DependencyInjection** + **Microsoft.Extensions.Hosting**.

### Composition root: `src/AssetAutomator.UI/App.xaml.cs`
```csharp
_host = Host.CreateDefaultBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<Core.Interfaces.IConfigService, Infrastructure.Services.ConfigService>();
        services.AddSingleton<Core.Interfaces.ILogService, Infrastructure.Logging.LogService>();
        services.AddSingleton<Application.Services.HistoryService>();
        services.AddSingleton<Application.Services.LicenseService>();
        services.AddSingleton<Application.Services.ChatGptService>();
    })
    .Build();
```

### Bridge static
`MainWindow.BatchImageGen.cs` có field initializers kiểu:
```csharp
private readonly BatchProjectService _batchProjectService = new BatchProjectService(ConfigService.Instance);
```
Vì field initializers chạy trước constructor, cần bridge:
```csharp
// AssetAutomator.UI.ConfigService (static helper)
public static IConfigService Instance { get; private set; } = null!;
public static AppSettings CurrentSettings => Instance?.CurrentSettings ?? new AppSettings();
public static void SetProvider(IConfigService provider) { Instance = provider; }
```
`App.OnStartup` gọi:
```csharp
ConfigService.SetProvider(Services.GetRequiredService<IConfigService>());
MainWindow = new MainWindow();
MainWindow.Show();
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
4. Resolve ở MainWindow:
   ```csharp
   var svc = App.Services.GetRequiredService<IMyService>();
   ```

---

## 10. WinUI 3 Project (AssetAutomator.WinUI)

Project `src/AssetAutomator.WinUI` là bản port UI sang **WinUI 3** (Fluent Design) của Microsoft — UI thế hệ mới, thay thế dần WPF. Hiện tại build pass 0 errors, app chạy được với đầy đủ 6 trang + 8 dialog, nhưng một số chi tiết nhỏ (WebView2 OAuth, theme tùy chỉnh sâu, integration WPF-specific services) còn cần bổ sung.

### 10.1. Đặc điểm kỹ thuật

| Thành phần | Giá trị |
| --- | --- |
| UI framework | **WinUI 3** (Fluent Design) trên nền Windows App SDK |
| TFM | `net10.0-windows10.0.26100.0` |
| `TargetPlatformMinVersion` | `10.0.17763.0` |
| Runtime | **Unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`) — không cần MSIX |
| Platform build | `x64` (bắt buộc — unpackaged WinUI 3 không hỗ trợ AnyCPU) |
| Package chính | `Microsoft.WindowsAppSDK 1.7.250310001`, `CommunityToolkit.Mvvm 8.4.2`, `Microsoft.Extensions.Hosting 9.0.0` |
| DI container | `Microsoft.Extensions.Hosting.IHost` (giống WPF) |
| Theme | Fluent mặc định + **Mica backdrop** + custom titlebar (toggle Dark/Light runtime) |
| App shell | `NavigationView` 6 menu + `Frame` content |

### 10.2. Yêu cầu môi trường bổ sung (so với WPF)

| Thành phần | Yêu cầu |
| --- | --- |
| **Windows App Runtime 1.7** | Bắt buộc phải cài. Cài qua: `winget install Microsoft.WindowsAppRuntime.1.7` hoặc tải từ [aka.ms/windowsappsdk/1.7](https://aka.ms/windowsappsdk/1.7/latest/windowsappruntimeinstall-x64.exe) |
| **Windows 10 SDK 10.0.26100** | Reference assemblies do .NET SDK 10.0.302 cung cấp (cài sẵn khi cài .NET SDK) |
| **dotnet workload** | Không cần cài workload nào thêm |
| **Visual Studio** | VS 2022 17.10+ (hỗ trợ WinAppSDK 1.7) hoặc VS 2026 |

Kiểm tra nhanh runtime đã cài:
```powershell
Get-AppxPackage -Name "*WindowsAppRuntime*1.7*"
# phải thấy Microsoft.WindowsAppRuntime.1.7 với version >= 7000.785.x
```

### 10.3. Build

```bash
# 1. Đóng Visual Studio trước (tránh lock file trên .Infrastructure/.Application.dll)
cd D:\Dev\AssetAutomator

# 2. Build WinUI 3
dotnet build src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
```

Kết quả mong đợi:
```
Build succeeded.
    8 Warning(s)     ← toàn là MVVMTK0045 (AOT) từ CommunityToolkit.Mvvm 8.4.2, không blocking
    0 Error(s)
```

Output: `src/AssetAutomator.WinUI/bin/x64/Debug/net10.0-windows10.0.26100.0/AssetAutomator.WinUI.exe`

### 10.4. Run

```bash
# Cách 1: dotnet run
dotnet run --project src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64

# Cách 2: chạy exe trực tiếp
src\AssetAutomator.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\AssetAutomator.WinUI.exe
```

Visual Studio:
1. Set **AssetAutomator.WinUI** làm Startup Project.
2. Configuration = `Debug`, Platform = `x64`.
3. Trong dropdown run profile chọn **`AssetAutomator.WinUI (Unpackaged)`** (profile này lấy từ `Properties/launchSettings.json`).
4. Nhấn **F5**.

### 10.5. Cấu trúc thư mục

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
│   └── CardStyles.xaml            # Card background style (ThemeResource brushes)
├── ViewModels/                    # 6 ViewModel: Tasks, Pool, Profiles, Gemini, History, Settings
│                                  # dùng CommunityToolkit.Mvvm [ObservableProperty]/[RelayCommand]
├── Views/
│   ├── Pages/                     # 6 Page: Tasks, Pool, Profiles, Gemini, History, Settings
│   │                              # mỗi Page có file .xaml.cs kèm code-behind lấy VM qua App.Services
│   └── Dialogs/                   # 8 ContentDialog: BulkTask, License, NewProject,
│                                  # ProxyTestResult, ScenesViewer, Update, VoiceSelector, WebViewLogin
└── publish/                       # Output của dotnet publish (tạo khi publish)
```

### 10.6. Mapping control WPF → WinUI 3

Mapping các control chính đã dùng trong project (tham khảo khi port thêm UI từ WPF):

| WPF | WinUI 3 | Ghi chú |
| --- | --- | --- |
| `<DataGrid>` | `ListView` + `DataTemplate` | WinUI 3 không có DataGrid sẵn; dùng `CommunityToolkit.WinUI.UI.Controls.DataGrid` nếu cần grid columns. |
| `<TabControl>` | `NavigationView` + `Frame` (page-based) | Hiện tại WinUI 3 port dùng page-based navigation. |
| `<ComboBox>` | `ComboBox` (WinUI 3) | Cú pháp giống WPF, chỉ khác template. |
| `<Label>` | `TextBlock` | |
| `<Window Height/Width>` | `AppWindow.Resize()` trong code-behind | Xem `MainWindow.SetSize()`. |
| `<Style.Triggers>` | `VisualStateManager` | Khi port trigger WPF sang WinUI 3 phải viết lại. |
| `<RelativeSource AncestorType>` | `RelativeSource { Mode=FindAncestor, AncestorType=... }` | |
| `DropShadowEffect` | `Microsoft.UI.Xaml.Media.ThemeShadow` | |
| `OpenFileDialog` (WinForms) | `FileOpenPicker` (WinRT) | WinUI 3 không hỗ trợ WinForms. |

### 10.7. Publish (đóng gói thư mục chạy được)

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

> Self-contained = true sẽ bundle cả .NET runtime + Windows App Runtime bootstrap vào thư mục publish, người dùng cuối không cần cài thêm gì (trừ Edge WebView2 Runtime). Kích thước khoảng **150–200 MB**.

Có sẵn 3 publish profile trong `Properties/PublishProfiles/`:
- `win-x64.pubxml`
- `win-x86.pubxml`
- `win-arm64.pubxml`

Dùng trong VS: chuột phải project → **Publish** → chọn profile.

### 10.8. Known issues (đang còn)

| Vấn đề | Trạng thái | Workaround |
| --- | --- | --- |
| `<controls:WebView2>` trong `WebViewLoginDialog.xaml` không resolve được khi compile vì WinAppSDK 1.7 không include WebView2 trong default XAML namespace | Đã tạm thời thay bằng `<Border>` placeholder | Cần implement OAuth flow qua `Windows.System.Launcher.LaunchUriAsync` + protocol activation, hoặc upgrade lên WinAppSDK bản có sẵn WebView2 XAML. |
| Theme tokens (Colors, Light/Dark resources) của WPF chưa được port | Chưa làm | Hiện tại dùng Fluent mặc định + Mica. Có thể port sau. |
| `MVVMTK0045` AOT warnings từ CommunityToolkit.Mvvm 8.4.2 | Không blocking | Có thể fix sau bằng cách chuyển sang `partial property` syntax. |

---

## 11. Troubleshooting

| Triệu chứng | Nguyên nhân / Cách xử lý |
| --- | --- |
| App crash ngay khi mở, log `configService` null | Bridge `ConfigService.SetProvider(...)` chưa được gọi trước `new MainWindow()`. Kiểm tra `App.OnStartup`. |
| `Cannot locate resource 'resources/app_logo.png'` | File `src/AssetAutomator.UI/Resources/app_logo.png` bị xóa hoặc csproj mất `<Resource Include="Resources\app_logo.png" />`. |
| Tab Gemini không load gem list | Server Python chưa chạy. Kiểm tra `PythonServerManager.Default` & cổng 8000. |
| Ảnh lô lỗi "API key invalid" | Mở Settings → nhập lại API key cho provider tương ứng. |
| `CS0234 GemOptionItem ambiguous` | Không nên xảy ra sau P4; nếu vẫn gặp thì kiểm tra cả 2 file `Models/Nodes/GeminiGraphModels.cs` và `Core/Models/GemOptionItem.cs`. UI chỉ dùng Core version (đã alias). |
| AngleSharp warning `NU1902` | Cảnh báo transitive dependency; không ảnh hưởng runtime. Có thể bỏ qua. |
| **WinUI 3:** `NETSDK1140: 10.0.28000.0 is not a valid TargetPlatformVersion` | TFM trong csproj đang dùng `10.0.28000.0` nhưng .NET SDK 10.0.302 chỉ chấp nhận tối đa `10.0.26100.0`. Sửa `TargetFramework` thành `net10.0-windows10.0.26100.0`. |
| **WinUI 3:** `XamlCompiler.exe exited with code 1` (silent fail, không in lỗi ra console) | XamlCompiler không nói rõ file nào lỗi. Cách debug: tạm thời đặt `<Page Remove="..." />` cho từng nhóm XAML trong csproj để cô lập. Lỗi thường do: (a) dùng control không có trong WinAppSDK default XAML namespace (vd: `WebView2`); (b) `RootNamespace` trong csproj không khớp với `x:Class` trong XAML. |
| **WinUI 3:** app crash ngay khi mở với lỗi `Microsoft.WindowsAppRuntime.Bootstrap` not found | Windows App Runtime chưa được cài. Cài bằng `winget install Microsoft.WindowsAppRuntime.1.7` hoặc tải từ link trong §10.2. |
| **WinUI 3:** `dotnet build` cảnh báo `MSB3061: file used by another process` | VS đang mở và lock file DLL của Core/Infrastructure/Application. Đóng VS trước khi build CLI, hoặc build trong VS. |
| **WinUI 3:** lỗi `MSB4019: imported project not found` cho `Microsoft.WindowsAppSDK.targets` | NuGet chưa restore đúng. Chạy `dotnet restore src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj` trước. |

---

## 12. Tài liệu liên quan

- `WINUI3_PLAN.md` – kế hoạch port UI sang WinUI 3 (phase 1 skeleton + phase 2 port toàn bộ)
- `WINUI3_CHECKLIST.md` – checklist tiến độ port WinUI 3
- `improve-docs/INDEX.md` – danh sách phase cải tiến P1–P4
- `improve-docs/check-list.md` – checklist các đầu việc đã hoàn thành
- `improve-docs/p4-project-cleanup.md` – chi tiết tái cấu trúc P4 (tách Core/Infrastructure/Application/UI)

---

**Trạng thái**:
- ✅ WPF: Build pass (0 errors), app khởi động thành công với title `AssetAutomator`.
- ✅ WinUI 3: Build pass (0 errors, 8 AOT warnings), app khởi động thành công với Mica backdrop + NavigationView, hiện đang ở giai đoạn port thô (1 số dialog tạm thời là placeholder).
