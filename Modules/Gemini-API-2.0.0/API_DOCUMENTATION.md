# Tài Liệu Tích Hợp Gemini WebAPI REST Service

Tài liệu hướng dẫn tích hợp các đầu API (RESTful Endpoints) cho ứng dụng Google Gemini WebAPI, hỗ trợ quản lý danh sách **Gemini Gems**, trò chuyện với Gem, tự động hóa **Deep Research** và sử dụng **Gemini Extensions**.

---

## 🚀 1. Khởi Chạy API Server

### Yêu cầu môi trường
- Python >= 3.10
- Đã tạo sẵn file `cookies.json` tại thư mục dự án chứa Cookie Google.

### Lệnh khởi chạy Server
```bash
python server.py
```
Hoặc khởi chạy bằng `uvicorn`:
```bash
uvicorn server:app --host 0.0.0.0 --port 8000 --reload
```

> 💡 Sau khi khởi chạy, bạn có thể truy cập giao diện **Swagger UI Interactive Documentation** tại: `http://localhost:8000/docs`

---

## 📡 2. Danh Sách Các Đầu API (Endpoints)

### 1. Kiểm tra trạng thái Server (Health Check)
- **URL**: `GET /api/health`
- **Mô tả**: Kiểm tra API Server và trạng thái kết nối Gemini Client.

#### Request mẫu:
```http
GET /api/health HTTP/1.1
Host: localhost:8000
```

#### Response mẫu (200 OK):
```json
{
  "status": "ok",
  "initialized": true
}
```

---

### 2. Danh sách các Gems (List Gems)
- **URL**: `GET /api/gems`
- **Query Parameter**:
  - `include_hidden` *(boolean, optional)*: `true` để lấy cả các System Gems ẩn. Mặc định `false`.
- **Mô tả**: Lấy danh sách tất cả các Gem khả dụng trong tài khoản (gồm cả System Gems và Custom Gems do bạn tạo).

#### Response mẫu (200 OK):
```json
[
  {
    "id": "coding-partner",
    "name": "Đối tác lập trình",
    "description": "Nâng cao kỹ năng lập trình của bạn...",
    "prompt": "Mục đích của bạn là giúp tôi...",
    "predefined": true
  },
  {
    "id": "f5c0e6766ee4",
    "name": "Scriptor - Rewrite",
    "description": "Viết lại kịch bản từ script có sẵn",
    "prompt": "Đây là bản full prompt...",
    "predefined": false
  }
]
```

---

### 3. Trò chuyện với Gem / Deep Research / Extensions
- **URL**: `POST /api/chat`
- **Content-Type**: `application/json`

#### Request Body Schema:
| Trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| :--- | :--- | :--- | :--- |
| `message` | `string` | **Có** | Nội dung tin nhắn / Prompt gửi cho Gemini. |
| `gem_id` | `string` | Không | ID hoặc Tên của Gem muốn áp dụng (Ví dụ: `"coding-partner"` hoặc `"f5c0e6766ee4"`). |
| `session_id` | `string` | Không | ID phiên chat để giữ ngữ cảnh hội thoại liên tục. Nếu để trống, server sẽ tự sinh ID mới. |
| `deep_research` | `boolean` | Không | Set `true` để tự động triển khai quy trình Deep Research ngầm. Mặc định `false`. |
| `temporary` | `boolean` | Không | Set `true` nếu không muốn lưu cuộc trò chuyện vào lịch sử Gemini. Mặc định `false`. |

#### Trường hợp 1: Trò chuyện thông thường với Gem
```json
{
  "message": "Hãy giải thích khái niệm Async/Await trong Python",
  "gem_id": "coding-partner"
}
```

#### Trường hợp 2: Trò chuyện kết hợp Gemini Extensions
*Sử dụng cú pháp `@ExtensionName` ngay trong `message`:*
```json
{
  "message": "@YouTube Tìm các video mới nhất hướng dẫn lập trình FastAPI 2026",
  "gem_id": "coding-partner"
}
```

#### Trường hợp 3: Tự động triển khai Deep Research
```json
{
  "message": "Nghiên cứu và so sánh chi tiết các mô hình ngôn ngữ lớn năm 2026",
  "gem_id": "coding-partner",
  "deep_research": true
}
```

#### Response mẫu (200 OK):
```json
{
  "session_id": "c7a23405-f3ef-4b36-9b16-e4d06a9db3f1",
  "text": "Dưới đây là báo cáo phân tích chi tiết...",
  "thoughts": "Phân tích yêu cầu lập trình...",
  "images": [],
  "deep_research_completed": false
}
```

---

### 4. Phản hồi trực tiếp theo thời gian thực (SSE Stream Chat)
- **URL**: `POST /api/chat/stream`
- **Content-Type**: `application/json`
- **Accept**: `text/event-stream`

Phản hồi dạng Server-Sent Events (SSE) từng ký tự / delta text nhận được từ Gemini.

---

## 💻 3. Ví Dụ Tích Hợp Dành Cho Lập Trình Viên

### 1. cURL (Terminal / Command Line)

#### Lấy danh sách Gems:
```bash
curl -X GET "http://localhost:8000/api/gems"
```

#### Chat với Gem:
```bash
curl -X POST "http://localhost:8000/api/chat" \
     -H "Content-Type: application/json" \
     -d '{
           "message": "Chào bạn, hãy giúp tôi viết hàm sắp xếp mảng trong Python",
           "gem_id": "coding-partner"
         }'
```

---

### 2. Python (`httpx` / `requests`)

```python
import requests

API_URL = "http://localhost:8000/api"

# 1. Lấy danh sách Gems
gems_response = requests.get(f"{API_URL}/gems")
gems = gems_response.json()
print(f"Tổng số Gems: {len(gems)}")

# 2. Chat với Gem
payload = {
    "message": "@YouTube Tìm kiếm các bài nhạc học tập nhẹ nhàng",
    "gem_id": "coding-partner"
}

response = requests.post(f"{API_URL}/chat", json=payload)
data = response.json()

print(f"Session ID: {data['session_id']}")
print(f"Phản hồi từ Gemini:\n{data['text']}")
```

---

### 3. JavaScript / TypeScript (Fetch / Node.js)

```javascript
const API_URL = "http://localhost:8000/api";

// 1. Lấy danh sách Gems
async function getGems() {
  const res = await fetch(`${API_URL}/gems`);
  const gems = await res.json();
  console.log("Danh sách Gems:", gems);
}

// 2. Gửi tin nhắn đến Gem
async function chatWithGem(message, gemId, sessionId = null) {
  const res = await fetch(`${API_URL}/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      message: message,
      gem_id: gemId,
      session_id: sessionId,
      deep_research: false
    })
  });
  
  const data = await res.json();
  console.log("Trả lời:", data.text);
  return data.session_id; // Lưu lại để tiếp tục trò chuyện
}

// Gọi thử nghiệm
getGems();
chatWithGem("Hãy giải thích nguyên lý Clean Architecture", "coding-partner");
```
