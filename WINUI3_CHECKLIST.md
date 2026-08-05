# Checklist Tiến Độ Chuyển Đổi WinUI 3 — AssetAutomator

> Thư mục dự án: [`src/AssetAutomator.WinUI`](file:///d:/Dev/AssetAutomator/src/AssetAutomator.WinUI)  
> Cập nhật lần cuối: 31/07/2026

---

## 🟢 1. Phần Đã Hoàn Thành (Completed)

- [x] **Phase 1: Khởi tạo WinUI 3 Project**
  - [x] Tạo dự án `AssetAutomator.WinUI` với .NET 10 (`net10.0-windows10.0.26100.0`).
  - [x] Cấu hình Unpackaged App (`WindowsPackageType=None`).
  - [x] Cài đặt `Microsoft.WindowsAppSDK` (v1.7.250310001) và `Microsoft.Extensions.Hosting` (v9.0.0).
  - [x] Tham chiếu các tầng `AssetAutomator.Core`, `Infrastructure`, `Application`.
  - [x] Build thành công **0 Errors**.

- [x] **Phase 2: Khung Giao Diện Chính (App Shell)**
  - [x] Setup `App.xaml.cs` tích hợp `IHost` Dependency Injection container.
  - [x] Dựng `MainWindow.xaml` với hiệu ứng **Mica Backdrop** chuẩn Fluent Design.
  - [x] Thiết kế **Custom TitleBar** lồng vào hệ thống Windows.
  - [x] Thêm Toggle Switch chuyển đổi màu nền **Dark / Light mode**.
  - [x] Tạo `NavigationView` menu bên trái gồm 6 mục điều hướng chính.

- [x] **Phase 3: Dựng Layout Các Trang Chức Năng (UI Pages)**
  - [x] [`TasksPage.xaml`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/Views/Pages/TasksPage.xaml) — Bảng điều khiển tác vụ tự động hóa.
  - [x] [`PoolPage.xaml`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/Views/Pages/PoolPage.xaml) — Thư viện lưu trữ & quản lý ảnh.
  - [x] [`ProfilesPage.xaml`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/Views/Pages/ProfilesPage.xaml) — Bảng quản lý Chrome Profiles & Proxy.
  - [x] [`GeminiPage.xaml`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/Views/Pages/GeminiPage.xaml) — Trình tạo kịch bản/nội dung AI với Gemini.
  - [x] [`HistoryPage.xaml`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/Views/Pages/HistoryPage.xaml) — Nhật ký thực thi lịch sử.
  - [x] [`SettingsPage.xaml`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/Views/Pages/SettingsPage.xaml) — Cấu hình ứng dụng & API keys.

- [x] **Phase 4: MVVM Binding & ViewModels Layer (HOÀN THÀNH 100%)**
  - [x] Cài đặt `CommunityToolkit.Mvvm` (v8.4.2).
  - [x] Tạo toàn bộ 6 ViewModels tại [`AssetAutomator.WinUI/ViewModels`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/ViewModels):
    - [`TasksViewModel.cs`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/ViewModels/TasksViewModel.cs)
    - [`PoolViewModel.cs`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/ViewModels/PoolViewModel.cs)
    - [`ProfilesViewModel.cs`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/ViewModels/ProfilesViewModel.cs)
    - [`GeminiViewModel.cs`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/ViewModels/GeminiViewModel.cs)
    - [`HistoryViewModel.cs`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/ViewModels/HistoryViewModel.cs)
    - [`SettingsViewModel.cs`](file:///d:/Dev/AssetAutomator/AssetAutomator.WinUI/ViewModels/SettingsViewModel.cs)
  - [x] Đăng ký các ViewModels & Services trong `App.xaml.cs` (IHost Dependency Injection).
  - [x] Ràng buộc dữ liệu 2 chiều (`DataContext` & `{Binding ...}`) cho tất cả 6 màn hình XAML.
  - [x] Kiểm tra biên dịch: Build pass clean **0 Errors**.

---

## 🟡 2. Phần Cần Làm Tiếp Theo (To-Do Checklist)

- [x] **Tích hợp Business Logic Sâu hơn (Deep Automation Wiring) - HOÀN THÀNH 100%**
  - [x] Kết nối `TasksPage` & `TasksViewModel` với `PipelineOrchestrator` và `HistoryService`.
  - [x] Kết nối `ProfilesPage` & `ProfilesViewModel` với `BrowserService` (quét Chrome profile dir, mở trình duyệt thực tế & test Proxy HTTP async).
  - [x] Kết nối `GeminiPage` & `GeminiViewModel` với `ChatGptService` & `ILogService` stream real-time log event.
  - [x] Kết nối `HistoryPage` & `HistoryViewModel` với `HistoryService` đọc file JSON theo ngày trên đĩa.
  - [x] Kết nối `PoolPage` & `PoolViewModel` với `ImagePoolService` và quét ảnh xuất trên đĩa.
  - [x] Kết nối `SettingsPage` & `SettingsViewModel` đọc/ghi cấu hình thực tế qua `IConfigService`.

- [x] **Port Các Cửa Sổ & Dialog Phụ từ WPF - HOÀN THÀNH 100%**
  - [x] Port `BulkTaskWindow` → `BulkTaskDialog.xaml` (WinUI 3 ContentDialog nhập danh sách URL/Voice ID).
  - [x] Port `LicenseWindow` → `LicenseDialog.xaml` (WinUI 3 ContentDialog quản lý bản quyền & kích hoạt trực tuyến).
  - [x] Port `NewProjectWindow` → `NewProjectDialog.xaml` (WinUI 3 ContentDialog tạo dự án tự động hóa mới).
  - [x] Port `ProxyTestResultWindow` → `ProxyTestResultDialog.xaml` (WinUI 3 ContentDialog hiển thị bảng kết quả test Proxy).
  - [x] Port `ScenesViewerWindow` → `ScenesViewerDialog.xaml` (WinUI 3 ContentDialog xem chi tiết phân cảnh kịch bản).
  - [x] Port `UpdateWindow` → `UpdateDialog.xaml` (WinUI 3 ContentDialog kiểm tra & tải bản cập nhật tự động).
  - [x] Port `VoiceSelectorWindow` → `VoiceSelectorDialog.xaml` (WinUI 3 ContentDialog duyệt và chọn giọng đọc ElevenLabs).
  - [x] Port `WebViewLoginWindow` → `WebViewLoginWindow.xaml` (WinUI 3 Sub-Window tích hợp WebView2 đăng nhập trực tiếp). (dùng WebView2 cho WinUI 3)

### 🔹 Bước 3: Kiểm Thử & Đóng Gói (QA & Release)
- [ ] Test toàn bộ luồng Playwright browser automation trên WinUI 3.
- [ ] Xử lý ngoại lệ, async thread-safety với UI dispatching.
- [ ] Tạo script đóng gói ứng dụng (Publish Single Folder Executable).
