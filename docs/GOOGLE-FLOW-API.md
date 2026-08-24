# Google Flow Image Generation API — Tài Liệu Tích Hợp

Tài liệu này mô tả chi tiết cách tận dụng API tạo ảnh của **Google Flow** (`labs.google/fx/tools/flow`) để tích hợp vào ứng dụng bên thứ ba, thay vì phải thao tác thủ công trên giao diện web.

---

## 1. Tổng Quan Về Google Flow

**Google Flow** là công cụ tạo ảnh AI chính thức của Google, sử dụng các model Nano Banana (Gemini Image). Google Flow hiện **KHÔNG cung cấp API công khai chính thức** dành cho developer — nó chỉ có giao diện web tại `https://labs.google/fx/tools/flow`.

Để tích hợp vào app khác, có **3 con đường** chính:

| # | Con đường | Ưu điểm | Nhược điểm |
|---|-----------|----------|------------|
| 1 | **Google Interactions API (Gemini API)** chính thức | Chính thức, ổn định, có SDK Python/JS/Go | Tính credit per-image, cần Google Cloud billing |
| 2 | **useapi.net wrapper** (bên thứ ba) | Mirror Flow `/v1/google-flow/images`, dùng tài khoản Flow thường (kể cả free) | Tốn phí subscription bên thứ ba |
| 3 | **Reverse-engineer labs.google session** (whisk-proxy, flow-proxy, v.v.) | Miễn phí, dùng tài khoản Flow cá nhân | Không chính thức, dễ vỡ khi Google cập nhật |

> 💡 Hiện tại AssetAutomator (`FLOW-API.md`) đang dùng con đường #3 với local proxy. Tài liệu này tổng hợp cả 3 con đường để bạn chọn.

---

## 2. Con Đường #1 — Google Interactions API (Chính Thức)

Google cung cấp các model image (gọi chung là **Nano Banana**) qua Gemini API thông qua endpoint **Interactions**.

### 2.1. Models Khả Dụng

| Tên hiển thị | Model ID | Đặc điểm |
|--------------|----------|----------|
| Nano Banana 2 Lite | `gemini-3.1-flash-lite-image` | Nhanh nhất, rẻ nhất, throughput cao |
| Nano Banana 2 | `gemini-3.1-flash-image` | Workhorse đa năng, hỗ trợ 4K, multi-reference |
| Nano Banana Pro | `gemini-3-pro-image` | Chất lượng cao nhất, advanced reasoning |
| Nano Banana (legacy) | `gemini-2.5-flash-image` | Phiên bản cũ, nên nâng cấp |

Tất cả ảnh sinh ra đều có **SynthID watermark** ẩn.

### 2.2. Authentication

Lấy API key tại: https://aistudio.google.com/apikey

```http
x-goog-api-key: $GEMINI_API_KEY
Content-Type: application/json
```

### 2.3. Endpoint

```
POST https://generativelanguage.googleapis.com/v1beta/interactions
```

### 2.4. Text-to-Image (Sinh ảnh từ text)

**cURL:**
```bash
curl -s -X POST "https://generativelanguage.googleapis.com/v1beta/interactions" \
  -H "x-goog-api-key: $GEMINI_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemini-3.1-flash-image",
    "input": [
      {"type": "text", "text": "Create a picture of a nano banana dish in a fancy restaurant with a Gemini theme"}
    ]
  }'
```

**Python:**
```python
from google import genai
import base64

client = genai.Client()

interaction = client.interactions.create(
    model="gemini-3.1-flash-image",
    input="Create a picture of a nano banana dish in a fancy restaurant with a Gemini theme",
)

with open("generated_image.png", "wb") as f:
    f.write(base64.b64decode(interaction.output_image.data))
```

**JavaScript:**
```javascript
import { GoogleGenAI } from "@google/genai";
import * as fs from "node:fs";

const ai = new GoogleGenAI({});
const interaction = await ai.interactions.create({
  model: "gemini-3.1-flash-image",
  input: "Create a picture of a nano banana dish in a fancy restaurant with a Gemini theme",
});

if (interaction.output_image) {
  const buffer = Buffer.from(interaction.output_image.data, "base64");
  fs.writeFileSync("gemini-native-image.png", buffer);
}
```

### 2.5. Image Editing (Text + Image → Image)

**cURL:**
```bash
curl -s -X POST "https://generativelanguage.googleapis.com/v1beta/interactions" \
  -H "x-goog-api-key: $GEMINI_API_KEY" \
  -H "Content-Type: application/json" \
  -d "{
    \"model\": \"gemini-3.1-flash-image\",
    \"input\": [
      {\"type\": \"text\", \"text\": \"Create a picture of my cat eating a nano-banana in a fancy restaurant under the Gemini constellation\"},
      {
        \"type\": \"image\",
        \"mime_type\": \"image/jpeg\",
        \"data\": \"<BASE64_IMAGE_DATA>\"
      }
    ]
  }"
```

**Python:**
```python
from google import genai
import base64

client = genai.Client()

with open("/path/to/cat_image.png", "rb") as f:
    image_bytes = f.read()

interaction = client.interactions.create(
    model="gemini-3.1-flash-image",
    input=[
        {"type": "text", "text": "Create a picture of my cat eating a nano-banana in a fancy restaurant under the Gemini theme"},
        {
            "type": "image",
            "data": base64.b64encode(image_bytes).decode("utf-8"),
            "mime_type": "image/png",
        },
    ],
)

with open("generated_image.png", "wb") as f:
    f.write(base64.b64decode(interaction.output_image.data))
```

### 2.6. Response Format

Response trả về chứa property `output_image` với `data` (base64) là ảnh mới nhất sinh ra. Với multi-turn:

```python
interaction_2 = client.interactions.create(
    model="gemini-3.1-flash-image",
    input="Update this infographic to be in Spanish. Do not change any other elements.",
    previous_interaction_id=interaction.id,
    response_format={
        "type": "image",
        "mime_type": "image/jpeg",
        "aspect_ratio": "16:9",
        "image_size": "2K",   # 0.5K, 1K, 2K, 4K (Nano Banana 2+)
    },
)
```

### 2.7. Grounding với Google Search (Image Search)

```bash
curl -s -X POST "https://generativelanguage.googleapis.com/v1beta/interactions" \
  -H "x-goog-api-key: $GEMINI_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemini-3.1-flash-image",
    "input": [
      {"type": "text", "text": "Use image search to find accurate images of a resplendent quetzal bird. Create a beautiful 3:2 wallpaper."}
    ],
    "tools": [{"type": "google_search"}],
    "search_tools": ["image_search"]
  }'
```

### 2.8. Response Format Options

```json
{
  "response_format": {
    "type": "image",
    "mime_type": "image/jpeg",     // image/png, image/jpeg, image/webp
    "aspect_ratio": "16:9",         // 1:1, 3:2, 2:3, 3:4, 4:3, 9:16, 16:9
    "image_size": "2K"              // 0.5K (chỉ Flash), 1K, 2K, 4K
  }
}
```

---

## 3. Con Đường #2 — useapi.net Wrapper (Mirror Flow)

**useapi.net** cung cấp REST API mirror endpoint của Google Flow, dùng được với **bất kỳ Google AI subscription nào** (kể cả free account).

### 3.1. Thông Tin Chung

- **Base URL**: `https://api.useapi.net/v1/google-flow`
- **Auth**: `Authorization: Bearer YOUR_API_TOKEN`
- **Setup**: Tạo token tại https://useapi.net/docs/start-here/setup-useapi
- **Setup account Google Flow**: https://useapi.net/docs/start-here/setup-google-flow
- **Image generation là synchronous** — một POST duy nhất trả về ảnh (10-20s), không cần polling.

### 3.2. POST /v1/google-flow/images — Generate Images

**Endpoint**: `POST https://api.useapi.net/v1/google-flow/images`

**Request Headers:**
```http
Authorization: Bearer YOUR_API_TOKEN
Content-Type: application/json
```

**Request Body:**
```json
{
  "prompt": "A serene mountain landscape at sunset with vibrant colors",
  "model": "nano-banana-2-lite",
  "aspectRatio": "16:9",
  "count": 4,
  "seed": 123456
}
```

**Parameters:**

| Tham số | Bắt buộc | Mô tả |
|---------|----------|-------|
| `prompt` | ✅ | Text mô tả ảnh |
| `model` | ❌ | `nano-banana-2-lite` (default), `nano-banana-2`, `nano-banana-pro` |
| `aspectRatio` | ❌ | `16:9`, `4:3`, `1:1`, `3:4`, `9:16`, `auto` (chỉ khi có reference) |
| `count` | ❌ | 1–4, default 4 |
| `seed` | ❌ | Integer ≥ 0 |
| `reference_1` … `reference_10` | ❌ | `mediaGenerationId` từ upload assets |
| `character_1` … `character_7` | ❌ | Character ref-id |
| `replyUrl` | ❌ | Webhook URL (timeout 5s) |
| `replyRef` | ❌ | Custom ref string cho callback |
| `email` | ❌ | Email tài khoản Flow (mặc định auto/load-balance) |

### 3.3. Response (200 OK)

```json
{
  "jobId": "j1731859345678i-u12345-email:jo***@gmail.com-bot:google-flow",
  "media": [
    {
      "name": "…redacted…",
      "workflowId": "…redacted…",
      "image": {
        "generatedImage": {
          "seed": 123456,
          "mediaGenerationId": "user:12345…redacted…",
          "mediaVisibility": "PRIVATE",
          "prompt": "A serene mountain landscape at sunset with vibrant colors",
          "modelNameType": "HARBOR_SEAL",
          "workflowId": "…redacted…",
          "fifeUrl": "https://flow-content.google/image/…?Expires=…&KeyName=labs-flow-prod-cdn-key&Signature=…",
          "aspectRatio": "IMAGE_ASPECT_RATIO_LANDSCAPE",
          "requestData": {
            "promptInputs": [{"textInput": "A serene mountain landscape at sunset with vibrant colors"}],
            "imageGenerationRequestData": {"imageGenerationImageInputs": []}
          }
        }
      }
    }
  ]
}
```

**Các field quan trọng:**
- `media[].image.generatedImage.fifeUrl` — URL signed để tải ảnh (hết hạn ~6h)
- `media[].image.generatedImage.encodedImage` — Base64 fallback khi `fifeUrl` vắng mặt (hai field này **không bao giờ cùng tồn tại**)
- `media[].image.generatedImage.seed` — Seed để reproduce
- `media[].image.generatedImage.mediaGenerationId` — ID dùng làm reference cho request sau

### 3.4. Inline @-mention Markers

Dùng `@character_1` hoặc `@reference_1` trong prompt để tham chiếu tới slot:

```json
{
  "model": "nano-banana-pro",
  "prompt": "@character_1 standing next to @reference_1, golden hour light",
  "character_1": "user:123-email:...-character:...-imgs:1",
  "reference_1": "user:123-email:...-image:..."
}
```

### 3.5. Error Codes

| Status | Ý nghĩa | Xử lý |
|--------|----------|-------|
| 400 | Validation error / content policy | Sửa prompt |
| 401 | Invalid API token | Check token |
| 402 | Subscription expired | Nạp credit |
| 403 | reCAPTCHA bị reject | Tăng `captchaRetry`, thêm provider |
| 404 | Account not found | Cấu hình lại account |
| 429 | Quota / rate limit | Đợi `Retry-After` (xem bảng dưới) |
| 500 | Google content moderation | Đổi model hoặc sửa prompt |
| 503 | Service unavailable hoặc captcha provider lỗi | Retry sau 5-10s |
| 596 | Session refresh failed | Re-setup account |

**Chi tiết 429 reasons:**
| Reason | Scope | Cooldown |
|--------|-------|----------|
| `PUBLIC_ERROR_USER_REQUESTS_THROTTLED` | Tất cả model của account | ~30 min |
| `PUBLIC_ERROR_USER_QUOTA_REACHED` | Tất cả model của account | ~30 min |
| `PUBLIC_ERROR_PER_MODEL_DAILY_QUOTA_REACHED` | 1 model trên 1 account | Tới UTC midnight |
| `PUBLIC_ERROR_UNUSUAL_ACTIVITY_TOO_MUCH_TRAFFIC` | Per request | 60s |

### 3.6. Code Mẫu

**Python (sync):**
```python
import base64
import requests

token = "YOUR_API_TOKEN"
url = "https://api.useapi.net/v1/google-flow/images"
headers = {
    "Authorization": f"Bearer {token}",
    "Content-Type": "application/json",
}
data = {
    "prompt": "A serene mountain landscape at sunset",
    "model": "nano-banana-2-lite",
    "aspectRatio": "16:9",
    "count": 4,
    "seed": 123456,
}

response = requests.post(url, headers=headers, json=data)
result = response.json()

for i, item in enumerate(result["media"]):
    img = item["image"]["generatedImage"]
    if not img.get("fifeUrl"):
        with open(f"image_{i+1}.jpg", "wb") as f:
            f.write(base64.b64decode(img["encodedImage"]))
        continue
    image = requests.get(img["fifeUrl"])
    with open(f"image_{i+1}.jpg", "wb") as f:
        f.write(image.content)
```

**JavaScript (fetch):**
```javascript
const token = "YOUR_API_TOKEN";
const res = await fetch("https://api.useapi.net/v1/google-flow/images", {
  method: "POST",
  headers: {
    "Authorization": `Bearer ${token}`,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({
    prompt: "A serene mountain landscape at sunset",
    model: "nano-banana-2-lite",
    aspectRatio: "16:9",
    count: 4,
    seed: 123456,
  }),
});
const result = await res.json();

for (const [i, item] of result.media.entries()) {
  const img = item.image.generatedImage;
  const blob = img.fifeUrl
    ? await (await fetch(img.fifeUrl)).blob()
    : new Blob([Uint8Array.from(atob(img.encodedImage), c => c.charCodeAt(0))], { type: "image/jpeg" });
  const buffer = Buffer.from(await blob.arrayBuffer());
  require("fs").writeFileSync(`image_${i+1}.jpg`, buffer);
}
```

**cURL:**
```bash
curl -X POST \
  -H "Authorization: Bearer YOUR_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "prompt": "A serene mountain landscape at sunset",
    "model": "nano-banana-2-lite",
    "aspectRatio": "16:9",
    "count": 4,
    "seed": 123456
  }' \
  "https://api.useapi.net/v1/google-flow/images" > response.json

# Download ảnh
jq -r '.media[0].image.generatedImage.fifeUrl' response.json | xargs curl -o image_1.jpg
```

### 3.7. Upload Reference Image (POST /assets/email)

```bash
curl -X POST "https://api.useapi.net/v1/google-flow/assets/email" \
  -H "Authorization: Bearer YOUR_API_TOKEN" \
  -F "email=YOUR_GOOGLE_EMAIL" \
  -F "file=@/path/to/image.png"
```

Response trả `mediaGenerationId.mediaGenerationId` — dùng làm `reference_1` etc.

### 3.8. So Sánh Models

| Parameter | Nano Banana 2 Lite | Nano Banana 2 | Nano Banana Pro |
|-----------|---------------------|---------------|-----------------|
| T2I (text-to-image) | ✓ | ✓ | ✓ |
| I2I (reference) | ✓ (max 10) | ✓ (max 10) | ✓ (max 10) |
| Aspect ratios | 16:9, 4:3, 1:1, 3:4, 9:16, auto | — | — |
| Default ratio (T2I) | 16:9 | 16:9 | 16:9 |
| Default ratio (I2I) | 16:9 | auto | auto |
| count | 1–4 | 1–4 | 1–4 |
| seed | ✓ | ✓ | ✓ |
| Subscription | all | all | all |

**Deprecated aliases** (vẫn còn hoạt động): `nano-banana` → `nano-banana-2`, `imagen-4` → `nano-banana-2-lite`.

> ⚠️ Google đã remove Imagen khỏi Flow từ tháng 7/2026.

---

## 4. Con Đường #3 — Reverse-Engineer labs.google (flow-proxy pattern)

Đây là cách AssetAutomator's local proxy đang dùng. Không có docs chính thức — bạn tự khám phá qua DevTools.

### 4.1. Các Endpoint Nội Bộ Của Google Flow

| Endpoint | Mục đích |
|----------|----------|
| `https://labs.google/fx/api/auth/session` | Lấy access_token từ session cookie |
| `https://labs.google/fx/api/trpc/backbone.uploadImage` | Upload reference image |
| `https://aisandbox-pa.googleapis.com/v1:runImageFx` | Tạo ảnh (ImageFX) |
| `https://aisandbox-pa.googleapis.com/v1/whisk:generateImage` | Tạo ảnh (Whisk-style) |
| `https://aisandbox-pa.googleapis.com/v1/whisk:runImageRecipe` | Image recipe (multi-ref) |

### 4.2. Cách Lấy Access Token

```javascript
// Mở https://labs.google/fx/tools/flow trong Chrome
// Mở DevTools → Console → Paste:
let script = document.querySelector("#__NEXT_DATA__");
let obj = JSON.parse(script.textContent);
let authToken = obj["props"]["pageProps"]["session"]["access_token"];
console.log(authToken);

// Hoặc gọi API trực tiếp với session cookie:
const res = await fetch("https://labs.google/fx/api/auth/session", {
  credentials: "include",
});
const data = await res.json();
const accessToken = data.access_token;
```

### 4.3. Gọi Endpoint Trực Tiếp

```python
import requests

# Headers bắt buộc
headers = {
    "Authorization": f"Bearer {access_token}",
    "Origin": "https://labs.google",
    "Referer": "https://labs.google/fx/tools/flow",
    "Content-Type": "application/json",
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
}

# Text-to-Image
payload = {
    "prompt": "A serene mountain landscape at sunset",
    "aspectRatio": "IMAGE_ASPECT_RATIO_LANDSCAPE",
    "count": 4,
    "modelNameType": "GEM_PIX_2",  # HARBOR_SEAL = nano-banana-2, NARWHAL = pro
}

response = requests.post(
    "https://aisandbox-pa.googleapis.com/v1:runImageFx",
    headers=headers,
    json=payload,
    timeout=120,
)
```

### 4.4. Common Models Trong Flow

| `modelNameType` | Model thực |
|-----------------|-----------|
| `HARBOR_SEAL` | Nano Banana 2 (Gemini 3.1 Flash Image) |
| `NARWHAL` | Nano Banana Pro (Gemini 3 Pro Image) |
| `GEM_PIX_2` | Nano Banana 2 Lite (Gemini 3.1 Flash-Lite Image) |
| `IMAGEN_3_5` | Imagen 3.5 (legacy, đã remove 2026) |
| `R2I` | Recipe-to-Image (legacy) |
| `GEM_PIX` | Gemini Pix (legacy) |

### 4.5. Aspect Ratios Trong Google Flow

| Value | Tỉ lệ |
|-------|-------|
| `IMAGE_ASPECT_RATIO_LANDSCAPE` | 16:9 |
| `IMAGE_ASPECT_RATIO_LANDSCAPE_FOUR_THREE` | 4:3 |
| `IMAGE_ASPECT_RATIO_SQUARE` | 1:1 |
| `IMAGE_ASPECT_RATIO_PORTRAIT_THREE_FOUR` | 3:4 |
| `IMAGE_ASPECT_RATIO_PORTRAIT` | 9:16 |

### 4.6. Models AssetAutomator Đang Expose

| Model ID | Tỉ lệ |
|----------|-------|
| `gemini-3.1-flash-image-landscape` | 16:9 |
| `gemini-3.1-flash-image-portrait` | 9:16 |
| `gemini-3.1-flash-image-square` | 1:1 |
| `gemini-3.0-pro-image-landscape` | 16:9 |
| `imagen-4.0-generate-preview-landscape` | 16:9 |
| `nano-banana-2-landscape` | 16:9 |

---

## 5. So Sánh 3 Con Đường

| Tiêu chí | #1 Gemini API | #2 useapi.net | #3 Reverse-engineer |
|----------|---------------|---------------|---------------------|
| Chính thức | ✅ | ❌ (3rd party) | ❌ |
| Chi phí | $0.039–$0.134/ảnh | Subscription hàng tháng | Free (tài khoản Flow) |
| Ổn định | Cao | Cao | Thấp (Google đổi là vỡ) |
| Setup | API key Google Cloud | Token + Google account | Session cookie + Chrome |
| Models | Nano Banana 2 Lite/2/Pro | Nano Banana 2 Lite/2/Pro | Nano Banana 2 Lite/2/Pro |
| Reference images | Có (multi-modal input) | Có (reference_1..10) | Có |
| 4K | Có | Có (NB2/NB Pro) | Phụ thuộc |
| Multi-turn | Có (`previous_interaction_id`) | Không (mỗi request độc lập) | Không |
| Tạo project trên web | ❌ | ❌ | ✅ (AssetAutomator) |

---

## 6. Khuyến Nghị Tích Hợp Từ App Khác

### 6.1. Nếu bạn là dev muốn nhanh & ổn định
→ Dùng **Con đường #1 (Gemini Interactions API)** — official SDK, free tier có, tài liệu đầy đủ.

```bash
pip install google-genai
```

### 6.2. Nếu bạn đã có sẵn tài khoản Google AI subscription
→ Dùng **Con đường #2 (useapi.net)** — tận dụng quota Flow thay vì trả per-image của Gemini API.

### 6.3. Nếu bạn muốn miễn phí & tích hợp với AssetAutomator local
→ Dùng **Con đường #3** — gọi tới `http://127.0.0.1:8787/v1/images/generations` (xem `FLOW-API.md` hiện có).

### 6.4. Nếu muốn tự build proxy như AssetAutomator
Tham khảo các open-source projects:
- **flow-proxy** (AndyShaman/whisk-proxy) — Chrome extension + Node.js CLI
- **whisk_client.py** (vndangkhoa/apix) — Python wrapper
- **comfyui-imagefx-integration** — ComfyUI node

Cả 3 đều dùng pattern:
1. Lấy access_token từ `labs.google/fx/api/auth/session` bằng session cookie
2. Auto-refresh token khi hết hạn (~30 ngày)
3. Gọi `aisandbox-pa.googleapis.com/v1:runImageFx` với Bearer token

---

## 7. Lưu Ý Quan Trọng

1. **SynthID watermark**: Tất cả ảnh do Google Flow / Nano Banana sinh ra đều có watermark ẩn SynthID. Khi tích hợp vào app production, cần disclose điều này theo [Google Prohibited Use Policy](https://ai.google.dev/terms).

2. **Content policy**: Google moderation có thể reject prompt "ranh giới" mà không báo trước. Nên có:
   - Retry logic (moderation decisions vary giữa các attempt)
   - Fallback giữa các model (NB2 Lite → NB2 → NB Pro) vì mỗi model có moderation khác nhau
   - User-facing message rõ ràng khi bị reject

3. **Rate limit / Quota**:
   - **Gemini API**: Set quota tại Google Cloud Console
   - **useapi.net**: Quota theo subscription
   - **Reverse-engineer**: ~30 ngày session, refresh trước khi hết

4. **Bản quyền & Legal**: Khi tạo ảnh từ reference có bản quyền, bạn chịu trách nhiệm. Google Flow không đảm bảo về IP.

5. **Không nên spam**: Gửi quá nhiều request song song dễ trigger reCAPTCHA hoặc 429. Khuyến nghị:
   - **Gemini API**: Theo tier quota
   - **useapi.net**: 3–20 parallel (dynamic concurrency)
   - **Reverse-engineer**: 3–5 parallel qua Singleton Browser

---

## 8. Tài Liệu Tham Khảo

- useapi.net Google Flow API docs: https://useapi.net/docs/api-google-flow-v1/post-google-flow-images
- useapi.net Articles (bash tutorial): https://useapi.net/docs/articles/google-flow-images-bash
- Google AI Interactions API: https://ai.google.dev/gemini-api/docs/interactions/image-generation
- Google Image Generation (generateContent): https://ai.google.dev/gemini-api/docs/image-generation
- Google Nano Banana 2 Lite: https://deepmind.google/models/gemini-image/flash-lite/
- Google Nano Banana 2: https://deepmind.google/models/gemini-image/flash/
- Google Nano Banana Pro: https://deepmind.google/models/gemini-image/pro/
- Vertex AI Imagen: https://cloud.google.com/vertex-ai/generative-ai/docs/image/generate-images
- flow-proxy (whisk-proxy): https://github.com/AndyShaman/whisk-proxy
- ComfyUI ImageFX integration: https://github.com/cleanlii/comfyui-imagefx-integration
- AssetAutomator FLOW-API (local proxy): `d:\Dev\AssetAutomator\FLOW-API.md`

---

*Tài liệu cập nhật đến 16/08/2026. Các field/endpoint có thể thay đổi khi Google cập nhật Flow.*
