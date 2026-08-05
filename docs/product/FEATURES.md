# Tính năng đã triển khai

> Phạm vi: trạng thái mã nguồn được rà soát ngày 05/08/2026. “Đã triển khai” không đồng nghĩa mọi provider ngoài đều luôn khả dụng; xem [Hạn chế](./LIMITATIONS.md).

## 1. Shell WinUI 3

Ứng dụng dùng WinUI 3, chạy unpackaged trên Windows, có `NavigationView`, Mica backdrop và các trang:

- Automation Tasks
- Image Pool
- Chrome Profiles
- Gemini AI Creator
- Batch Image Gen
- History
- Settings

ViewModel được đăng ký qua DI trong `src/AssetAutomator.WinUI/App.xaml.cs`. `GeminiViewModel` dùng lifetime singleton để giữ danh sách task khi chuyển trang; các ViewModel còn lại chủ yếu transient.

## 2. Automation Tasks

Đã có UI và ViewModel cho quản lý task, bao gồm:

- Thêm task đơn hoặc danh sách task.
- Lọc task theo URL/title, ngôn ngữ và voice ID.
- Theo dõi trạng thái xử lý.
- Kết nối với `PipelineOrchestrator`, history, logging và config.

Các service pipeline cũ vẫn tồn tại nhưng được đánh dấu `[Obsolete]`; không nên dùng làm nền cho tính năng mới.

## 3. Gemini AI Creator

Pipeline chính vận hành theo mô hình assembly line bốn stage:

1. **Deep Research / Transcript**: tạo `transcript.txt` từ topic hoặc URL.
2. **Voiceover / Subtitle**: gọi AI84, tạo audio và SRT.
3. **Scene Creator**: tạo và kiểm tra `scenes.json`, hỗ trợ API stream hoặc Playwright.
4. **Image Generation**: kiểm tra Flow Local, sau đó sinh ảnh scene vào thư mục `img/`.

`PipelineOrchestrator` hỗ trợ:

- Chạy nhiều task đồng thời.
- Semaphore riêng cho từng stage.
- Giới hạn mặc định 2/3/4/1 cho Research/Voice/Scene/Image.
- Bỏ qua stage khi artifact hợp lệ đã tồn tại trong đúng thư mục task.
- Tổng hợp số task thành công/thất bại và thời gian chạy.
- Hủy giữa các stage bằng `CancellationToken`.

Đầu ra thực tế dùng tên `voiceover.mp3` hoặc `voiceover.wav`, `voiceover.srt`, `transcript.txt`, `scenes.json` và `img/*.png`. Tài liệu cũ ghi `audio.mp3`/`subtitles.srt` không còn chính xác.

## 4. Batch Image Generation

Đã triển khai Strategy Provider cho:

- **Flow Local**: API OpenAI-compatible mặc định tại `http://127.0.0.1:8787/v1`.
- **G-Labs**: provider webhook theo cấu hình.

Các khả năng chính:

- Tạo ảnh theo lô từ prompt.
- Chọn tỷ lệ, model và tùy chọn upscale.
- Dùng ảnh tham chiếu.
- Tái sử dụng `reference_media_id` theo mô hình 1-to-N.
- Tạo project Flow qua API.
- Kiểm tra endpoint `/health` trước khi chạy stage ảnh.
- Theo dõi trạng thái từng item và lưu ảnh ra output directory.

## 5. Flow Local tự khởi động

Khi app khởi động, hệ thống có thể:

- Kiểm tra Python embedded và marker cài đặt.
- Chạy `tools/Scripts/Setup-PythonEmbed.ps1` nếu runtime chưa sẵn sàng.
- Khởi động server `google-flow-2.0.0` qua `GoogleFlow2ServerLauncher`.
- Hiển thị trạng thái Flow Local ở góc dưới app.
- Hiển thị dialog và điều hướng đến Settings nếu startup thất bại.

Tính năng này có thể tắt bằng `GoogleFlow2AutoLaunch`.

## 6. Image Pool

Đã có service, ViewModel và page để:

- Quản lý các ảnh đã sinh/lưu.
- Tìm kiếm theo prompt hoặc tên file.
- Chọn và xóa ảnh.
- Làm nguồn dữ liệu cho các workflow liên quan đến ảnh.

## 7. Chrome Profiles và browser automation

Đã triển khai:

- Quản lý profile Chrome.
- Chọn profile mặc định và thư mục profile.
- Proxy theo profile.
- Browser automation thông qua Playwright/`BrowserService`.
- Đồng bộ cookie Gemini và hỗ trợ scene creation qua giao diện web.

Đây là nhóm tính năng nhạy với thay đổi DOM, session/cookie và chính sách của dịch vụ ngoài.

## 8. History

`HistoryService`, `HistoryViewModel` và History page đã được nối vào DI. Hệ thống lưu và hiển thị lịch sử task để người dùng xem lại kết quả đã chạy.

## 9. Settings

Settings page/ViewModel quản lý các nhóm cấu hình:

- API keys AI84, image provider và các dịch vụ khác.
- URL Gemini API, image API, subtitle API và license server.
- Thư mục output, project storage và Chrome profiles.
- Proxy files.
- Flow Local root path, port và auto-launch.
- Gem mặc định cho scriptwriter/scene creator.

Settings được serialize ở `%APPDATA%\AssetAutomator\appsettings.json`; một số biến môi trường có thể override cấu hình.

## 10. License và update

Mã nguồn có service/dialog cho:

- Kích hoạt và xác thực license theo endpoint cấu hình.
- Heartbeat và trạng thái license.
- Kiểm tra/cung cấp thông tin cập nhật ứng dụng.

Mức độ sẵn sàng thực tế phụ thuộc endpoint ngoài và quy trình release; chưa có automated end-to-end test chứng minh toàn bộ luồng production.

## 11. Tooling và module Python

Repository chứa:

- Module Gemini Web API bằng Python.
- Module Google Flow Local và API layer.
- Script cài Python embedded/Playwright.
- Script chẩn đoán và smoke test batch flow.
- Bộ test Python bên trong các module ngoài.

Các test module Python không thay thế unit/integration test cho phần C#/WinUI.
