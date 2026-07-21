# Quy chuẩn Code Quality & Architecture cho dự án AssetAutomator

Tập tin này chứa cấu hình tự động (agent rules) cho các phiên làm việc tiếp theo của AI Assistant.

---

## 1. Cấu trúc thư mục dự án
Tuân thủ cấu trúc thư mục phân cụm chức năng sau:
- **Windows/**: Chứa toàn bộ giao diện WPF (XAML & code-behind UI).
- **Services/**: Tầng nghiệp vụ xử lý chính. Mỗi step trong pipeline nghiệp vụ tự động phải tách thành class riêng trong `Services/Steps/`.
- **Models/**: Chứa các cấu trúc dữ liệu, cấu hình (AppSettings, Task models).
- **Helpers/**: Chứa hàm tĩnh phi trạng thái (stateless utilities) như `YoutubeHelper`, `HumanBehaviourHelper`.
- **Converters/**: Chứa định dạng hiển thị XAML binding.
- **Scripts/**: Chứa các kịch bản python chạy độc lập.

## 2. Nguyên tắc Code
- **SOLID & DRY**:
  - Tuyệt đối không viết business logic, Playwright automation hay HTTP requests trực tiếp trong code-behind của giao diện (`Windows/`).
  - Không sao chép các hàm xử lý chuỗi, Regex, Playwright script kéo thả. Tất cả phải đưa vào Services hoặc Helpers dùng chung.
  - Sử dụng DI cơ bản bằng cách chuyển tiếp các service chính (`BrowserService`, `HistoryService`, `ChatGptService`) từ MainWindow qua các step class.
- **Quản lý tài nguyên**:
  - Luôn bọc đóng Playwright context trong khối `finally` bằng `BrowserService.CloseBrowserSafelyAsync()`.
  - Dọn dẹp profile tạm sau khi chạy để tránh tràn đĩa.
  - Thread-safe: Sử dụng `SemaphoreSlim` khi thao tác I/O chung hoặc lịch sử.

## 3. Quy trình thực hiện
1. Luôn ưu tiên tạo mới/sửa đổi Service hoặc Model riêng trước khi liên kết với giao diện.
2. Tránh làm thay đổi cấu trúc namespace `AssetAutomator` để không ảnh hưởng XAML bindings.
3. Build dự án bằng `dotnet build` và kiểm tra 0 lỗi, 0 warnings sau mỗi thay đổi lớn.
