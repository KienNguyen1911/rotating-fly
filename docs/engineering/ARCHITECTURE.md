# Kiến trúc hiện tại

## Tổng quan

AssetAutomator là modular monolith gồm bốn project C# và hai nhóm module Python tích hợp.

```text
AssetAutomator.WinUI
  -> AssetAutomator.Application
  -> AssetAutomator.Infrastructure
  -> AssetAutomator.Core

AssetAutomator.Application
  -> AssetAutomator.Infrastructure
  -> AssetAutomator.Core

AssetAutomator.Infrastructure
  -> AssetAutomator.Core
```

## Các layer

### Core

Chứa model, interface, constant và enum dùng chung. Core không tham chiếu project nội bộ khác.

Điểm cần lưu ý: `AppSettings` hiện trộn cấu hình thông thường với credential; `AutomationTask` và `GeminiTaskModel` có phần dữ liệu trùng nhau.

### Infrastructure

Chứa triển khai kỹ thuật:

- Config persistence trong AppData.
- Logging.
- Browser service.
- Helper OS/network/proxy/YouTube.
- Python và Google Flow server launcher.

### Application

Chứa use case và orchestration:

- Pipeline steps.
- `PipelineOrchestrator`.
- Gemini, voiceover, image generation, history, license, update.
- Strategy provider cho image generation.

Application hiện tham chiếu Infrastructure trực tiếp. Đây là modular layering thực dụng, chưa phải Clean Architecture nghiêm ngặt.

### WinUI

Là composition root và presentation layer:

- Đăng ký DI trong `App.xaml.cs`.
- Navigation shell trong `MainWindow`.
- Page, dialog, ViewModel, converter và style.
- Điều phối UI-thread cho log và trạng thái task.

## Pipeline Gemini

Mỗi task đi tuần tự qua bốn stage, nhiều task chạy xen kẽ:

```text
Task N
  -> Research/Transcript
  -> Voiceover/SRT
  -> Scene Breakdown
  -> Scene Image Generation
```

Mỗi stage có semaphore riêng. Cách này giới hạn tải theo loại tài nguyên thay vì giới hạn chung cho cả pipeline.

## Runtime ngoài process

WinUI giao tiếp với:

- Gemini API local mặc định port 8000.
- Google Flow Local mặc định port 8787.
- G-Labs webhook theo cấu hình.
- AI84, subtitle API, license server và các endpoint public khác.
- Chrome/Chromium qua Playwright.

Python runtime/source có thể được bundle vào output hoặc cài trong lần chạy đầu.

## Persistence

- Settings: `%APPDATA%\AssetAutomator\appsettings.json`.
- Outputs: theo `OutputsDir`/project storage được cấu hình.
- History, project và image pool: file-based service trong app.
- Cookie/profile/browser data: các thư mục/module tích hợp riêng.

## Quy tắc kiến trúc đề xuất

1. UI chỉ gọi use case/service Application, không chứa HTTP/process/file workflow dài.
2. Application phụ thuộc abstraction cho HTTP, clock, browser và filesystem ở các vùng cần test.
3. Credential không đặt default thật trong Core và không log giá trị đầy đủ.
4. Pipeline step dài phải nhận `CancellationToken`.
5. Trạng thái stage dùng một enum/model thống nhất.
6. Module Python được xem như external process có health contract, version và timeout rõ ràng.
7. Mỗi thay đổi dependency direction phải cập nhật tài liệu này.
