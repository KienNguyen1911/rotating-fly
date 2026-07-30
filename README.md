# AssetAutomator

AssetAutomator là công cụ hỗ trợ biến một video YouTube thành bộ tài nguyên sẵn sàng cho quy trình làm nội dung: ảnh minh họa, nội dung kịch bản đã được viết lại, giọng đọc và phụ đề. Thay vì thực hiện từng thao tác lặp lại, bạn chỉ cần tạo tác vụ, chọn những đầu ra cần có và theo dõi tiến độ ở một nơi.

> 📖 **Đọc [`HOW-TO-RUN.md`](./HOW-TO-RUN.md) để biết cách build, run và kiến trúc dự án.**

---

## 🎯 Công cụ này phù hợp với ai?

- Người làm nội dung muốn tái sử dụng và chuyển thể video tham khảo thành nội dung theo ngôn ngữ mới.
- Nhóm sản xuất cần tạo nhiều kịch bản, giọng đọc và phụ đề theo một quy trình thống nhất.
- Người quản lý nhiều video, cần biết rõ tác vụ nào đã xong, đang chạy hoặc cần xử lý lại.

---

## ✨ Những việc AssetAutomator có thể làm

### 🎬 Xử lý video từ một đường dẫn YouTube
Với mỗi URL video, công cụ có thể tạo các đầu ra:
- Tải ảnh đại diện.
- Trích xuất transcript / lời thoại.
- Viết lại kịch bản theo ngôn ngữ đích (qua ChatGPT hoặc Gemini).
- Tạo voice-over từ kịch bản (ElevenLabs / AI84).
- Sinh file phụ đề SRT đồng bộ với voice-over.

### 🤖 Gemini AI Creator (Tab Gemini)
Quy trình 5-step tự động hoá toàn diện:
1. **Deep Research** → báo cáo nghiên cứu + kịch bản (`transcript.txt`)
2. **Voiceover** → `audio.mp3` + `subtitles.srt`
3. **Scene Breakdown** → `scenes.json` (Visual Image Prompts)
4. **Batch Image Generation** → `img/scene_NN.png` (Flow Local / G-Labs)
5. **Export** → thư mục `Outputs/<task_id>/` hoàn chỉnh.

Hỗ trợ **chạy batch nhiều task song song** qua `PipelineOrchestrator` (ma trận 4-stage với rate-limit).

> Chi tiết: xem [`improve-docs/gemini-creator-architecture.md`](./improve-docs/gemini-creator-architecture.md).

### 🖼️ Tạo ảnh hàng loạt (Tab Hình ảnh)
- Hỗ trợ 2 provider: **Google Flow Local** (port 8787) và **G-Labs** (port 8765).
- Tái sử dụng `reference_media_id` để sinh nhiều biến thể từ 1 ảnh gốc (1-to-N pattern).
- Auto-create Project trên `labs.google/fx/tools/flow` qua `POST /v1/projects`.

> Chi tiết API: xem [`FLOW-API.md`](./FLOW-API.md).

### 📋 Quản lý tác vụ
- Thêm 1 hoặc nhiều URL YouTube (paste danh sách để tạo hàng loạt).
- Gán giọng đọc và Chrome Profile riêng cho từng video.
- Chạy 1 task hoặc batch nhiều task cùng lúc.
- Theo dõi trạng thái realtime (Idle / Running / Success / Failed) trên DataGrid.
- Mở thư mục kết quả, xem log chi tiết, xóa task không cần.

### 🔐 Quản lý bản quyền (License)
- 1 License ↔ 1 thiết bị active tại 1 thời điểm (chống dùng đồng thời).
- Heartbeat 5 phút/lần, Offline grace period 7 ngày.
- Hỗ trợ chuyển máy, kích hoạt, hủy kích hoạt.

---

## 🚀 Quick Start

### 📦 Cho người dùng cuối (End-User)
1. Tải bản release `.zip` từ mục Releases.
2. Giải nén và chạy `AssetAutomator.exe`.
3. Điền cấu hình API key trong **Settings** (Gemini, AI84, G-Labs, ElevenLabs, v.v.).

### 🛠️ Cho lập trình viên (Developer)
```bash
# 1. Clone
git clone <URL>
cd AssetAutomator

# 2. Build
dotnet build AssetAutomator.sln

# 3. Run
dotnet run --project src/AssetAutomator.UI/AssetAutomator.UI.csproj
```

Chi tiết đầy đủ: **[HOW-TO-RUN.md](./HOW-TO-RUN.md)** — yêu cầu môi trường, cấu hình AppSettings, cách DI hoạt động, troubleshooting.

---

## 📂 Cấu trúc dự án

```
AssetAutomator/
├── src/                              ← Source code (4 projects, Modular Monolith)
│   ├── AssetAutomator.Core/          ← Domain models, interfaces, constants
│   ├── AssetAutomator.Infrastructure/ ← OS/network helpers
│   ├── AssetAutomator.Application/   ← Business logic + pipeline steps
│   └── AssetAutomator.UI/            ← WPF presentation (composition root)
├── tools/                            ← Runtime tools (Python embedded, scripts)
├── Modules/                          ← External Python modules (Gemini server)
├── Resources/                        ← Logo gốc
├── improve-docs/                     ← Lịch sử cải tiến P1–P4 + tài liệu kiến trúc
├── HOW-TO-RUN.md                     ← Hướng dẫn build/run chi tiết
├── FLOW-API.md                       ← Tài liệu tích hợp Google Flow Image API
├── README.md                         ← File này
└── AssetAutomator.sln                ← Solution file
```

Chi tiết từng layer + dependency graph: **[HOW-TO-RUN.md §2](./HOW-TO-RUN.md)**.

---

## 📚 Tài liệu tham khảo

| Tài liệu | Mô tả |
|---|---|
| **[HOW-TO-RUN.md](./HOW-TO-RUN.md)** | Build, run, architecture overview, DI explanation, troubleshooting. |
| **[FLOW-API.md](./FLOW-API.md)** | Google Flow Image API: endpoints, Python/JS/cURL examples, model list. |
| **[improve-docs/INDEX.md](./improve-docs/INDEX.md)** | Index roadmap cải tiến P1–P4 (15 reports). |
| **[improve-docs/DEVELOPER-RULES.md](./improve-docs/DEVELOPER-RULES.md)** | Coding conventions cho AI assistant + lập trình viên. |
| **[improve-docs/gemini-creator-architecture.md](./improve-docs/gemini-creator-architecture.md)** | Kiến trúc 5-step Gemini pipeline + PipelineOrchestrator matrix. |
| **[improve-docs/p4-project-cleanup.md](./improve-docs/p4-project-cleanup.md)** | Phase tái cấu trúc dự án (Core / Infrastructure / Application / UI). |
| **[improve-docs/check-list.md](./improve-docs/check-list.md)** | Checkbox trạng thái từng đầu việc cải tiến. |

---

## ⚙️ Yêu cầu hệ thống

| Thành phần | Yêu cầu |
|---|---|
| OS | Windows 10/11 (64-bit) |
| .NET SDK | .NET 10 SDK |
| Chrome / Chromium | Bắt buộc (cho `BrowserService` + Playwright) |
| WebView2 Runtime | Có sẵn trên Windows 10/11 |
| Python Embedded | Đặt ở `tools/PythonEmbed/` (app tự quản lý) |
| RAM tối thiểu | 8 GB |

---

## 🤝 Đóng góp

Trước khi thêm tính năng mới:
1. Đọc **[improve-docs/DEVELOPER-RULES.md](./improve-docs/DEVELOPER-RULES.md)**.
2. Tách logic thành class riêng ở `Application/` hoặc `Infrastructure/` (SRP + DI).
3. Build pass 0 errors, test run thành công.
4. Cập nhật tài liệu liên quan trong `improve-docs/` hoặc `HOW-TO-RUN.md`.

---

## 📜 Lưu ý bản quyền

- Chỉ sử dụng nội dung video khi bạn có quyền phù hợp hoặc được phép sử dụng.
- Chất lượng đầu ra phụ thuộc vào nội dung gốc, ngôn ngữ được chọn và giọng đọc sử dụng.
- Khi chạy số lượng lớn, nên bắt đầu với 1–2 tác vụ để kiểm tra thiết lập trước.

---

**Trạng thái dự án**: ✅ Build pass (0 errors) · ✅ App khởi động thành công · ✅ Tài liệu cập nhật đầy đủ sau P4 cleanup.
