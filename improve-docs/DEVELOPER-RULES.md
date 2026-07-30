# Nguyên tắc Phát triển & Cấu trúc Code (Developer Rules)

> Tài liệu này đóng vai trò hướng dẫn phát triển cho các AI assistant (Gemini, Cursor, Codex) và lập trình viên khi tiếp tục làm việc trên dự án `AssetAutomator`.

---

## 1. Cấu trúc Kiến trúc (Modular Monolith + DI)

Dự án áp dụng **Modular Monolith** chia thành 4 project (xem `../HOW-TO-RUN.md`):

| Project | Trách nhiệm |
|---|---|
| `AssetAutomator.Core` | Domain models, interfaces, constants. Không phụ thuộc project khác. |
| `AssetAutomator.Infrastructure` | Triển khai OS/network: ConfigService, PythonServerManager, YoutubeHelper, LogService. |
| `AssetAutomator.Application` | Business logic + pipeline steps. Phụ thuộc Core + Infrastructure. |
| `AssetAutomator.UI` | WPF presentation. Composition root đăng ký DI trong `App.xaml.cs`. |

### Quy tắc bắt buộc

- **Tuyệt đối không viết business logic trực tiếp vào code-behind** của WPF Windows. Code-behind chỉ thu thập input và chuyển tiếp cho Service/Step tương ứng.
- **Quy chuẩn folder cấu trúc (sau P4 migration)**:
  - `src/AssetAutomator.Core/{Constants,Interfaces,Models}/` – chỉ chứa POCO + interface.
  - `src/AssetAutomator.Infrastructure/{Helpers,Logging,Services}/` – OS/network utilities.
  - `src/AssetAutomator.Application/{Services,Steps,Services/Providers}/` – nghiệp vụ. Pipeline step tách class riêng theo SRP.
  - `src/AssetAutomator.UI/{Windows,Converters,Models,Resources}/` – WPF UI.

---

## 2. Quy chuẩn Chất lượng Code (SOLID & DRY)

### Single Responsibility Principle (SRP)
Mỗi Service hoặc Step chỉ làm một việc. Ví dụ:
- `BrowserService` chỉ quản lý Chrome/Playwright.
- `HistoryService` chỉ ghi/tải lịch sử.
- `ChatGptService` chỉ tương tác giao diện ChatGPT.

### Don't Repeat Yourself (DRY)
- Tránh sao chép logic xử lý chuỗi / UI selector.
- Gom HTML/JS chèn vào Playwright hoặc Regex (như `ExtractVideoId`) vào **Service** hoặc **Helper** dùng chung (`ChatGptService`, `YoutubeHelper`, `GeminiSelectors`).

### Dependency Injection (DIP)
- **Mọi Service** phải nhận dependencies qua constructor (`IConfigService`, `ILogService`, `IBrowserService`).
- **Composition root** đăng ký trong `App.xaml.cs` (`Host.CreateDefaultBuilder().ConfigureServices(...)`).
- **Không dùng `static Instance`/`Default`** trong service thuộc DI graph. Bridge tĩnh chỉ dành cho các field initializer của WPF (xem `AssetAutomator.UI.ConfigService`).

---

## 3. Quy tắc Quản lý Tài nguyên

### Playwright Browser Contexts
- Luôn đóng trình duyệt bằng `CloseBrowserSafelyAsync` (có Timeout bảo vệ) trong `finally`.
- Dọn dẹp các thư mục profile tạm (`TempProfile_*`) sau khi luồng chạy kết thúc.

### Thread-Safety
- Tác vụ ghi file chung (History, log, slot browser) **bắt buộc** dùng `SemaphoreSlim` hoặc `lock object`.
- Tác vụ nặng chạy bằng `Task.Run` + cập nhật UI qua `Dispatcher.BeginInvoke`.

### Cancellation
- Mọi step/service chạy lâu phải nhận `CancellationToken` và kiểm tra `token.ThrowIfCancellationRequested()` tại các checkpoint (xem `p2-add-cancellation-token.md`).

### Retry
- HTTP calls tới external API (Gemini, AI84, ElevenLabs, Flow Local) dùng `Polly` retry policy với exponential backoff (xem `p2-retry-policy-polly.md`).

---

## 4. Quy trình Cải tiến & Kiểm thử

Khi AI Assistant nhận yêu cầu cải tiến tính năng mới:

1. **Đọc trước**:
   - `HOW-TO-RUN.md` (cách build & run)
   - `improve-docs/INDEX.md` (roadmap cải tiến)
   - Tài liệu liên quan trong `improve-docs/` (ví dụ thêm MVVM → đọc `p3-mvvm-*.md`).

2. **Bắt đầu bằng Model/Service**: Tách logic thành class riêng ở `Application/` hoặc `Infrastructure/` trước khi sửa UI.

3. **Cập nhật helper tĩnh** nếu có logic trùng lặp xuất hiện.

4. **Build & test**:
   ```bash
   dotnet build AssetAutomator.sln      # phải 0 errors
   dotnet run --project src/AssetAutomator.UI/AssetAutomator.UI.csproj
   ```

5. **Xử lý triệt để warnings** (đặc biệt NU1902, CS0618 obsolete, CS8600 nullable).

6. **Cập nhật tài liệu**: Nếu thêm feature mới, cập nhật `HOW-TO-RUN.md` hoặc thêm file vào `improve-docs/` rồi cập nhật `INDEX.md`.
