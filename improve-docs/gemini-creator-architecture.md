# 📖 Kiến Trúc Hoạt Động: Gemini AI Creator

> Tài liệu này giải thích chi tiết kiến trúc tổng quan và quy trình xử lý **5-Step Pipeline** của **Gemini AI Creator** trong dự án `AssetAutomator`.
>
> **Lưu ý**: Sau P4 cleanup, các file source đã chuyển sang `src/AssetAutomator.Application/`. Đường dẫn trong tài liệu này đã được cập nhật theo cấu trúc mới.

---

## ⚙️ 1. Tổng Quan Kiến Trúc

**Gemini AI Creator** tự động hóa sản xuất Video ngắn/dài toàn diện dựa trên **Gemini AI** kết hợp **AI84 / ElevenLabs TTS** và các **Provider sinh ảnh AI (FLUX / Glabs / Flow Local)**.

```
[Nhập Chủ Đề / Link YouTube]
         │
         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 1: Deep Research (Report) ➔ Script (Transcript)   │ ──► research_report.txt, transcript.txt
└────────────────────────┬───────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 2: Tạo Giọng Đọc & Phụ Đề (AI84/ElevenLabs)      │ ──► audio.mp3, subtitles.srt
└────────────────────────┬───────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 3: Phân Cảnh & Prompt Ảnh (Gemini Scene Gem)    │ ──► scenes.json
└────────────────────────┬───────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 4: Sinh Ảnh Cảnh Hàng Loạt (+ Character Ref)    │ ──► img/scene_01.png, img/scene_02.png...
└────────────────────────┬───────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 5: Đóng Gói Tài Nguyên & Hoàn Tất (Export)       │ ──► Outputs/<id_task>/
└────────────────────────┴───────────────────────────────┘
```

---

## 🔍 2. Chi Tiết Các Bước Trong Quy Trình

### 🔴 STEP 1: Deep Research & Tạo Kịch Bản (`GeminiTopicResearchStep`)
- **File**: `src/AssetAutomator.Application/Steps/GeminiTopicResearchStep.cs`
- **Đầu vào**: Chủ đề / Link YouTube, Scriptwriter Gem ID (tuỳ chọn), cờ `EnableDeepResearch`.
- **Quy trình**:
  1. **Prompt 1** (nếu bật Deep Research): Gửi request tới Gemini Gem để tạo báo cáo nghiên cứu chuyên sâu → lưu `research_report.txt`.
  2. **Prompt 2**: Nhận report trên, gửi prompt chuẩn để viết lại thành kịch bản lời đọc (~1600–2000 từ).
  3. Lưu kịch bản thuần túy vào `transcript.txt`.
- **Output**: `research_report.txt` (nếu bật) + `transcript.txt`.

### 🟡 STEP 2: Voiceover & Phụ Đề SRT (`VoiceoverGenerationStep`)
- **File**: `src/AssetAutomator.Application/Steps/VoiceoverGenerationStep.cs`
- **Đầu vào**: `transcript.txt`, Voice ID, AI84 API Key (trong Settings).
- **Quy trình**:
  1. Tách `transcript.txt` thành các đoạn tối ưu cho TTS.
  2. Gọi API **AI84 / ElevenLabs** để tạo file âm thanh MP3.
  3. Ghép các file thành `audio.mp3` duy nhất.
  4. Trích xuất timestamps để xuất `subtitles.srt`.
- **Output**: `audio.mp3` + `subtitles.srt`.

### 🟢 STEP 3: Phân Cảnh & Prompt (`GeminiSceneBreakdownStep` / `GeminiPlaywrightSceneBreakdownStep`)
- **File**:
  - `src/AssetAutomator.Application/Steps/GeminiSceneBreakdownStep.cs` (HTTP API)
  - `src/AssetAutomator.Application/Steps/GeminiPlaywrightSceneBreakdownStep.cs` (Playwright UI)
- **Đầu vào**: `transcript.txt` + `subtitles.srt`, Scene Creator Gem ID (tuỳ chọn).
- **Quy trình**:
  1. Gemini đọc toàn bộ kịch bản và phụ đề.
  2. Chia thành các Scene (mỗi cảnh 3–6 giây).
  3. Với mỗi Scene, Gemini tạo **Visual Image Prompt** tiếng Anh chi tiết.
- **Output**: `scenes.json` với cấu trúc chuẩn (xem `src/AssetAutomator.Core/Models/SceneItemModel.cs`).

### 🔵 STEP 4: Sinh Ảnh Hàng Loạt + Character Reference (`SceneImageBatchStep`)
- **File**: `src/AssetAutomator.Application/Steps/SceneImageBatchStep.cs`
- **Provider factory**: `src/AssetAutomator.Application/Services/Providers/`
  - `FlowLocalImageGenProvider.cs` — gọi Google Flow API (port 8787, xem `FLOW-API.md` ở root)
  - `GlabsImageGenProvider.cs` — gọi G-Labs webhook (port 8765)
- **Đầu vào**: `scenes.json`, Character Ref, Provider chọn từ UI.
- **Quy trình**:
  1. Đọc danh sách cảnh + Character Ref.
  2. Gửi Visual Prompts song song/tuần tự tới provider đã chọn.
  3. Tải ảnh về và lưu vào `img/scene_NN.png`.
  4. Cập nhật đường dẫn ảnh ngược vào `scenes.json`.
- **Output**: Thư mục `img/` chứa tất cả ảnh minh họa.

### 🟣 STEP 5: Đóng Gói & Hoàn Tất
- Cấu trúc thư mục output cuối cùng (`Outputs/<id_task>/`):
  ```
  📁 Outputs/<id_task>/
  ├── 📄 research_report.txt
  ├── 📄 transcript.txt
  ├── 🎵 audio.mp3
  ├── 📜 subtitles.srt
  ├── 📊 scenes.json
  └── 📁 img/
      ├── 🖼️ scene_01.png
      ├── 🖼️ scene_02.png
      └── ...
  ```
- UI cập nhật Badge trạng thái → **`✔️ Hoàn thành`** + bật nút `Assets` để mở `scenes.json` hoặc mở thư mục output.

---

## 🚀 3. Pipeline Orchestrator — Chạy Nhiều Task Song Song (Batch)

### 3.1 Tổng Quan

**Pipeline Orchestrator** (`src/AssetAutomator.Application/Services/PipelineOrchestrator.cs`) chạy **nhiều task cùng lúc** theo mô hình **Assembly Line (Dây Chuyền Lắp Ráp)**. Mỗi task đi qua 4 stage, mỗi stage có giới hạn slot riêng.

### 3.2 Mô Hình Ma Trận (Assembly Line)

```
                        6 Tasks chạy đồng thời
                    ─────────────────────────────►
          Task1   Task2   Task3   Task4   Task5   Task6
          ─────────────────────────────────────────────
Stage A:  [██]    [██]    [⏳]    [⏳]    [⏳]    [⏳]    ← max 2 slot
Stage B:  [⏳]    [⏳]    [⏳]    [⏳]    [⏳]    [⏳]    ← max 3 slot
Stage C:  [⏳]    [⏳]    [⏳]    [⏳]    [⏳]    [⏳]    ← max 4 slot
Stage D:  [⏳]    [⏳]    [⏳]    [⏳]    [⏳]    [⏳]    ← max 1 slot (tuần tự)
```

### 3.3 Giới Hạn Slot Mỗi Stage

| Stage | Bước | Slot | Cơ Chế | Giải Thích |
|---|---|---|---|---|
| **A** | Deep Research & Transcript | **2** | `SemaphoreSlim(2)` | Tối đa 2 task gọi Gemini API cùng lúc. |
| **B** | Voiceover AI84 (mp3 + srt) | **3** | `SemaphoreSlim(3)` | Tối đa 3 task gọi AI84 TTS API đồng thời (rate-limit ~3-5 concurrent). |
| **C** | Scene Creator (scenes.json) | **4** | `SemaphoreSlim(4)` | Tối đa 4 task gọi Gemini Scene Creator cùng lúc. Step nhẹ, chạy được nhiều. |
| **D** | Image Generation (Flow Local) | **1** | `SemaphoreSlim(1)` | **TUẦN TỰ TUYỆT ĐỐI**. Mỗi task tạo 50+ ảnh với 6 luồng song song nội bộ. Sau khi xong, **nghỉ 15 giây** trước khi task tiếp theo vào. |

### 3.4 So Sánh: Chạy 1 Task vs Chạy Batch

| | Chạy 1 Task (nút `Run`) | Chạy Batch (nút `CHẠY TASK ĐÃ CHỌN`) |
|---|---|---|
| **Engine** | `GeminiVideoPipelineService` (tuần tự 5 step) | `PipelineOrchestrator` (ma trận 4 stage song song) |
| **Deep Research** | 1 task tại 1 thời điểm | Tối đa 2 task cùng lúc |
| **Voiceover** | 1 task | Tối đa 3 task cùng lúc |
| **Scene Creator** | 1 task | Tối đa 4 task cùng lúc |
| **Image Gen** | 1 task, 6 ảnh song song | 1 task, 6 ảnh song song + sleep 15s giữa các task |
| **Thời gian 6 tasks** | ~90–180 phút | ~35–60 phút |

### 3.5 Code Tham Khảo

```csharp
// Khởi tạo PipelineOrchestrator
var orchestrator = new PipelineOrchestrator(
    geminiApiService: _geminiApiService,
    configService: ConfigService.Instance,
    voiceoverStep: voiceoverStep,
    sceneBreakdownStep: sceneBreakdownStep,
    batchImageGenService: batchImageGenService,
    topicResearchStep: topicResearchStep,
    maxDeepResearch: 2,   // 2 task deep research cùng lúc
    maxVoiceover: 3,      // 3 task voiceover cùng lúc
    maxSceneCreator: 4,   // 4 task scene creator cùng lúc
    maxImageGen: 1        // 1 task image gen (tuần tự, sleep 15s)
);

// Chạy batch
var result = await orchestrator.ExecuteBatchAsync(
    taskModels: selectedTasks,
    logTask: (taskModel, msg) => Dispatcher.Invoke(() =>
    {
        AppendGeminiTaskLog(taskModel, msg);
        UpdateTaskStepInfoFromLog(taskModel, msg);
    })
);
```

---

## 📊 4. Cấu Trúc File & Dependencies (sau P4)

| File mới | Vai Trò |
|---|---|
| `src/AssetAutomator.Application/Services/PipelineOrchestrator.cs` | Orchestrator cho batch multi-task |
| `src/AssetAutomator.Application/Services/GeminiVideoPipelineService.cs` | Pipeline 5-step cho 1 task đơn lẻ (đã `[Obsolete]`) |
| `src/AssetAutomator.Application/Steps/GeminiTopicResearchStep.cs` | Step 1: Deep Research + Transcript |
| `src/AssetAutomator.Application/Steps/VoiceoverGenerationStep.cs` | Step 2: Voiceover AI84 + SRT |
| `src/AssetAutomator.Application/Steps/GeminiSceneBreakdownStep.cs` | Step 3: Scene Creator JSON (HTTP) |
| `src/AssetAutomator.Application/Steps/GeminiPlaywrightSceneBreakdownStep.cs` | Step 3 alt: qua Playwright |
| `src/AssetAutomator.Application/Steps/SceneImageBatchStep.cs` | Step 4: Batch Image Generation |
| `src/AssetAutomator.Application/Services/GeminiApiService.cs` | HTTP client tới Gemini Python REST Server |
| `src/AssetAutomator.Application/Services/BatchImageGenService.cs` | Factory cho Image Gen Provider |
| `src/AssetAutomator.UI/MainWindow.GeminiCreator.cs` | UI code-behind (tab Gemini AI Creator) |
