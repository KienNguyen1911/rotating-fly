# Nguyên tắc Phát triển & Cấu trúc Code (Developer Rules)

Tài liệu này đóng vai trò hướng dẫn phát triển cho các AI assistant (Gemini, Antigravity) và lập trình viên khi tiếp tục làm việc trên dự án `AssetAutomator`.

---

## 1. Cấu trúc Kiến trúc (Clean Architecture)

Dự án áp dụng mô hình **Clean Architecture đơn giản** để tách biệt tầng giao diện UI và tầng nghiệp vụ Logic:

* **Tuyệt đối không viết thêm business logic trực tiếp vào code-behind** của WPF Windows (như `MainWindow.xaml.cs` hoặc các partial files). Code-behind chỉ đóng vai trò thu thập dữ liệu nhập từ giao diện và chuyển tiếp xử lý cho Service tương ứng.
* **Quy chuẩn Folder cấu trúc**:
  * `Windows/`: Chỉ chứa WPF UI (XAML & Interaction logic thuần UI).
  * `Services/`: Chứa nghiệp vụ xử lý chính. Nếu một quy trình có nhiều bước (ví dụ Pipeline chạy các Steps), chia nhỏ mỗi Step thành một class riêng nằm ở `Services/Steps/` tuân thủ nguyên lý Single Responsibility (SRP).
  * `Models/`: Chỉ chứa cấu trúc dữ liệu, không chứa logic xử lý phức tạp.
  * `Helpers/`: Chứa các hàm tĩnh phi trạng thái (stateless utilities) có thể dùng chung mọi nơi.
  * `Converters/`: Chứa các bộ định dạng XAML Binding.

---

## 2. Quy chuẩn Chất lượng Code (SOLID & DRY)

* **Single Responsibility Principle (SRP)**:
  * Mỗi Service hoặc Step Class chỉ làm duy nhất một công việc. Ví dụ: `BrowserService` chỉ lo quản lý Chrome/Playwright; `HistoryService` chỉ làm nhiệm vụ ghi/tải lịch sử; `ChatGptService` chỉ lo tương tác giao diện ChatGPT.
* **Don't Repeat Yourself (DRY)**:
  * Tránh sao chép code giả lập giao diện hoặc xử lý chuỗi.
  * Các chuỗi HTML/JS chèn vào Playwright (như Drag-and-Drop) hoặc các biểu thức Regex (như Extract Video ID) bắt buộc phải gom vào một Service hoặc Helper dùng chung (`ChatGptService`, `YoutubeHelper`).
* **Dependency Injection (DIP)**:
  * Các Service hỗ trợ (`BrowserService`, `HistoryService`, `ChatGptService`) được khởi tạo một lần ở MainWindow và inject qua tham số/constructor khi gọi các Step Class.

---

## 3. Quy tắc Quản lý Tài nguyên (Resource Management)

* **Playwright Browser Contexts**:
  * Luôn đảm bảo đóng các trình duyệt ảo Playwright bằng phương thức an toàn `CloseBrowserSafelyAsync` (có Timeout bảo vệ tránh treo luồng) trong khối `finally`.
  * Luôn dọn dẹp các thư mục profile tạm thời (`TempProfile_`) sau khi luồng chạy kết thúc để tối ưu ổ đĩa.
* **Thread-Safety**:
  * Các tác vụ ghi file chung (như History) hoặc quản lý slot trình duyệt mở đồng thời bắt buộc phải điều phối thông qua cơ chế khóa (`SemaphoreSlim` hoặc lock object).
  * Luôn chạy các tác vụ chạy nền nặng bằng `Task.Run` kết hợp với dispatcher cập nhật giao diện `Dispatcher.BeginInvoke`.

---

## 4. Quy trình Cải tiến & Kiểm thử

* Khi AI Assistant nhận yêu cầu cải tiến tính năng mới:
  1. Đọc kỹ file [README.md](README.md) để nắm rõ cách phân bổ thư mục hiện tại.
  2. Bắt đầu bằng việc viết/cập nhật Model hoặc Service tương ứng, tuyệt đối không sửa trực tiếp vào luồng chính trước khi có Class tách biệt.
  3. Cập nhật các helper tĩnh nếu có logic trùng lặp xuất hiện.
  4. Build kiểm thử bằng lệnh `dotnet build` và xử lý triệt để tất cả Warning (nếu có).
