# License Flow Design & System Behavior

Tài liệu này mô tả chi tiết kiến trúc, quy trình kiểm tra (flow), các trường hợp xử lý (cases) và hành vi ứng dụng (behaviour) cho hệ thống quản lý bản quyền **AssetAutomator**.

---

## 1. Mục tiêu & Nguyên tắc Bản quyền
- **1 License Key ↔ 1 Thiết bị tại một thời điểm** (Single Active Device per License).
- **Thời hạn hiệu lực**: 30 ngày kể từ thời điểm kích hoạt thành công (hỗ trợ reset/gia hạn khi cần).
- **Hỗ trợ chuyển máy**: Cho phép chuyển bản quyền sang thiết bị mới khi thay máy hoặc mất máy.
- **Chống sử dụng đồng thời (Anti-Concurrency)**: Nếu License được kích hoạt/chuyển sang thiết bị mới, thiết bị cũ lập tức bị vô hiệu hóa.
- **Bắt buộc bản quyền (Enforced License Guard)**: Không có bản quyền hợp lệ -> Ứng dụng lập tức tự đóng (Shutdown).

---

## 2. Mô hình Dữ liệu & Lưu trữ Bảo mật

### Dữ liệu Server (Google Sheets Database)
- **Sheet `Licenses`**:
  - `LicenseKeyHash`: Mã băm SHA-256 (Salted) của License Key.
  - `LicenseKey`: Mã bản quyền dạng hiển thị (VD: `ASSET-XXXX-XXXX-XXXX`).
  - `AppId`: Tên ứng dụng (`AssetAutomator`).
  - `Status`: Trạng thái bản quyền (`Active` / `Inactive` / `Expired` / `Revoked`).
  - `DeviceId`: Mã thiết bị hiện đang liên kết.
  - `ActivatedAt`: Thời điểm kích hoạt lần đầu (ISO Timestamp).
  - `ExpiredAt`: Thời điểm hết hạn (ISO Timestamp = ActivatedAt + 30 ngày).
  - `LastHeartbeat`: Thời điểm gửi Heartbeat gần nhất.
  - `SessionId`: GUID phiên làm việc hiện tại (dùng để phát hiện xung đột thiết bị).
  - `TransferCount`: Số lần đã chuyển thiết bị.
- **Sheet `ActivationLogs`**: Nhật ký toàn bộ hành động (`ACTIVATE`, `VERIFY`, `HEARTBEAT`, `DEACTIVATE`, `TRANSFER`, `INVALID_SESSION`, `EXPIRED`, `REVOKED`).

### Dữ liệu Client (Token Cục Bộ)
- **Vị trí lưu**: `%AppData%/AssetAutomator/license.dat`
- **Bảo mật**: Mã hóa dữ liệu bằng Windows DPAPI (`ProtectedData.Protect` theo người dùng Windows).
- **Thời hạn Offline (Offline Grace Period)**: Tối đa **7 ngày** kể từ lần xác thực Online thành công gần nhất.

---

## 3. Mã Thiết Bị (DeviceId Fingerprint)
Tạo bằng thuật toán SHA-256 kết hợp 4 thành phần phần cứng duy nhất của máy tính:
$$\text{DeviceId} = \text{SHA256}(\text{CPU ProcessorId} \parallel \text{Mainboard Serial} \parallel \text{Disk Drive Serial} \parallel \text{Windows User SID})$$

---

## 4. Chi tiết Luồng & Behaviour Theo Các Case

```text
                               ┌─────────────────────────┐
                               │     Khởi động App       │
                               └────────────┬────────────┘
                                            │
                                  Xác thực License?
                                            │
                    ┌───────────────────────┴───────────────────────┐
                    ▼                                               ▼
         [Xác thực THÀNH CÔNG]                          [Xác thực THẤT BẠI]
                    │                                               │
        Chạy Heartbeat ngầm 5 min                            Hiển thị Popup
                    │                                      Kích Hoạt Bản Quyền
                    │                                               │
                    │                                  ┌────────────┴────────────┐
                    │                                  ▼                         ▼
                    │                            [Nhập Key Đúng]         [Tắt Popup / Hủy]
                    │                                  │                         │
                    └──────────────────────────────────┤                   Đóng ứng dụng
                                                       ▼                    (Shutdown)
                                                Vào app sử dụng
```

---

### Case 1: Khởi động Ứng dụng (App Startup)
1. App khởi động -> Tự động gọi `VerifyAsync()`:
   - **Khi Có Mạng (Online)**:
     - Gửi `LicenseKey`, `DeviceId`, `SessionId` lên Server.
     - Nếu Server trả `Active` -> Cập nhật ngày xác thực online vào token cục bộ, khởi chạy **Timer Heartbeat ngầm (5 phút/lần)** -> Vào app bình thường.
     - Nếu Server trả `INVALID_SESSION`, `EXPIRED` hoặc `REVOKED` -> Xóa token cục bộ, mở **Popup Kích Hoạt**.
   - **Khi Mất Mạng (Offline)**:
     - Đọc token mã hóa cục bộ. Kiểm tra: `DeviceId` khớp + chưa hết hạn 30 ngày + chưa quá 7 ngày offline.
     - Nếu thỏa mãn -> Cho phép sử dụng Offline.
     - Nếu quá 7 ngày offline hoặc token hết hạn -> Mở **Popup Kích Hoạt**.
2. **Hành vi khi đóng Popup**: Nếu người dùng tắt Popup Kích Hoạt mà chưa kích hoạt bản quyền hợp lệ -> Ứng dụng tự động gọi `Application.Current.Shutdown()` để thoát hoàn toàn.

---

### Case 2: Kích Hoạt Lần Đầu (First Activation)
- Người dùng nhập License Key -> Bấm `Kích hoạt` (`POST /activate`):
  - **Chưa từng kích hoạt (`DeviceId` trống)**: Gán `DeviceId` máy hiện tại, `ActivatedAt` = now, `ExpiredAt` = now + 30 ngày, sinh `SessionId` mới, đổi `Status` = `Active`.
  - **Đã gán máy khác (`DeviceId` khác)**: Trả về lỗi `ALREADY_BOUND` (*"License đã được liên kết với thiết bị khác. Vui lòng chọn Chuyển máy"*).
  - **Key hết hạn**: Trả về lỗi `EXPIRED`.
  - **Key bị khóa**: Trả về lỗi `REVOKED`.

---

### Case 3: Chống Dùng Đồng Thời & Heartbeat (Anti-Concurrency)
- Chu kỳ: **5 phút/lần** chạy ngầm khi ứng dụng đang mở.
- Gửi payload: `LicenseKey`, `DeviceId`, `SessionId`.
- **Kịch bản xung đột**:
  1. Máy A đang chạy với `SessionId_A`.
  2. Máy B thực hiện `Transfer` hoặc kích hoạt cùng License -> Server tạo `SessionId_B` mới và gán `DeviceId` = Máy B.
  3. Ở chu kỳ Heartbeat tiếp theo, Máy A gửi `SessionId_A`.
  4. Server phát hiện sai `SessionId` -> Trả về mã error `INVALID_SESSION`.
  5. Máy A nhận `INVALID_SESSION` -> Lập tức hủy Heartbeat Timer, xóa token cục bộ, mở Popup cảnh báo: *"Phiên bản quyền của bạn đã được chuyển sang thiết bị khác"*.
  6. Nếu Máy A đóng Popup mà không kích hoạt Key mới -> **Ứng dụng tự động đóng ngay lập tức**.

---

### Case 4: Chuyển Thiết Bị (Transfer Device)
- Người dùng ở máy mới bấm `Chuyển máy` (`POST /transfer`):
  - Server kiểm tra License Key hợp lệ và còn hạn sử dụng.
  - Server gán `DeviceId` mới, tạo `SessionId` mới, tăng `TransferCount` + 1.
  - Trả về thông tin kích hoạt thành công cho máy mới.
  - Máy cũ sẽ bị gỡ quyền tự động ở lần Heartbeat tiếp theo (trong tối đa 5 phút).

---

### Case 5: Hủy Kích Hoạt (Deactivate)
- Người dùng bấm `Hủy kích hoạt` (`POST /deactivate`):
  - Server xóa `DeviceId` và `SessionId` của License trên trang tính.
  - Client xóa file token mã hóa cục bộ và ngắt Heartbeat.
  - Khi đóng cửa sổ bản quyền -> Ứng dụng tự động đóng.

---

### Case 6: Chặn Thực Thi Tác Vụ (Task Execution Guard)
- Khi người dùng bấm nút **"Chạy Task Đã Chọn"** hoặc **"Run Single Task"**:
  - Hệ thống tự động gọi hàm kiểm tra bản quyền `EnsureLicenseValidAsync()`.
  - Nếu phát hiện bản quyền chưa kích hoạt/hết hạn -> Mở Popup Kích Hoạt.
  - Nếu người dùng không kích hoạt mà đóng Popup -> Hủy toàn bộ luồng chạy và đóng ứng dụng.

---

## 5. Thiết Kế Giao Diện & UX (UI/UX Principles)
- **Tối giản (Minimalist)**: Giao diện chỉ tập trung vào ô nhập **License Key** và **Thẻ Trạng Thái Bản Quyền** (Green/Orange).
- **Ẩn thông tin kỹ thuật**:
  - URL License Server backend được cấu hình cố định trong mã nguồn/file config, không hiển thị ra Popup để tránh người dùng thao tác nhầm.
  - Mã thiết bị (Device ID) được ẩn hoàn toàn để đơn giản hóa trải nghiệm người dùng.
- **Hỗ trợ Co Giãn (Resizable Popup)**: Cửa sổ Kích Hoạt Bản Quyền hỗ trợ kéo giãn kích thước tự do (`ResizeMode="CanResizeWithGrip"`).
