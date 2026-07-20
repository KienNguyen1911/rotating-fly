# AutoCreateImage - Clean Architecture & SOLID Refactored

Hệ thống tự động hóa quy trình tải hình thu nhỏ (thumbnail), dịch thuật kịch bản thông qua AI, chuyển đổi văn bản thành giọng nói (TTS) và tạo video tự động tích hợp quản lý Chrome Profiles. Codebase đã được tái cấu trúc triệt để theo mô hình **Clean Architecture**, tuân thủ nguyên lý **SOLID** và **DRY**.

---

## 📁 Cấu trúc Thư mục & Chức năng

```
AutoCreateImage/
├── Converters/               # Các bộ chuyển đổi dữ liệu hiển thị (XAML Data Binding)
│   └── YoutubeUrlConverter.cs  # Chuẩn hóa đường dẫn YouTube URL sang dạng ID 11 ký tự
│
├── Core/                     # Cấu hình tĩnh và hằng số toàn hệ thống
│   └── AppConstants.cs         # Định nghĩa danh sách ngôn ngữ và cài đặt mặc định
│
├── Helpers/                  # Tiện ích bổ trợ phi trạng thái (Stateless Utilities)
│   ├── HumanBehaviourHelper.cs # Giả lập hành vi rê chuột, gõ phím tự nhiên để tránh Bot Detection
│   └── YoutubeHelper.cs        # Trích xuất Video ID và xử lý thư mục đầu ra (OutputDir) dùng chung
│
├── Models/                   # Định nghĩa các cấu trúc dữ liệu và thực thể (Domain Entities)
│   ├── AppSettings.cs          # Model cấu hình ứng dụng (appsettings.json)
│   ├── AutomationTask.cs       # Đối tượng Task chính chạy trong luồng xử lý tự động
│   ├── HistoryTaskModel.cs     # Model lưu trữ thông tin lịch sử tác vụ
│   ├── ImageGenRequest.cs      # Thực thể yêu cầu sinh ảnh gửi vào hàng đợi xử lý song song
│   └── SharedVoiceModels.cs    # Định nghĩa cấu trúc dữ liệu giọng nói từ API
│
├── Scripts/                  # Chứa kịch bản xử lý bên ngoài
│   └── media_generator.py      # Python script tích hợp MoviePy ghép nhạc, chạy chữ và tạo video
│
├── Services/                 # Tầng nghiệp vụ xử lý chính (Business Logic Services)
│   ├── Steps/                  # Chia nhỏ 5 bước xử lý nghiệp vụ tự động tuần tự (SRP & OCP)
│   │   ├── ThumbnailDownloadStep.cs    # Step 1: Tải ảnh thu nhỏ (thumbnail) YouTube
│   │   ├── TranscriptExtractionStep.cs # Step 2: Trích xuất transcript từ phụ đề YouTube
│   │   ├── ChatGptRewriteStep.cs       # Step 3: Dùng Playwright giả lập đẩy transcript lên ChatGPT dịch kịch bản
│   │   ├── VoiceoverGenerationStep.cs  # Step 4: Gọi API TTS AI84 sinh file giọng nói và SRT Whisper
│   │   └── ImageGenerationStep.cs      # Step 5: Gọi API xử lý ảnh (Legacy / G-Labs) qua Pool Service
│   │
│   ├── BrowserService.cs       # Quản lý vòng đời trình duyệt Playwright, nhân bản Chrome Profile và dọn dẹp bộ nhớ tạm
│   ├── ChatGptService.cs       # Tập hợp các thao tác tương tác giao diện ChatGPT (Kéo thả file Base64, gửi lệnh, trích xuất text)
│   ├── ConfigService.cs        # Đọc/ghi cấu hình ứng dụng từ file appsettings.json
│   ├── HistoryService.cs       # Lưu trữ, tải lại và xóa dữ liệu lịch sử tác vụ theo từng ngày
│   └── ImagePoolService.cs     # Điều phối hàng đợi sinh hình ảnh bất đồng bộ với cơ chế Worker giới hạn luồng song song
│
├── Windows/                  # Giao diện người dùng (WPF UI Views)
│   ├── MainWindow.xaml + .cs        # Cửa sổ điều khiển chính (gắn kết UI với các Services)
│   ├── BulkTaskWindow.xaml + .cs    # Giao diện thêm danh sách hàng loạt tác vụ
│   ├── VoiceSelectorWindow.xaml + .cs# Giao diện lọc và lựa chọn giọng nói từ thư viện AI84
│   └── WebViewLoginWindow.xaml + .cs # Giao diện đăng nhập tài khoản bằng WebView2
│
├── App.xaml + App.xaml.cs    # Cấu hình khởi tạo và điểm khởi chạy của ứng dụng WPF
├── AssemblyInfo.cs           # Khai báo thông tin và phiên bản Assembly của ứng dụng
├── AutoCreateImage.csproj    # File dự án MSBuild định nghĩa các gói phụ thuộc (.NET 10.0-windows)
├── free-proxies.json         # Danh sách proxy dự phòng dùng khi gọi API
└── requirement.md            # Tài liệu yêu cầu chức năng và sơ đồ luồng dữ liệu
```

---

## 🛠️ Công nghệ Sử dụng

* **UI Framework:** WPF (.NET 10)
* **Automation:** Microsoft Playwright for .NET (Điều khiển Chrome và giả lập người dùng)
* **API Integration:** YoutubeExplode (Trích xuất phụ đề tốc độ cao), HttpClient (Kết nối AI84 TTS & Image API)
* **Media Rendering:** Python + MoviePy + Pillow (Chạy thông qua Process Command Line gọi script `media_generator.py`)
