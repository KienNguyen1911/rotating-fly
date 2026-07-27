# 📖 Tài Liệu Hướng Dẫn & Kiến Trúc Hoạt Động: Gemini AI Creator

Tài liệu này giải thích chi tiết kiến trúc tổng quan và quy trình xử lý **5 bước (5-Step Pipeline)** của công cụ **Gemini AI Creator** trong dự án **AssetAutomator**.

---

## ⚙️ 1. Tổng Quan Kiến Trúc

**Gemini AI Creator** là quy trình tự động hóa sản xuất Video ngắn/dài toàn diện dựa trên mô hình **Gemini AI (Google DeepMind)** kết hợp với **AI84 / ElevenLabs TTS** và các **Provider sinh ảnh AI (FLUX / Glabs)**.

```
[Nhập Chủ Đề / Link YouTube]
         │
         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 1: Deep Research (Report) ➔ Script (Transcript)   │ ──► Xuất: research_report.txt, transcript.txt
└────────────────────────┬───────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 2: Tạo Giọng Đọc & Phụ Đề (AI84/ElevenLabs)      │ ──► Xuất: audio.mp3, subtitles.srt
└────────────────────────┬───────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 3: Phân Cảnh & Prompt Ảnh (Gemini Scene Gem)    │ ──► Xuất: scenes.json
└────────────────────────┬───────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 4: Sinh Ảnh Cảnh Hàng Loạt (+ Character Ref)    │ ──► Xuất: img/scene_01.png, img/scene_02.png...
└────────────────────────┬───────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────┐
│  STEP 5: Đóng Gói Tài Nguyên & Hoàn Tất (Export)       │ ──► Thư mục: Outputs/<id_task>/ (img/)
└────────────────────────┴───────────────────────────────┘
```

---

## 🔍 2. Chi Tiết Các Bước Trong Quy Trình (5-Step Workflow)

### 🔴 STEP 1: Gemini Deep Research & Tạo Kịch Bản (`GeminiTopicResearchStep`)
* **Mục đích**: Nghiên cứu sâu về chủ đề đầu vào và chuyển đổi báo cáo nghiên cứu thành kịch bản Video hoàn chỉnh.
* **Đầu vào**:
  * **Chủ Đề / Link YouTube**: Tên chủ đề hoặc URL video tham khảo.
  * **Scriptwriter Gem ID**: Tùy chọn Custom Gem chuyên viết kịch bản.
  * **Enable Deep Research (Bật/Tắt)**: Cho phép Gemini nghiên cứu đa chiều trên Web.
* **Cách hoạt động (Quy trình 2 Prompt khi bật Deep Research)**:
  1. **Prompt 1 (Nghiên cứu sâu)**: Gửi request tới Gemini Gem để tổng hợp một bản **Báo cáo nghiên cứu chuyên sâu (Research Report)** chi tiết nhiều số liệu và góc nhìn. Bản report này được lưu thành file `research_report.txt`.
  2. **Prompt 2 (Chuyển đổi kịch bản)**: Nhận bản report trên và gửi tiếp prompt tiêu chuẩn:
     > *"Write a complete, high-quality script of approximately 1000 - 1200 words based on these guidelines. Remember: output ONLY the spoken words"*
  3. Trích xuất đúng lời đọc kịch bản thuần túy và lưu về máy.
* **Kết quả đầu ra**: File `research_report.txt` (bản nghiên cứu) và file `transcript.txt` (kịch bản lời đọc hoàn chỉnh).

---

### 🟡 STEP 2: Sinh Giọng Đọc Voiceover & Phụ Đề SRT (`VoiceoverGenerationStep`)
* **Mục đích**: Chuyển đổi kịch bản văn bản thành file âm thanh Voiceover chất lượng cao và tạo file phụ đề SRT tương ứng.
* **Đầu vào**:
  * File `transcript.txt` từ Step 1.
  * **Voice ID**: Mã giọng đọc chọn từ thư viện AI84 / ElevenLabs.
  * **AI84 API Key**: Được cấu hình sẵn trong phần Cài đặt.
* **Cách hoạt động**:
  1. Tách `transcript.txt` thành các đoạn văn bản tối ưu cho TTS.
  2. Gọi API **AI84 / ElevenLabs** để chuyển dòng đọc sang dạng sóng âm thanh (MP3).
  3. Ghép các tệp âm thanh thành file Voiceover duy nhất `audio.mp3`.
  4. Trích xuất mốc thời gian (timestamps) chính xác từng từ để xuất file phụ đề `subtitles.srt`.
* **Kết quả đầu ra**: File `audio.mp3` và `subtitles.srt`.

---

### 🟢 STEP 3: Phân Cảnh & Sinh Prompt Chi Tiết (`GeminiSceneBreakdownStep`)
* **Mục đích**: Chia nhỏ video thành các phân cảnh visual (scenes) khớp với mốc thời gian phụ đề và viết prompt minh họa cho AI Image Gen.
* **Đầu vào**:
  * File `transcript.txt` và `subtitles.srt`.
  * **Scene Creator Gem ID**: Custom Gem chuyên phân tích khung hình & tạo prompt ảnh nghệ thuật.
* **Cách hoạt động**:
  1. Gemini đọc toàn bộ kịch bản và dòng phụ đề theo thời gian.
  2. Tính toán chia kịch bản thành các Scene (mỗi cảnh kéo dài 3 - 6 giây).
  3. Với mỗi Scene, Gemini tự động viết một **Visual Image Prompt** tiếng Anh chi tiết (mô tả góc quay, ánh sáng, đối tượng, chất liệu, tâm trạng).
* **Kết quả đầu ra**: File cấu trúc `scenes.json` chứa thông tin chi tiết từng phân cảnh.

---

### 🔵 STEP 4: Sinh Ảnh Cảnh Hàng Loạt Kèm Character Reference (`SceneImageBatchStep`)
* **Mục đích**: Tự động gọi mô hình sinh ảnh AI để tạo hình ảnh minh họa chất lượng cao cho tất cả các cảnh, duy trì tính nhất quán nhân vật qua **Character Reference**.
* **Đầu vào**:
  * File `scenes.json` từ Step 3.
  * **Character Ref**: Ô nhập Character Reference (mô tả nhân vật / đường dẫn ảnh nhân vật mẫu) trực tiếp trên DataGrid UI để giữ nhân vật đồng nhất giữa các cảnh.
  * **Provider Sinh Ảnh**: Chọn giữa `flow_local` (Server FLUX/SD local) hoặc `glabs`.
* **Cách hoạt động**:
  1. Đọc danh sách các cảnh trong `scenes.json` và cấu hình **Character Ref**.
  2. Gửi đồng thời/tuần tự các Visual Prompts (kèm Character Reference) tới Server sinh ảnh (`ImageGenerationStep`).
  3. Tải về và lưu toàn bộ ảnh vào **thư mục con `img/`** (ví dụ: `img/scene_01.png`, `img/scene_02.png`...).
  4. Cập nhật đường dẫn ảnh trực tiếp vào file `scenes.json`.
* **Kết quả đầu ra**: Các file ảnh minh họa lưu trong thư mục con `img/` của task.

---

### 🟣 STEP 5: Đóng Gói Tài Nguyên & Hoàn Tất (`Export & Packaging`)
* **Mục đích**: Kiểm tra tính toàn vẹn của dữ liệu và xuất kết quả ra thư mục chỉ định theo `id_task`.
* **Cấu trúc thư mục xuất ra (`Outputs/<id_task>/`)**:
  ```
  📁 Outputs/<id_task>/
  ├── 📄 research_report.txt (Báo cáo nghiên cứu sâu nếu bật Deep Research)
  ├── 📄 transcript.txt      (Kịch bản lời đọc hoàn chỉnh)
  ├── 🎵 audio.mp3           (Giọng đọc Voiceover chuẩn)
  ├── 📜 subtitles.srt       (Phụ đề SRT đồng bộ thời gian)
  ├── 📊 scenes.json         (Cấu trúc dữ liệu phân cảnh & prompt)
  └── 📁 img/                (Thư mục con chứa bộ ảnh AI minh họa)
      ├── 🖼️ scene_01.png
      ├── 🖼️ scene_02.png
      └── ...
  ```
* **Cập nhật giao diện**: Đổi Badge trạng thái thành **`✔️ Hoàn thành`** và cho phép ấn nút `Assets` để mở xem `scenes.json` hoặc mở thư mục output ngay lập tức.
