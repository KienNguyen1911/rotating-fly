# Hạn chế và điểm cần cải thiện

> Tài liệu này ghi nhận hạn chế dựa trên mã nguồn, không khẳng định một lỗi đang xảy ra ở mọi môi trường.

## Hạn chế chức năng

### Phụ thuộc nhiều dịch vụ ngoài

Gemini, ChatGPT, AI84, G-Labs, Google Flow, subtitle API và license server đều có thể thay đổi API, quota, cookie, DOM hoặc chính sách. Một pipeline có thể thất bại dù app không thay đổi.

**Cần cải thiện**: health check theo provider, thông báo lỗi có mã/nhóm nguyên nhân, retry có giới hạn và tài liệu fallback cho từng stage.

### Hủy task chưa dừng ngay

`PipelineOrchestrator` kiểm tra cancellation giữa các stage và khi chờ semaphore, nhưng nhiều step dài chưa nhận `CancellationToken`. Polling voiceover và browser automation có thể tiếp tục đến khi request/poll kết thúc.

**Cần cải thiện**: truyền token xuyên suốt HTTP request, delay, file I/O và Playwright operation; phân biệt trạng thái `Cancelled` với `Failed`.

### Browser automation dễ vỡ

Scene creator và cookie sync phụ thuộc selector/DOM của Gemini web. Nhiều selector fallback giúp tăng khả năng hoạt động nhưng cũng làm mã phức tạp và khó chẩn đoán.

**Cần cải thiện**: ưu tiên API stream; version hóa selector; lưu diagnostic snapshot có kiểm soát; contract test cho selector quan trọng.

### Flow Local là nút thắt cổ chai

Image stage mặc định chỉ có một slot và nghỉ 15 giây sau task. Cách này an toàn cho GPU/API nhưng làm batch lớn chậm.

**Cần cải thiện**: đưa concurrency và cooldown vào Settings; dùng adaptive throttling theo lỗi 429/GPU; hiển thị ETA.

### Resume dựa trên artifact

Pipeline bỏ qua stage nếu file output tồn tại và đạt kiểm tra tối thiểu. Điều này nhanh nhưng chưa xác nhận artifact được tạo từ đúng config/model hiện tại.

**Cần cải thiện**: thêm manifest theo task gồm input hash, model/provider, version pipeline và checksum output.

### Lifecycle server chưa có safety net khi process thoát bất thường

`MainWindow.Closed` đã dừng Gemini Python server và Flow Local trong luồng đóng cửa sổ bình thường. Tuy nhiên, chưa thấy fallback qua `AppDomain.ProcessExit` hoặc hosted-service disposal cho trường hợp window cleanup không chạy, process bị terminate hoặc startup thất bại giữa chừng.

**Cần cải thiện**: chuyển ownership process vào hosted service/`IAsyncDisposable`; cleanup idempotent ở cả window close và host shutdown; không cố thực hiện async dài trong `ProcessExit`.

### Một số converter chưa hỗ trợ `ConvertBack`

Nhiều WinUI converter ném `NotImplementedException` ở `ConvertBack`. Điều này chấp nhận được với binding one-way, nhưng sẽ gây lỗi nếu vô tình dùng two-way.

**Cần cải thiện**: dùng `BindingMode=OneWay` rõ ràng hoặc trả `DependencyProperty.UnsetValue`; thêm test binding cơ bản.

### Đăng nhập WebView chưa hoàn chỉnh

Tài liệu migration trước đây ghi WebView login từng dùng placeholder do khác biệt WinAppSDK. Cần kiểm tra lại luồng đăng nhập hiện tại trên bản đóng gói trước khi quảng bá là hoàn chỉnh.

## Hạn chế vận hành

### Cài đặt lần đầu nặng và phụ thuộc mạng

Python embedded, package Python và Chromium có thể cần hàng trăm MB. Setup giữa chừng có thể để lại trạng thái chưa hoàn chỉnh dù marker giúp hỗ trợ idempotency.

**Cần cải thiện**: kiểm checksum, tải có resume, progress UI, rollback thư mục staging và gói runtime sẵn trong release chính thức.

### Windows/x64 là nền tảng chính

WinUI project cấu hình `Platforms=x64` và unpackaged runtime. Các publish profile x86/ARM64 không đồng nghĩa đã được kiểm chứng.

**Cần cải thiện**: công bố x64 là supported target; chỉ quảng bá target khác sau CI và smoke test trên máy tương ứng.

### Cấu hình nhạy cảm lưu plain text

API key và license key được serialize trong AppData. Đây là giới hạn bảo mật của bản hiện tại.

**Cần cải thiện**: dùng Windows Credential Manager hoặc DPAPI; masking trong UI/log; không tự động ghi environment secret trở lại JSON.

### URL mặc định có thể lỗi thời

Subtitle API dùng URL tunnel ngrok cố định; license server và Custom GPT URL được hardcode làm default. Đây không nhất thiết là secret nhưng tạo coupling với endpoint có vòng đời ngoài repository.

**Cần cải thiện**: chuyển sang deployment config; validate URL; hiển thị trạng thái endpoint; không dùng tunnel tạm làm default release.

### Repository có artifact runtime chưa được ignore đầy đủ

Git status hiện có cookie, ảnh output, log và nhiều artifact build không nên commit.

**Cần cải thiện ngay**:

- Ignore `Modules/*/cookies.json` và các cookie/session tương tự.
- Ignore `Modules/*/output/` hoặc pattern ảnh sinh tự động.
- Ignore `logs/`, `*.out.txt`, `*.err.txt` và diagnostic screenshots phù hợp.
- Kiểm tra lịch sử git nếu credential từng được commit; rotate/revoke khi cần.

## Hạn chế chất lượng và kiểm thử

- Chưa có bằng chứng về bộ unit test C# hoạt động trong solution chính.
- Các test Python chủ yếu thuộc module tích hợp ngoài.
- Chưa thấy CI bắt buộc build/test/format/security scan.
- Một số class rất lớn, đặc biệt Gemini browser automation và `GeminiViewModel`.
- Error handling/logging chưa đồng nhất; có nhiều catch rỗng hoặc chỉ ghi `Debug.WriteLine`.
- Task state bị chia giữa model enum và status string, làm mapping dễ lệch.

Chi tiết và phương án xử lý: [Code Quality](../engineering/CODE-QUALITY.md) và [Improvement Roadmap](../engineering/IMPROVEMENT-ROADMAP.md).
