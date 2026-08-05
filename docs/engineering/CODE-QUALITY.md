# Đánh giá Code Quality

> Baseline: rà soát tĩnh ngày 05/08/2026. Đây không phải security pentest hay kết quả code coverage. Mức ưu tiên dựa trên impact và khả năng phát sinh lỗi, không chỉ dựa trên số dòng code.

## Kết luận ngắn

Dự án đã có nền tảng tốt: tách bốn project, dùng DI, MVVM Toolkit, pipeline stage/semaphore, centralized logging interface và provider strategy. Tuy nhiên, mức production readiness còn bị giới hạn bởi bốn nhóm chính:

1. Credential/config và artifact runtime chưa được quản lý an toàn.
2. Cancellation/error handling/logging chưa xuyên suốt.
3. Thiếu automated tests và CI quality gate cho phần C#.
4. Một số class/module quá lớn và trạng thái domain bị trùng lặp.

Không nên tiếp tục tuyên bố “100% code improvement” chỉ dựa trên checklist refactor cũ; checklist đó phản ánh một scope lịch sử, không phải chất lượng toàn dự án hiện tại.

## Điểm mạnh

### Kiến trúc module rõ

- Core, Infrastructure, Application và WinUI được tách thành project.
- Dependency direction nhìn chung rõ ràng.
- Composition root tập trung tại `App.xaml.cs`.
- Legacy pipeline được đánh dấu `[Obsolete]` thay vì tiếp tục mở rộng.

### DI và state lifetime có chủ đích

- Service/ViewModel được đăng ký tập trung.
- Factory registration tránh nullable constructor parameter bị DI inject `null`.
- `GeminiViewModel` singleton có lý do rõ: giữ state khi navigation.

### Pipeline concurrency hợp lý

- Semaphore theo stage phù hợp với workload khác nhau.
- Có resume/skip dựa trên artifact.
- Có preflight health check trước image stage.
- Có tổng hợp batch result và log theo task.

### Một số abstraction tốt

- `IConfigService`, `ILogService`, `IBrowserService`.
- Image provider strategy/factory.
- Model/status observable phục vụ WinUI.

## Phát hiện ưu tiên P0/P1

### P0 — Credential và session artifact

**Bằng chứng**:

- `AppSettings` chứa API/license key và các URL deployment.
- `ConfigService` serialize toàn bộ settings ra JSON trong AppData.
- Git status có `Modules/Gemini-API-2.0.0/cookies.json`.

**Đánh giá**:

- `flow-local-key` là token nội bộ mặc định, không phải Google secret; không nên gắn nhãn “credential bị lộ” nếu server chỉ bind localhost và semantics cho phép giá trị bất kỳ.
- URL Custom GPT/license server public không tự động là secret, nhưng hardcode tạo coupling và có thể để lộ deployment identifier.
- Cookie/session và API key thực mới là dữ liệu cần bảo vệ khẩn cấp.

**Hành động**:

- Ignore cookie/session/output/log.
- Kiểm tra lịch sử git và rotate cookie/key nếu từng commit.
- Dùng DPAPI/Windows Credential Manager cho secret at rest.
- Tách deployment endpoint khỏi model Core.

### P1 — Cancellation không xuyên suốt

`PipelineOrchestrator` nhận token nhưng `GeminiTopicResearchStep`, `VoiceoverGenerationStep`, browser flow và image provider chưa thống nhất token. Polling voiceover có nhiều `Task.Delay`/HTTP call không hủy được.

**Hậu quả**: người dùng nhấn Cancel nhưng network/browser/GPU job vẫn chạy; resource và output có thể tiếp tục thay đổi.

**Hành động**: thêm token cuối signature, truyền tới `SendAsync`, `ReadAs*Async`, `Task.Delay`, semaphore và Playwright operation; dùng trạng thái `Cancelled` riêng.

### P1 — Thiếu automated test cho C#

Không có test project được xác nhận là thành viên của solution và chạy được; các test Python thuộc module ngoài không cover orchestration/ViewModel/config C#.

**Hành động tối thiểu**:

- Unit test cho config defaults/serialization.
- Unit test `PipelineOrchestrator` bằng fake step adapter.
- Unit test provider selection và artifact skip logic.
- Test status/cancellation mapping.
- CI chạy restore, build, test và publish smoke check x64.

### P1 — Error handling không nhất quán

Có catch rỗng, catch chỉ `Debug.WriteLine`, và provider chuyển exception thành status string mà không log tập trung. Global handler đặt `Handled=true` có nguy cơ che lỗi UI nghiêm trọng.

**Hành động**:

- Quy ước exception boundary theo layer.
- Không swallow ngoài cleanup best-effort có comment/lý do.
- Log exception qua `ILogService` với category, operation và task ID.
- Chỉ handle global UI exception khi app còn ở trạng thái an toàn.

## Phát hiện P2

### Quản lý HTTP chưa thống nhất

Có static `HttpClient` ở một số provider, nhưng `VoiceoverGenerationStep` tạo client mỗi lần và nhiều service tự quản lý client/timeout/header.

**Đề xuất**: dùng `IHttpClientFactory` với named client (`Gemini`, `AI84`, `FlowLocal`, `License`, `Update`), timeout/retry riêng và handler dễ fake trong test.

### Class lớn, nhiều trách nhiệm

- `GeminiPlaywrightSceneCreator` quản lý browser lifecycle, selector, input/file drop, wait/retry, parse và cleanup.
- `GeminiViewModel` quản lý collection, command, cookie, execution, logging, dialog và UI state.
- `App.xaml.cs` vừa composition root vừa setup Python/server/dialog startup.

**Đề xuất**: tách theo use case và adapter, nhưng làm sau khi có characterization test để tránh regression.

### Hai hệ trạng thái task

`AutomationTask` dùng nhiều status string, `GeminiTaskModel` dùng `NodeStatus`; ViewModel còn suy trạng thái từ text log. Đây là coupling dễ vỡ.

**Đề xuất**: `PipelineStage`, `PipelineStageState`, `TaskRunState`, event typed `PipelineProgress`; log chỉ là presentation, không phải nguồn state.

### Service locator/static singleton còn sót

`PythonServerManager` vừa được đăng ký singleton qua DI, vừa gán instance vào static `PythonServerManager.Default`. Hai đường truy cập tạo hidden dependency, làm test isolation khó hơn và có nguy cơ dùng instance không cùng lifecycle với host.

**Đề xuất**: loại bỏ `Default` sau khi xác nhận không còn caller; chỉ inject `PythonServerManager` hoặc interface lifecycle tương ứng.

### Static mutable cache trong Flow provider

Dictionary và semaphore static giúp tái sử dụng media ID nhưng làm state sống toàn process, khó test và có thể reuse sai giữa account/project.

**Đề xuất**: cache service scoped theo batch, key gồm account/project/file hash, có TTL và cancellation.

### Validation cấu hình còn mỏng

Port chỉ kiểm tra `<= 0`, URL/path chưa validate đầy đủ; `EnsureDefaults` có thể che lỗi bằng fallback.

**Đề xuất**: typed validation trả danh sách lỗi cho Settings page; phân biệt default, missing và invalid.

## Phát hiện P3

- Nhiều magic/status/display string còn rải rác.
- Manual `INotifyPropertyChanged` tồn tại song song source generator.
- `SynchronizationContext` nằm trong domain-like model làm thread concern tràn xuống Core.
- Một số converter ném `NotImplementedException` cho `ConvertBack`.
- Chưa có `.editorconfig`/analyzer policy/nullable warning baseline rõ.
- Documentation cũ mâu thuẫn giữa WPF, WinUI, số tab, package version và output filename.

## Security và privacy checklist

- Không commit cookie, browser profile, proxy credential hoặc appsettings thật.
- Mask API key/proxy password/license key trong UI và log.
- Chỉ bind local server vào loopback mặc định; nếu bind LAN phải có auth thực.
- Validate output path nằm trong allowed root trước khi ghi.
- Kiểm checksum runtime/script tải về.
- Pin/version module Python và dependency lock.
- Scan NuGet/Python dependency trong CI.

## Quality gate đề xuất

Mỗi pull request nên đạt:

- `dotnet build` x64 không có error.
- `dotnet test` pass; không giảm coverage cho Application/Core đã được test.
- Python unit test chọn lọc pass cho module được thay đổi.
- Không có secret/session/output mới trong git diff.
- Analyzer không phát sinh warning mới ở mức đã chốt.
- Docs feature/limitation được cập nhật khi behavior thay đổi.
- Smoke test startup WinUI và health check Flow Local cho release candidate.

## Cách duy trì tài liệu này

Khi xử lý một phát hiện, ghi bằng chứng kiểm chứng và chuyển đầu việc trong [Improvement Roadmap](./IMPROVEMENT-ROADMAP.md). Không xóa hạn chế chỉ vì code đã sửa; chỉ đóng sau build/test/smoke test phù hợp.
