# Lộ trình cải thiện kỹ thuật

> Đây là phương án triển khai đề xuất, chưa phải cam kết tính năng. Thứ tự ưu tiên: bảo vệ dữ liệu → kiểm soát runtime → tạo safety net → refactor.

## Phase 0 — Repository safety (0.5–1 ngày)

### Mục tiêu

Ngăn credential, session và output mới bị commit.

### Công việc

- Mở rộng `.gitignore` cho cookie/session, output ảnh, logs, diagnostic output và local settings.
- Kiểm tra file nào đã được tracked trong lịch sử; không xóa dữ liệu người dùng trên máy khi chưa xác nhận.
- Rotate/revoke cookie hoặc key nếu phát hiện từng xuất hiện trong commit/PR/public artifact.
- Thêm secret scan ở CI hoặc pre-commit.

### Tiêu chí hoàn thành

- `git status` không còn liệt kê artifact runtime sau khi chạy app/test.
- Secret scan pass trên diff mới.
- Tài liệu setup không yêu cầu commit credential.

## Phase 1 — Cancellation, HTTP và error boundary (3–5 ngày)

### Mục tiêu

Cancel thực sự dừng công việc và lỗi được quan sát nhất quán.

### Công việc

1. Thêm `CancellationToken` vào mọi operation dài.
2. Truyền token tới HTTP, delay, semaphore, file I/O và Playwright.
3. Thêm `TaskRunState.Cancelled`; không map cancel thành failed.
4. Cấu hình `IHttpClientFactory` với named clients.
5. Chuẩn hóa exception mapping và logging context.
6. Giảm global exception swallowing trong `App`.

### Thứ tự triển khai

- Voiceover polling.
- Gemini research/API stream.
- Scene browser automation.
- Flow image provider.
- Startup Python/Flow Local.

### Tiêu chí hoàn thành

- Cancel trong mỗi stage dừng trong thời gian giới hạn được định nghĩa.
- Không còn delay/request dài thiếu token.
- Task bị hủy có trạng thái và log riêng.
- Không có catch rỗng ngoài cleanup best-effort được phê duyệt.

## Phase 2 — Test foundation và CI (4–7 ngày)

### Mục tiêu

Tạo safety net trước khi refactor class lớn.

### Công việc

- Tạo/khôi phục test project C# chuẩn và thêm vào solution.
- Dùng xUnit/NUnit hoặc MSTest; chọn một framework duy nhất.
- Fake HTTP bằng `HttpMessageHandler` hoặc named-client test handler.
- Tạo adapter interface cho pipeline steps để orchestrator dễ test.
- Thêm CI Windows x64.

### Test ưu tiên

1. `ConfigService`: default, migration, invalid JSON, env override.
2. `PipelineOrchestrator`: concurrency limit, skip artifact, failure, cancellation.
3. `FlowLocalImageGenProvider`: URL normalization, cache boundary, response parse.
4. `VoiceoverGenerationStep`: submit/poll/done/fail/timeout/cancel.
5. ViewModel: command enablement và state transition không cần WinUI visual tree.

### Tiêu chí hoàn thành

- CI build/test pass trên pull request.
- Có test cho happy path, failure và cancellation của pipeline.
- Không gọi network/browser thật trong unit test.
- Release candidate có smoke test riêng, không trộn với unit test.

## Phase 3 — Configuration and secret storage (3–5 ngày)

### Mục tiêu

Tách cấu hình deployment và bảo vệ credential at rest.

### Công việc

- Tách `AppSettings` thành general settings, endpoint options và secrets.
- DPAPI/Credential Manager cho API/license key.
- Không persist environment override trở lại JSON.
- URL/port/path validation hiển thị trực tiếp trong Settings.
- Cấu hình endpoint theo environment/release channel.
- Mask secret trong log/UI.

### Tiêu chí hoàn thành

- JSON settings không chứa secret mới ở plain text.
- Endpoint invalid không được lưu/chạy.
- Tunnel tạm thời không còn là default release.
- Migration từ settings cũ có backup và rollback.

## Phase 4 — Typed pipeline state (4–6 ngày)

### Mục tiêu

Loại bỏ việc suy trạng thái từ log string và giảm trùng model.

### Thiết kế đề xuất

```text
PipelineTask
  RunState
  Stages: IReadOnlyDictionary<PipelineStage, StageResult>

PipelineProgress
  TaskId
  Stage
  State
  Percent
  Message
  ErrorCode
```

### Công việc

- Định nghĩa enum stage/state trong Core.
- Orchestrator phát typed progress event/callback.
- ViewModel bind typed state, log chỉ để hiển thị.
- Tạo mapper tạm cho `AutomationTask` và `GeminiTaskModel`.
- Hợp nhất model sau khi migration hoàn tất.

### Tiêu chí hoàn thành

- Không parse text log để cập nhật stage.
- Một nguồn sự thật cho task/stage state.
- Existing history/settings được migrate hoặc backward compatible.

## Phase 5 — Tách class lớn (1–2 tuần)

### Mục tiêu

Giảm cognitive load và tăng khả năng test mà không đổi hành vi.

### Gemini browser automation

Tách thành:

- Browser/session manager.
- Gem/model selector.
- Input/file attachment adapter.
- Response wait/completion strategy.
- Response parser.
- Diagnostic capture policy.

### Gemini ViewModel

Tách thành:

- Task collection service.
- Execution coordinator.
- Gem catalog loader.
- Cookie/session workflow.
- UI log buffer/presenter.

### App startup

Tách Python setup và Flow Local startup thành hosted/startup service có timeout, cancellation và user-facing status. Hosted service phải sở hữu process lifecycle, thay thế `PythonServerManager.Default`, cleanup idempotent khi window đóng/host shutdown và có best-effort safety net khi process thoát bất thường.

### Tiêu chí hoàn thành

- Mỗi class có trách nhiệm rõ và dependency nhỏ.
- Characterization test giữ nguyên behavior.
- Không thêm service locator ngoài composition root/page activation boundary.

## Phase 6 — Performance và release hardening (3–7 ngày)

### Công việc

- Configurable/adaptive stage concurrency và cooldown.
- Task artifact manifest/hash để resume chính xác.
- Atomic write cho config/history/scenes.
- Python setup qua staging + checksum + rollback.
- Publish x64 reproducible; dependency manifest/SBOM.
- Telemetry opt-in không chứa prompt/credential.

### Tiêu chí hoàn thành

- Resume không dùng artifact từ input/config khác.
- Setup fail không để runtime nửa hoàn chỉnh.
- Release package được smoke test trên máy sạch.
- Có rollback và troubleshooting theo error code.

## Ước lượng và cách chia PR

Nên chia thành PR nhỏ, mỗi PR có một mục tiêu kiểm chứng được:

1. Git ignore + credential handling policy.
2. Voiceover cancellation + tests.
3. Gemini API cancellation + tests.
4. Flow provider cancellation/cache scope + tests.
5. Named HTTP clients.
6. Typed pipeline progress.
7. Model consolidation.
8. Browser creator decomposition từng adapter.
9. ViewModel decomposition.
10. Release hardening.

Không nên gộp security cleanup, refactor kiến trúc và thay đổi behavior vào cùng một PR.
