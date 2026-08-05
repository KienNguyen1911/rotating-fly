# AssetAutomator — Hướng dẫn build, chạy và vận hành

> Cập nhật theo mã nguồn ngày 05/08/2026. Ứng dụng chính là **WinUI 3**, project `src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj`.

## 1. Yêu cầu

- Windows 10/11 x64.
- .NET 10 SDK.
- Windows App Runtime và WebView2 Runtime tương thích WinUI project.
- PowerShell 7 (`pwsh`) được ưu tiên; Windows PowerShell là fallback cho setup script.
- Kết nối mạng ở lần đầu nếu Python embedded/Chromium chưa được bundle.
- Chrome/Chromium và account/session hợp lệ cho các flow dùng browser automation.

Kiểm tra SDK:

```powershell
dotnet --info
dotnet --list-sdks
```

## 2. Restore và build

Tại repository root:

```powershell
dotnet restore AssetAutomator.sln
dotnet build src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
```

WinUI project khai báo platform x64; nên truyền `-p:Platform=x64` khi build/run CLI.

Build cả solution:

```powershell
dotnet build AssetAutomator.sln -c Debug -p:Platform=x64
```

Không coi warning là “bình thường” một cách mặc định. Ghi nhận warning hiện tại, phân loại warning mới/cũ và xử lý warning liên quan nullable, package vulnerability hoặc XAML binding trước release.

## 3. Chạy app

```powershell
dotnet run --project src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
```

Nếu đã build:

```powershell
.\src\AssetAutomator.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\AssetAutomator.WinUI.exe
```

Đường dẫn output có thể khác theo SDK/cấu hình. Nếu không thấy file, kiểm tra output được in bởi `dotnet build` thay vì giả định thư mục.

## 4. Kiểm tra startup

Sau khi chạy, xác nhận:

- Cửa sổ `AssetAutomator` mở được.
- Navigation có Automation Tasks, Image Pool, Chrome Profiles, Gemini AI Creator, Batch Image Gen, History và Settings.
- Flow Local indicator chuyển khỏi trạng thái “đang kiểm tra”.
- Settings page đọc/lưu được cấu hình.
- Không có exception mới trong debugger/log.

Đây là smoke test startup, không chứng minh các API/provider ngoài đang hoạt động.

## 5. Python embedded và Flow Local

Khi auto-launch được bật, app kiểm tra:

- `tools/PythonEmbed/python.exe`.
- `tools/PythonEmbed/.installed-marker`.
- `tools/Scripts/Setup-PythonEmbed.ps1`.
- Google Flow source trong `tools/PythonSource/`.

Nếu thiếu runtime, app có thể chạy setup script. Có thể chuẩn bị thủ công:

```powershell
pwsh -File tools/Scripts/Setup-PythonEmbed.ps1
```

Sau setup, chạy lại app và kiểm tra endpoint mặc định:

```powershell
Invoke-RestMethod http://127.0.0.1:8787/health
```

Cấu hình liên quan:

- `GoogleFlow2RootPath`
- `GoogleFlow2Port`
- `GoogleFlow2AutoLaunch`
- `ImageApiUrl`
- `ImageApiKey`

`ImageApiKey=flow-local-key` là token nội bộ mặc định của server local trong thiết kế hiện tại; không phải Google API key. Nếu expose server ra ngoài loopback, phải thay cơ chế auth phù hợp.

## 6. Cấu hình ứng dụng

Settings được lưu ở:

```text
%APPDATA%\AssetAutomator\appsettings.json
```

Các nhóm cấu hình chính:

- AI84/API key và image provider credentials.
- Gemini API base URL.
- Image API URL/key.
- Subtitle API và license server URL.
- Output/project/profile directories.
- Proxy file paths.
- Gem IDs và provider mặc định.

Các environment override hiện có trong `ConfigService`:

- `AI84_API_KEY`
- `IMAGE_API_URL`
- `IMAGE_API_KEY`

Lưu ý: config hiện serialize credential plain text. Không chia sẻ file này; xem [Hạn chế](./docs/product/LIMITATIONS.md).

## 7. Chạy workflow Gemini

Trước khi chạy batch lớn:

1. Mở Settings và kiểm tra AI84, Gemini, Flow Local/output path.
2. Kiểm tra Flow Local `/health` nếu dùng provider này.
3. Tạo một task với topic hoặc URL.
4. Chọn voice, language, gem/model và image provider.
5. Chạy một task trước để xác nhận output.
6. Sau đó mới tăng số task batch.

Artifact chính:

```text
<output-task>/
  transcript.txt
  voiceover.mp3 hoặc voiceover.wav
  voiceover.srt
  scenes.json
  img/
    <scene-id>.png
```

Pipeline có thể skip stage nếu artifact hợp lệ đã có trong đúng folder task.

## 8. Batch Image Gen

- Flow Local mặc định: `http://127.0.0.1:8787/v1`.
- G-Labs: URL/key theo Settings.
- Health endpoint Flow Local nằm ở root `/health`, không phải `/v1/health`.
- Ảnh tham chiếu có thể được upload một lần và tái sử dụng qua `reference_media_id`.

Khi lỗi:

- Xác nhận provider/URL/key.
- Kiểm tra server health.
- Kiểm tra output directory có quyền ghi.
- Thử một prompt và một ảnh trước khi chạy batch.

## 9. Publish x64

```powershell
dotnet publish src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:Platform=x64 `
  -p:WindowsPackageType=None `
  -p:PublishSingleFile=false
```

Trước khi phát hành:

- Chạy trên máy Windows sạch.
- Xác nhận Python/Chromium được bundle hoặc first-run setup hoạt động.
- Không đóng gói cookie, appsettings thật, log hoặc output test.
- Kiểm tra license/update endpoint theo environment release.
- Lưu dependency/version manifest của package.

## 10. Troubleshooting

### App không build hoặc XAML compiler fail

- Chạy restore project WinUI.
- Đảm bảo target/platform đúng.
- Đóng process app/Visual Studio nếu DLL đang bị lock.
- Build với verbosity cao và cô lập XAML vừa thay đổi.

```powershell
dotnet restore src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj
dotnet build src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64 -v:diag
```

### Flow Local không khởi động

- Kiểm tra marker và Python executable.
- Chạy setup script thủ công.
- Kiểm tra port 8787 có bị chiếm.
- Kiểm tra `GoogleFlow2RootPath` và log setup/server.

### Gemini không load gem hoặc scene

- Kiểm tra API local port 8000.
- Kiểm tra cookie/session/profile Chrome.
- Thử API stream trước Playwright.
- Xác nhận selector/browser flow chưa bị dịch vụ ngoài thay đổi.

### Cancel nhưng task chưa dừng ngay

Đây là hạn chế hiện tại: token chưa được truyền xuyên suốt mọi step. Không force kill app khi đang ghi output nếu chưa sao lưu; xem roadmap cancellation.

## 11. Tài liệu liên quan

- [Mục lục tài liệu](./docs/README.md)
- [Tính năng](./docs/product/FEATURES.md)
- [Hạn chế](./docs/product/LIMITATIONS.md)
- [Kiến trúc](./docs/engineering/ARCHITECTURE.md)
- [Code quality](./docs/engineering/CODE-QUALITY.md)
- [Roadmap](./docs/engineering/IMPROVEMENT-ROADMAP.md)
- [Flow API](./FLOW-API.md)
