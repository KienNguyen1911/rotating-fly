# AssetAutomator

AssetAutomator là ứng dụng WinUI 3 trên Windows hỗ trợ tự động hóa quy trình tạo tài nguyên nội dung: nghiên cứu/kịch bản, voice-over, phụ đề, scene breakdown và ảnh minh họa.

## Tính năng chính

- Gemini AI Creator theo pipeline bốn stage: Research → Voiceover/SRT → Scene → Image.
- Batch Image Generation với Flow Local và G-Labs.
- Automation Tasks, Image Pool, Chrome Profiles, History và Settings.
- Chạy nhiều task theo giới hạn concurrency riêng cho từng stage.
- Tự chuẩn bị Python embedded và khởi động Flow Local khi được bật.

Chi tiết và trạng thái từng tính năng: [docs/product/FEATURES.md](./docs/product/FEATURES.md).

## Quick start cho developer

Yêu cầu chính:

- Windows 10/11 x64.
- .NET 10 SDK.
- Windows App Runtime/WebView2 theo cấu hình WinUI hiện tại.
- PowerShell; kết nối mạng cho lần setup Python/Chromium nếu runtime chưa được bundle.

```powershell
dotnet restore AssetAutomator.sln
dotnet build src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
dotnet run --project src/AssetAutomator.WinUI/AssetAutomator.WinUI.csproj -c Debug -p:Platform=x64
```

Hướng dẫn đầy đủ: [HOW-TO-RUN.md](./HOW-TO-RUN.md).

## Cấu trúc repository

```text
src/
  AssetAutomator.Core/            Models, interfaces, constants
  AssetAutomator.Infrastructure/  Config, logging, OS/network/browser helpers
  AssetAutomator.Application/     Services, providers, pipeline steps
  AssetAutomator.WinUI/           WinUI pages, dialogs, ViewModels
Modules/                           Gemini API và Google Flow Python modules
tools/                             Setup, diagnostics và smoke-test scripts
docs/                              Tài liệu hiện hành
```

## Tài liệu

- [Mục lục docs](./docs/README.md)
- [Tính năng đã triển khai](./docs/product/FEATURES.md)
- [Hạn chế và điểm cần cải thiện](./docs/product/LIMITATIONS.md)
- [Kiến trúc](./docs/engineering/ARCHITECTURE.md)
- [Code quality](./docs/engineering/CODE-QUALITY.md)
- [Lộ trình cải thiện](./docs/engineering/IMPROVEMENT-ROADMAP.md)
- [Flow API](./FLOW-API.md)

## Trạng thái dự án

Ứng dụng đã có WinUI shell và các workflow chính trong mã nguồn. Tuy nhiên, độ sẵn sàng thực tế phụ thuộc nhiều dịch vụ ngoài; cancellation chưa xuyên suốt, credential đang lưu plain text và automated test/CI cho phần C# chưa đủ bằng chứng. Xem tài liệu hạn chế trước khi phát hành production.

Không coi các tuyên bố build/test trong tài liệu là bằng chứng hiện tại nếu không kèm kết quả chạy trong cùng phiên bản/commit.

## An toàn dữ liệu

Không commit:

- `cookies.json`, browser session/profile data.
- API/license keys hoặc appsettings thật.
- Ảnh sinh tự động, logs, screenshots chẩn đoán.
- `bin/`, `obj/`, Python runtime/venv.

Nếu credential từng được commit, cần rotate/revoke thay vì chỉ xóa file ở working tree.

## Bản quyền nội dung

Chỉ xử lý và tái sử dụng nội dung khi bạn có quyền phù hợp. Người vận hành chịu trách nhiệm với prompt, nguồn video, giọng đọc, ảnh tham chiếu và đầu ra do các dịch vụ AI tạo ra.
