# AI84 TTS API — Tài Liệu Tích Hợp

Tài liệu này mô tả chi tiết cách tích hợp **AI84 TTS API** để tạo voiceover và subtitles trong AssetAutomator và các ứng dụng bên thứ ba.

---

## 1. Tổng Quan Về AI84

**AI84** là dịch vụ Text-to-Speech (TTS) API tương thích với ElevenLabs API v2, cung cấp:
- **Async TTS**: Tạo audio từ text với job queue và polling
- **Transcript/Subtitles**: Sinh transcript SRT từ audio đã tạo
- **Shared Voices**: Thư viện giọng đọc chia sẻ có sẵn
- **Multi-language**: Hỗ trợ nhiều ngôn ngữ

### 1.1 Base URL

```
https://api.ai84.pro
```

### 1.2 Authentication

API Key được truyền qua header:

```http
xi-api-key: YOUR_API_KEY
```

### 1.3 Các Endpoint Chính

| Method | Endpoint | Mục đích |
|--------|----------|----------|
| `GET` | `/v1/shared-voices` | Liệt kê giọng đọc chia sẻ |
| `POST` | `/v2/text-to-speech/async` | Tạo job TTS (async) |
| `GET` | `/v2/text-to-speech/async/{job_id}` | Kiểm tra trạng thái job |

---

## 2. Voice Selection API

### 2.1 GET /v1/shared-voices

Liệt kê các giọng đọc chia sẻ có sẵn trong thư viện AI84.

**Request:**

```http
GET https://api.ai84.pro/v1/shared-voices?page_size=30&page=0&sort=trending&gender=female&language=en-US&search=professional
xi-api-key: YOUR_API_KEY
```

**Query Parameters:**

| Tham số | Kiểu | Mặc định | Mô tả |
|---------|------|----------|-------|
| `page_size` | int | 30 | Số giọng đọc mỗi trang (10, 20, 30, 50) |
| `page` | int | 0 | Số trang (0-indexed) |
| `sort` | string | trending | Thứ tự: `trending`, `popular`, `recent` |
| `gender` | string | - | Bộ lọc giới tính: `male`, `female` |
| `language` | string | - | Mã ngôn ngữ (VD: `en-US`, `vi-VN`) |
| `search` | string | - | Tìm kiếm theo tên/ID |

**Response (200 OK):**

```json
{
  "voices": [
    {
      "voice_id": "jessie",
      "name": "Jessie",
      "category": "ElevenLabs",
      "gender": "Female",
      "language": "en-US",
      "description": "Professional female voice with clear articulation"
    },
    {
      "voice_id": "en-US-Standard-A",
      "name": "Standard American Male",
      "category": "Standard",
      "gender": "Male",
      "language": "en-US",
      "description": "Clear, neutral American male voice"
    }
  ],
  "has_more": true,
  "last_sort_id": "abc123xyz"
}
```

**Response Model (C#):**

```csharp
public class SharedVoicesResponse
{
    public List<SharedVoiceInfo> voices { get; set; } = new();
    public bool has_more { get; set; }
    public string? last_sort_id { get; set; }
}

public class SharedVoiceInfo
{
    public string voice_id { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string category { get; set; } = string.Empty;
    public string gender { get; set; } = string.Empty;
    public string language { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
}
```

### 2.2 Code Mẫu

**cURL:**

```bash
curl -s "https://api.ai84.pro/v1/shared-voices?page_size=10" \
  -H "xi-api-key: YOUR_API_KEY"
```

**Python:**

```python
import requests

API_KEY = "YOUR_API_KEY"
url = "https://api.ai84.pro/v1/shared-voices"
headers = {"xi-api-key": API_KEY}
params = {
    "page_size": 30,
    "page": 0,
    "sort": "trending",
    "gender": "female",
    "language": "en-US"
}

response = requests.get(url, headers=headers, params=params)
data = response.json()

for voice in data["voices"]:
    print(f"{voice['name']} ({voice['voice_id']}) - {voice['language']}")
```

**JavaScript:**

```javascript
const API_KEY = "YOUR_API_KEY";
const url = new URL("https://api.ai84.pro/v1/shared-voices");
url.searchParams.set("page_size", "30");
url.searchParams.set("page", "0");
url.searchParams.set("sort", "trending");

const response = await fetch(url, {
  headers: { "xi-api-key": API_KEY }
});
const data = await response.json();

data.voices.forEach(voice => {
  console.log(`${voice.name} (${voice.voice_id}) - ${voice.language}`);
});
```

**C# (.NET):**

```csharp
using System.Net.Http;
using System.Net.Http.Json;

var client = new HttpClient();
client.DefaultRequestHeaders.Add("xi-api-key", "YOUR_API_KEY");

var url = "https://api.ai84.pro/v1/shared-voices?page_size=30&page=0&sort=trending";
var response = await client.GetFromJsonAsync<SharedVoicesResponse>(url);

foreach (var voice in response!.voices)
{
    Console.WriteLine($"{voice.Name} ({voice.VoiceId}) - {voice.Language}");
}

public record SharedVoicesResponse(List<SharedVoiceInfo> voices, bool has_more, string? last_sort_id);
public record SharedVoiceInfo(string voice_id, string name, string category, string gender, string language, string description);
```

---

## 3. Text-to-Speech Async API

### 3.1 POST /v2/text-to-speech/async

Tạo một job TTS không đồng bộ. Server sẽ xử lý và trả về `job_id` để theo dõi trạng thái.

**Request:**

```http
POST https://api.ai84.pro/v2/text-to-speech/async?voice_id=jessie
xi-api-key: YOUR_API_KEY
Content-Type: application/json
```

**Query Parameters:**

| Tham số | Bắt buộc | Mô tả |
|---------|----------|-------|
| `voice_id` | ✅ | ID của giọng đọc (VD: `jessie`, `en-US-Standard-A`) |

**Request Body:**

```json
{
  "text": "Welcome to Asset Automator! This is a sample voiceover text.",
  "voice_id": "jessie",
  "model_id": "eleven_v3",
  "with_transcript": true
}
```

| Tham số | Bắt buộc | Mặc định | Mô tả |
|---------|----------|----------|-------|
| `text` | ✅ | - | Nội dung text cần chuyển thành audio |
| `voice_id` | ✅ | - | ID giọng đọc (trùng với query param) |
| `model_id` | ❌ | eleven_v3 | Model TTS: `eleven_v3`, `eleven_v3_5`, `eleven_multilingual_v2` |
| `with_transcript` | ❌ | false | Tự động sinh transcript SRT kèm audio |

**Response (200 OK):**

```json
{
  "job_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
}
```

### 3.2 GET /v2/text-to-speech/async/{job_id}

Kiểm tra trạng thái của một job TTS.

**Request:**

```http
GET https://api.ai84.pro/v2/text-to-speech/async/a1b2c3d4-e5f6-7890-abcd-ef1234567890
xi-api-key: YOUR_API_KEY
```

**Response (Job Pending):**

```json
{
  "job": {
    "job_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "status": "queued"
  }
}
```

**Response (Job Processing):**

```json
{
  "job": {
    "job_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "status": "processing",
    "progress": 0.45
  }
}
```

**Response (Job Done):**

```json
{
  "job": {
    "job_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "status": "done",
    "audio_url": "https://cdn.ai84.pro/audio/abc123.mp3",
    "transcript_url": "https://cdn.ai84.pro/transcript/abc123.srt",
    "duration": 12.5,
    "audio_duration": 12.5
  }
}
```

**Response (Job Failed):**

```json
{
  "job": {
    "job_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "status": "failed",
    "error_message": "Text exceeds maximum length of 5000 characters"
  }
}
```

**Status Values:**

| Status | Ý nghĩa |
|--------|----------|
| `queued` | Job đang chờ xử lý |
| `processing` | Đang xử lý |
| `done` | Hoàn thành, có `audio_url` |
| `failed` | Thất bại, có `error_message` |

### 3.3 Polling Logic

Đây là pattern async nên cần polling để kiểm tra trạng thái:

**Polling Strategy:**

```python
import time

JOB_STATUS_URL = "https://api.ai84.pro/v2/text-to-speech/async/{job_id}"
POLL_DELAY_SECONDS = 3
MAX_POLL_ATTEMPTS = 100

def poll_job(job_id: str) -> dict:
    for attempt in range(MAX_POLL_ATTEMPTS):
        time.sleep(POLL_DELAY_SECONDS)
        
        response = requests.get(
            JOB_STATUS_URL.format(job_id=job_id),
            headers={"xi-api-key": API_KEY}
        )
        data = response.json()
        job = data["job"]
        
        status = job["status"]
        print(f"Attempt {attempt + 1}: Status = {status}")
        
        if status == "done":
            return job  # Contains audio_url, transcript_url, duration
        elif status == "failed":
            raise Exception(f"TTS Job failed: {job.get('error_message', 'Unknown error')}")
    
    raise Exception(f"TTS Job timed out after {MAX_POLL_ATTEMPTS} attempts")
```

### 3.4 Code Mẫu Hoàn Chỉnh

**Python:**

```python
import requests
import time
import json

API_KEY = "YOUR_API_KEY"
BASE_URL = "https://api.ai84.pro"

def create_tts_job(text: str, voice_id: str, with_transcript: bool = True) -> str:
    """Submit TTS job and return job_id."""
    url = f"{BASE_URL}/v2/text-to-speech/async?voice_id={voice_id}"
    headers = {
        "xi-api-key": API_KEY,
        "Content-Type": "application/json"
    }
    payload = {
        "text": text,
        "voice_id": voice_id,
        "model_id": "eleven_v3",
        "with_transcript": with_transcript
    }
    
    response = requests.post(url, headers=headers, json=payload)
    response.raise_for_status()
    
    data = response.json()
    return data["job_id"]

def poll_job_status(job_id: str, poll_delay: float = 3.0, max_attempts: int = 100) -> dict:
    """Poll until job is done or failed."""
    url = f"{BASE_URL}/v2/text-to-speech/async/{job_id}"
    headers = {"xi-api-key": API_KEY}
    
    for attempt in range(max_attempts):
        time.sleep(poll_delay)
        
        response = requests.get(url, headers=headers)
        response.raise_for_status()
        
        job = response.json()["job"]
        status = job["status"]
        
        if status == "done":
            return job
        elif status == "failed":
            raise Exception(f"TTS Job failed: {job.get('error_message')}")
        
        print(f"Polling attempt {attempt + 1}: {status}")
    
    raise Exception("TTS Job timed out")

def download_file(url: str, output_path: str):
    """Download audio or transcript file."""
    response = requests.get(url, stream=True)
    response.raise_for_status()
    
    with open(output_path, "wb") as f:
        for chunk in response.iter_content(chunk_size=8192):
            f.write(chunk)

def text_to_speech(text: str, voice_id: str, output_dir: str):
    """Complete TTS workflow."""
    print(f"Creating TTS job for voice: {voice_id}")
    job_id = create_tts_job(text, voice_id, with_transcript=True)
    print(f"Job created: {job_id}")
    
    print("Polling for completion...")
    result = poll_job_status(job_id)
    
    audio_url = result["audio_url"]
    transcript_url = result.get("transcript_url")
    duration = result.get("duration", result.get("audio_duration", 0))
    
    print(f"Job done! Duration: {duration}s")
    print(f"Downloading audio from: {audio_url}")
    download_file(audio_url, f"{output_dir}/voiceover.mp3")
    
    if transcript_url:
        print(f"Downloading transcript from: {transcript_url}")
        download_file(transcript_url, f"{output_dir}/voiceover.srt")
    
    return {"audio": f"{output_dir}/voiceover.mp3", "transcript": f"{output_dir}/voiceover.srt"}

# Usage
result = text_to_speech(
    text="Welcome to Asset Automator! This is a sample voiceover.",
    voice_id="jessie",
    output_dir="./output"
)
print(f"Generated files: {result}")
```

**JavaScript:**

```javascript
const API_KEY = "YOUR_API_KEY";
const BASE_URL = "https://api.ai84.pro";

async function createTtsJob(text, voiceId, withTranscript = true) {
  const url = `${BASE_URL}/v2/text-to-speech/async?voice_id=${encodeURIComponent(voiceId)}`;
  
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "xi-api-key": API_KEY,
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      text,
      voice_id: voiceId,
      model_id: "eleven_v3",
      with_transcript
    })
  });
  
  if (!response.ok) {
    throw new Error(`Failed to create job: ${response.status}`);
  }
  
  const data = await response.json();
  return data.job_id;
}

async function pollJobStatus(jobId, pollDelayMs = 3000, maxAttempts = 100) {
  const url = `${BASE_URL}/v2/text-to-speech/async/${jobId}`;
  
  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    await new Promise(resolve => setTimeout(resolve, pollDelayMs));
    
    const response = await fetch(url, {
      headers: { "xi-api-key": API_KEY }
    });
    
    if (!response.ok) {
      throw new Error(`Poll failed: ${response.status}`);
    }
    
    const data = await response.json();
    const job = data.job;
    const status = job.status;
    
    console.log(`Attempt ${attempt + 1}: ${status}`);
    
    if (status === "done") {
      return job;
    } else if (status === "failed") {
      throw new Error(`TTS Job failed: ${job.error_message}`);
    }
  }
  
  throw new Error("TTS Job timed out");
}

async function downloadFile(url, outputPath) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`Download failed: ${response.status}`);
  
  const blob = await response.blob();
  const fs = require("fs");
  const buffer = Buffer.from(await blob.arrayBuffer());
  fs.writeFileSync(outputPath, buffer);
}

async function textToSpeech(text, voiceId, outputDir) {
  console.log(`Creating TTS job for voice: ${voiceId}`);
  const jobId = await createTtsJob(text, voiceId, true);
  console.log(`Job created: ${jobId}`);
  
  console.log("Polling for completion...");
  const result = await pollJobStatus(jobId);
  
  console.log(`Job done! Duration: ${result.duration}s`);
  
  const audioPath = `${outputDir}/voiceover.mp3`;
  console.log(`Downloading audio to: ${audioPath}`);
  await downloadFile(result.audio_url, audioPath);
  
  if (result.transcript_url) {
    const transcriptPath = `${outputDir}/voiceover.srt`;
    console.log(`Downloading transcript to: ${transcriptPath}`);
    await downloadFile(result.transcript_url, transcriptPath);
  }
  
  return { audio: audioPath };
}

// Usage
textToSpeech(
  "Welcome to Asset Automator! This is a sample voiceover.",
  "jessie",
  "./output"
).then(result => console.log("Generated:", result));
```

**cURL:**

```bash
#!/bin/bash
# Complete TTS workflow with polling

API_KEY="YOUR_API_KEY"
TEXT="Welcome to Asset Automator! This is a sample voiceover."
VOICE_ID="jessie"

# Step 1: Create job
echo "Creating TTS job..."
JOB_RESPONSE=$(curl -s -X POST "https://api.ai84.pro/v2/text-to-speech/async?voice_id=${VOICE_ID}" \
  -H "xi-api-key: ${API_KEY}" \
  -H "Content-Type: application/json" \
  -d "{\"text\":\"${TEXT}\",\"voice_id\":\"${VOICE_ID}\",\"model_id\":\"eleven_v3\",\"with_transcript\":true}")

JOB_ID=$(echo $JOB_RESPONSE | jq -r '.job_id')
echo "Job ID: $JOB_ID"

# Step 2: Poll for completion
echo "Polling for completion..."
while true; do
  sleep 3
  STATUS_RESPONSE=$(curl -s "https://api.ai84.pro/v2/text-to-speech/async/${JOB_ID}" \
    -H "xi-api-key: ${API_KEY}")
  
  STATUS=$(echo $STATUS_RESPONSE | jq -r '.job.status')
  echo "Status: $STATUS"
  
  if [ "$STATUS" == "done" ]; then
    AUDIO_URL=$(echo $STATUS_RESPONSE | jq -r '.job.audio_url')
    TRANSCRIPT_URL=$(echo $STATUS_RESPONSE | jq -r '.job.transcript_url')
    echo "Audio URL: $AUDIO_URL"
    echo "Transcript URL: $TRANSCRIPT_URL"
    
    # Download files
    curl -s "$AUDIO_URL" -o "output/voiceover.mp3"
    echo "Downloaded audio to output/voiceover.mp3"
    
    if [ "$TRANSCRIPT_URL" != "null" ] && [ -n "$TRANSCRIPT_URL" ]; then
      curl -s "$TRANSCRIPT_URL" -o "output/voiceover.srt"
      echo "Downloaded transcript to output/voiceover.srt"
    fi
    break
  elif [ "$STATUS" == "failed" ]; then
    ERROR=$(echo $STATUS_RESPONSE | jq -r '.job.error_message')
    echo "Job failed: $ERROR"
    exit 1
  fi
done

echo "Done!"
```

---

## 4. Caching & Performance

### 4.1 In-Memory Cache cho Shared Voices

AssetAutomator sử dụng caching để tránh spam API khi user flip-flop giữa các filter:

```csharp
// Bounded cache: 64 entries, 5-minute TTL
private static readonly ConcurrentDictionary<string, CachedVoiceResponse> SharedVoicesCache = new();
private static readonly TimeSpan VoiceCacheTtl = TimeSpan.FromMinutes(5);
private const int VoiceCacheMaxEntries = 64;

public async Task<SharedVoicesResponse> GetVoicesWithCacheAsync(string url)
{
    if (SharedVoicesCache.TryGetValue(url, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
    {
        return cached.Value;
    }
    
    var response = await httpClient.GetAsync(url);
    var result = await response.Content.ReadFromJsonAsync<SharedVoicesResponse>();
    
    // Store in cache
    if (SharedVoicesCache.Count >= VoiceCacheMaxEntries)
    {
        // Evict oldest/expired entry
    }
    SharedVoicesCache[url] = new CachedVoiceResponse {
        Value = result,
        ExpiresAt = DateTime.UtcNow + VoiceCacheTtl
    };
    
    return result;
}
```

---

## 5. Resilience & Error Handling

### 5.1 HttpClient Configuration

AssetAutomator sử dụng **Polly v8** cho resilience với 2 pipeline riêng biệt:

| Pipeline | Mục đích | Timeout | Retry | Circuit Breaker |
|----------|----------|---------|-------|-----------------|
| `ai84` (standard) | Lookup/Submit/Download | 60s | 3 attempts, exp. backoff | Yes (opens after failures) |
| `ai84-polling` | Job status polling | 120s | 5 attempts, exp. backoff | No |

### 5.2 Standard Pipeline Configuration

```csharp
// For normal HTTP calls: lookup, submit, download
.AddResilienceHandler("ai84-std", builder =>
{
    // Per-attempt timeout
    builder.AddTimeout(TimeSpan.FromSeconds(60));
    
    // Retry with exponential backoff + jitter
    builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(10),
        UseJitter = true,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .HandleResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.TooManyRequests)
    });
    
    // Circuit breaker
    builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
    {
        FailureRatio = 0.5,
        MinimumThroughput = 5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(30)
    });
});
```

### 5.3 Long-Polling Pipeline Configuration

```csharp
// For job-status polling: no circuit breaker
.AddResilienceHandler("ai84-polling", builder =>
{
    builder.AddTimeout(TimeSpan.FromSeconds(120));
    
    builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 5,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromSeconds(20),
        UseJitter = true
    });
});
```

### 5.4 Error Codes

| HTTP Status | Ý nghĩa | Xử lý |
|-------------|----------|-------|
| 200 | Thành công | Parse response |
| 400 | Bad Request | Kiểm tra request body |
| 401 | Unauthorized | Kiểm tra API key |
| 403 | Forbidden | API key không có quyền |
| 404 | Not Found | Voice ID không tồn tại |
| 429 | Rate Limited | Đợi và retry |
| 500 | Server Error | Retry với backoff |
| 503 | Service Unavailable | Retry sau |

---

## 6. Integration Pattern trong AssetAutomator

### 6.1 DI Registration (App.xaml.cs)

```csharp
// Standard AI84 client: lookup, submit, download
services.AddHttpClient(VoiceoverGenerationStep.Ai84HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.ai84.pro/");
    client.Timeout = TimeSpan.FromSeconds(120);
})
.AddResilienceHandler("ai84-std", builder =>
{
    ResiliencePipelineDefaults.ConfigureStandardPipeline(builder);
});

// Long-polling AI84 client: job status
services.AddHttpClient(VoiceoverGenerationStep.Ai84PollingHttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.ai84.pro/");
    client.Timeout = TimeSpan.FromSeconds(300); // 5 min for long polls
})
.AddResilienceHandler("ai84-polling", builder =>
{
    ResiliencePipelineDefaults.ConfigureLongPollingPipeline(builder);
});
```

### 6.2 Usage trong Step

```csharp
public class VoiceoverGenerationStep
{
    private readonly IHttpClientFactory _httpClientFactory;
    
    public async Task ExecuteAsync(...)
    {
        // Submit job
        var submitClient = _httpClientFactory.CreateClient(Ai84HttpClientName);
        submitClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        
        var response = await submitClient.PostAsync(submitUrl, content);
        
        // Poll for status
        var pollingClient = _httpClientFactory.CreateClient(Ai84PollingHttpClientName);
        
        while (!isComplete)
        {
            var status = await pollingClient.GetAsync(statusUrl);
            // ...
        }
    }
}
```

---

## 7. Best Practices

### 7.1 API Key Management

1. **Environment Variable**: Ưu tiên dùng biến môi trường `AI84_API_KEY`
2. **Config File**: Hoặc file `ai84_api_key.txt` trong thư mục app
3. **Never hardcode**: Không để API key trong source code

```csharp
// ConfigService.cs
string? envAi84Key = Environment.GetEnvironmentVariable("AI84_API_KEY");
if (!string.IsNullOrEmpty(envAi84Key))
{
    settings.Ai84ApiKey = envAi84Key.Trim();
}

// Hoặc đọc từ file
string ai84Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai84_api_key.txt");
if (File.Exists(ai84Path))
{
    settings.Ai84ApiKey = File.ReadAllText(ai84Path).Trim();
}
```

### 7.2 Polling Best Practices

1. **Start with short delay**: 3-5 giây ban đầu
2. **Increase delay**: Tăng dần nếu job lâu
3. **Set max attempts**: Tránh infinite loop
4. **Handle timeout**: Gracefully fail sau max attempts

```python
# Adaptive polling
def poll_with_backoff(job_id, base_delay=3, max_delay=30, max_attempts=100):
    delay = base_delay
    for attempt in range(max_attempts):
        status = check_status(job_id)
        
        if status == "done":
            return status
        elif status == "failed":
            raise Exception(status["error_message"])
        
        # Exponential backoff with cap
        time.sleep(delay)
        delay = min(delay * 1.5, max_delay)
```

### 7.3 Transcript/Subtitle Handling

1. **Prefer `with_transcript=true`**: Nếu API hỗ trợ
2. **Fallback to Whisper**: Nếu `transcript_url` không có sau khi audio xong
3. **Cache SRT locally**: Không cần regenerate

---

## 8. Limitations & Gotchas

### 8.1 Known Limitations

1. **Text Length**: Giới hạn ~5000 ký tự mỗi request
2. **Polling Timeout**: Job có thể chờ lâu (đặc biệt với transcript)
3. **CDN Expiry**: Audio URLs có thể hết hạn sau một thời gian
4. **API Changes**: AI84 API có thể thay đổi mà không báo trước

### 8.2 Transcript URL Timing

Transcript URL có thể không có ngay khi audio xong:

```csharp
// AssetAutomator's approach: poll separately for transcript
if (withTranscript && string.IsNullOrEmpty(transcriptUrl))
{
    // Estimate max polling based on audio duration
    double maxPollingSeconds = duration / 1.5;
    maxPollingSeconds = Math.Max(5, maxPollingSeconds);
    
    // Poll for transcript separately
    var startTime = DateTime.UtcNow;
    while ((DateTime.UtcNow - startTime).TotalSeconds < maxPollingSeconds)
    {
        await Task.Delay(Delays.PageRenderDelayMs);
        // Check transcript_url again
    }
    
    // Fallback to Whisper if still not available
}
```

---

## 9. Tài Liệu Tham Khảo

- **AssetAutomator Source**: `src/AssetAutomator.Application/Steps/VoiceoverGenerationStep.cs`
- **Resilience Config**: `src/AssetAutomator.Infrastructure/Http/ResiliencePipelineDefaults.cs`
- **Voice Selector**: `src/AssetAutomator.WinUI/Views/Dialogs/VoiceSelectorDialog.xaml.cs`
- **Models**: `src/AssetAutomator.Core/Models/SharedVoiceModels.cs`

---

## 10. Quick Reference

### 10.1 Endpoints Summary

```
GET    https://api.ai84.pro/v1/shared-voices                     # List voices
POST   https://api.ai84.pro/v2/text-to-speech/async?voice_id=X  # Create TTS job
GET    https://api.ai84.pro/v2/text-to-speech/async/{job_id}    # Check job status
```

### 10.2 Headers

```
xi-api-key: YOUR_API_KEY
Content-Type: application/json
```

### 10.3 TTS Request Body

```json
{
  "text": "Your text here",
  "voice_id": "voice_id",
  "model_id": "eleven_v3",
  "with_transcript": true
}
```

### 10.4 Job Response

```json
{
  "job": {
    "job_id": "...",
    "status": "queued|processing|done|failed",
    "audio_url": "https://cdn...",
    "transcript_url": "https://cdn...",
    "duration": 10.5,
    "error_message": "..."
  }
}
```

---

*Tài liệu cập nhật đến 22/08/2026. Thông tin chi tiết được trích xuất từ AssetAutomator source code.*
