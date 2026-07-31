# AssetAutomator – Hướng dẫn chạy & vận hành

Tài liệu này mô tả cách dự án **AssetAutomator** hoạt động sau khi hoàn tất phase tái cấu trúc (P1–P4) + migrate UI, và cách build/run trên máy Windows.

---

## 1. Tổng quan

**AssetAutomator** là ứng dụng WPF (.NET 10) phục vụ cho quy trình tạo video tự động với sự hỗ trợ của nhiều AI providers (Gemini, ChatGPT, G-Labs, Flow Local, ElevenLabs…). Ứng dụng có 5 tab chính:

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
├── Resources/                            ← Logo gốc (.ico/.png) – tham chiếu bởi csproj
├── tools/                                ← (PythonEmbed, Scripts…) copy khi build UI
├── improve-docs/                         ← Tài liệu cải tiến P1–P4
├── AssetAutomator.csproj                 ← Project top-level (build cả solution nhanh)
└── HOW-TO-RUN.md                         ← File bạn đang đọc
```

### Layer dependencies

```
UI ──► Application ──► Core
 │           │
 │           └────► Infrastructure ──► Core
 └──► Infrastructure (BrowserService có WPF)
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

### Build cả solution
```bash
cd D:\Dev\AssetAutomator
dotnet build AssetAutomator.sln
```

Kết quả mong đợi: `Build succeeded. 0 Error(s)`. Một số **warning** (NU1902 AngleSharp, CS0618 obsolete `GeminiVideoPipelineService` / `LegacyVideoPipelineService`) là bình thường – không ảnh hưởng chức năng.

### Build chỉ project UI
```bash
dotnet build src/AssetAutomator.UI/AssetAutomator.UI.csproj
```

### Clean rebuild (khi cache bị lệch)
```bash
dotnet clean AssetAutomator.sln
dotnet build AssetAutomator.sln
```

### Build release
```bash
dotnet build AssetAutomator.sln -c Release
```

Output: `src/AssetAutomator.UI/bin/Debug/net10.0-windows/AssetAutomator.UI.exe`

---

## 5. Run

### Cách 1: dotnet CLI
```bash
cd D:\Dev\AssetAutomator
dotnet run --project src/AssetAutomator.UI/AssetAutomator.UI.csproj
```

### Cách 2: chạy exe đã build
```bash
src\AssetAutomator.UI\bin\Debug\net10.0-windows\AssetAutomator.UI.exe
```

### Cách 3: Visual Studio
1. Mở `AssetAutomator.sln`
2. Set `AssetAutomator.UI` làm **Startup Project**
3. Nhấn **F5**

### Khi nào thì coi như thành công?
- Cửa sổ WPF bật lên với **title "AssetAutomator"**, kích thước `1240×700`, icon "A" màu xanh.
- Tab mặc định là **Tab Tổng hợp**.
- Không có hộp thoại exception, không có `crash.log` được tạo ở thư mục exe.

### File log
- `crash.log` ở thư mục exe khi có unhandled exception.
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

## 10. Troubleshooting

| Triệu chứng | Nguyên nhân / Cách xử lý |
| --- | --- |
| App crash ngay khi mở, log `configService` null | Bridge `ConfigService.SetProvider(...)` chưa được gọi trước `new MainWindow()`. Kiểm tra `App.OnStartup`. |
| `Cannot locate resource 'resources/app_logo.png'` | File `src/AssetAutomator.UI/Resources/app_logo.png` bị xóa hoặc csproj mất `<Resource Include="Resources\app_logo.png" />`. |
| Tab Gemini không load gem list | Server Python chưa chạy. Kiểm tra `PythonServerManager.Default` & cổng 8000. |
| Tab Gemini log `UNUTHENTICATED` / `Unexpected response data structure: )]}'` / `500 Internal Server Error` từ `/api/gems` | `cookies.json` hết hạn (Google rotate session tokens mỗi ~24h). Vào tab Gemini → **Nhập Cookies** (Chrome profile có gemini.google.com đang đăng nhập) hoặc chạy Playwright login. Xem mục "Làm mới cookies" trong tab Gemini. |
| `Errno 10048 address already in use` khi restart server | Có process `python.exe` khác đang giữ cổng 8000. Đã fix tự động trong `PythonServerManager` (kill theo PID port-holder). Nếu orphan thuộc SYSTEM thì cần `taskkill /PID <pid> /T /F` với quyền Admin. |
| Ảnh lô lỗi "API key invalid" | Mở Settings → nhập lại API key cho provider tương ứng. |
| `CS0234 GemOptionItem ambiguous` | Không nên xảy ra sau P4; nếu vẫn gặp thì kiểm tra cả 2 file `Models/Nodes/GeminiGraphModels.cs` và `Core/Models/GemOptionItem.cs`. UI chỉ dùng Core version (đã alias). |
| AngleSharp warning `NU1902` | Cảnh báo transitive dependency; không ảnh hưởng runtime. Có thể bỏ qua. |

---

## 11. Tài liệu liên quan

- `improve-docs/INDEX.md` – danh sách phase cải tiến P1–P4
- `improve-docs/check-list.md` – checklist các đầu việc đã hoàn thành
- `improve-docs/p4-project-cleanup.md` – chi tiết tái cấu trúc P4 (tách Core/Infrastructure/Application/UI)

---

**Trạng thái**: ✅ Build pass (0 errors), ✅ App khởi động thành công với title `AssetAutomator`.
