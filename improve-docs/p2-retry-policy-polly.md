# P2.4 — Retry policy với Polly cho external HTTP calls

> **Trạng thái:** ✅ Hoàn thành
> **Ngày:** 2026-07-29
> **Phạm vi:** Tích hợp `Microsoft.Extensions.Http.Resilience` + Polly v8 vào named HttpClient `ai84`,
> tạo helper `ResiliencePipelineDefaults` để chuẩn hóa pipeline retry/circuit-breaker/timeout.

---

## 🎯 Mục tiêu

External HTTP call (đặc biệt là AI84 TTS API) là các điểm failure thường gặp:
- Network blip (TCP reset, DNS timeout).
- Rate-limit 429/503 từ upstream.
- API tạm thời không khả dụng (5xx).

Cần wrap các call này trong Polly pipeline với:
1. **Retry với exponential backoff + jitter** — tránh thundering herd.
2. **Circuit breaker** — nếu upstream chết hẳn, ngưng gọi để bảo vệ tài nguyên & người dùng.
3. **Per-attempt timeout** — kill request treo, không để pipeline kẹt 30 phút.

---

## 📦 Thay đổi

### 1. NuGet
`AssetAutomator.csproj` — thêm:
```xml
<PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="9.0.0" />
```

### 2. Helper mới — `Services/Http/ResiliencePipelineDefaults.cs`
Định nghĩa 2 extension method cho `ResiliencePipelineBuilder<HttpResponseMessage>`:

- **`ConfigureStandardPipeline()`** — áp dụng cho call bình thường (POST submit, GET lookup).
  - Retry: 3 lần, exponential backoff `1s → 3s → 9s` + jitter ±500ms.
  - ShouldHandle: `HttpRequestException`, `TaskCanceledException`, response ≥ 500.
  - Circuit breaker: 50% failure ratio với min 5 request trong cửa sổ 30s → open 30s.
  - Timeout: 60s / attempt.

- **`ConfigureLongPollingPipeline()`** — áp dụng cho polling job status (AI84 async TTS).
  - Retry: 5 lần, exponential backoff `2s → 6s → 18s`.
  - Không circuit breaker (server đang respond, chỉ chậm).
  - Timeout: 120s / attempt.

### 3. App.xaml.cs — đăng ký named HttpClient "ai84" với resilience
```csharp
services.AddHttpClient("ai84", client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
})
.AddResilienceHandler("ai84", b => b.ConfigureStandardPipeline());
```

### 4. `VoiceoverGenerationStep.cs` — chuyển sang `IHttpClientFactory`
- Inject `IHttpClientFactory` qua constructor.
- Dùng `_httpClientFactory.CreateClient("ai84")` thay vì `new HttpClient()` thủ công.
- Thêm fallback `DefaultHttpClientFactory` (no-op) cho code path chưa có DI (legacy 5-step UI).
- Named client constant: `VoiceoverGenerationStep.Ai84HttpClientName = "ai84"`.

---

## 📂 File ảnh hưởng

| File | Thay đổi |
|---|---|
| `AssetAutomator.csproj` | +2 NuGet packages |
| `Services/Http/ResiliencePipelineDefaults.cs` | **Mới** |
| `App.xaml.cs` | Đăng ký named HttpClient với Polly pipeline |
| `Services/Steps/VoiceoverGenerationStep.cs` | Inject `IHttpClientFactory`, dùng named client |

---

## ✅ Verify

1. **Build:** `dotnet build` → 0 error (25 warnings, không liên quan Polly).
2. **Smoke test:** Chạy pipeline tạo voiceover bình thường → call tới `api.ai84.pro` đi qua Polly pipeline
   (log Polly: `OnRetry` không hiện nếu OK; hiện nếu mô phỏng lỗi).
3. **Negative test:** Tạm thời set DNS `api.ai84.pro` → 127.0.0.1 → pipeline sẽ retry 3 lần trước khi throw.

---

## 🔭 Bước tiếp theo (chưa làm trong task này)

- **Polly cho Playwright navigation**: Playwright có retry nội bộ nhưng selector timeout lặp đi lặp lại
  (đặc biệt với Gemini UI) có thể wrap trong Polly pipeline với delay riêng (chưa làm, deferred sang P3).
- **Đăng ký thêm named clients**: `services.AddHttpClient("gemini-api", ...)`,
  `services.AddHttpClient("image-api", ...)` cho các call API khác hiện đang dùng `new HttpClient()`
  raw trong `MainWindow.Tasks.cs` & `GeminiCreatorService.cs`.
- **Long-polling pipeline cho AI84 polling**: hiện vẫn dùng `ConfigureStandardPipeline`; sau này tách
  polling client dùng `ConfigureLongPollingPipeline()` (no circuit breaker).