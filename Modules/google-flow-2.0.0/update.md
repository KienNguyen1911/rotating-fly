# Comprehensive Upgrade & Extension Layer Architecture Report (`google_flow_ext`)

This document summarizes all enhancements, architectural differences, and components between the **Original Base Repository (`3mora2/google-flow`)** and the **Extended Package (`google_flow_ext`)**.

---

## 1. 🏛️ Extension Layer Architecture Pattern

The project strictly follows the **Extension Layer (Outer Wrapper)** design pattern:
- **Root Directory `google_flow/`**: **100% Clean Untouched Base**. No files in the original codebase are modified, ensuring seamless `git pull` or `git checkout` updates from `3mora2` at any time without git conflicts.
- **Extension Package `google_flow_ext/`**: Encapsulates all custom business logic, feature extensions, and the new web interface.

---

## 2. 🚀 Feature Enhancements in `google_flow_ext`

### A. Access Token Auto-Refresh & Retry Engine
- **`ExtendedImageGenerator`** (Subclasses `google_flow.core.generator.ImageGenerator`):
  - Intercepts `FlowTokenExpiredError` during image generation or upscaling retries.
  - Automatically triggers `refresh_access_token()` inside the `on_retry` callback and resumes image generation seamlessly without dropping user requests.

### B. Multi-Project Support & Reference Image Caching
- **`ExtendedFlowClient`** (Subclasses `google_flow.core.client.FlowClient`):
  - Adds `create_project()` to create new projects on the Google Flow website via tRPC API.
  - Project-isolated `media_id` caching using SHA-256 byte hashes.
  - Automatic media cache invalidation (`invalidate_media_cache`) and force re-upload (`force_reupload=True`) fallback on generation errors.

### C. Extended OpenAI-Compatible API Endpoints
- **`POST /v1/projects`**: Create new projects on Google Flow website.
- **`POST /v1/images/generations`**: Supports `project_id`, `project_title`, and reference image lists (`reference_media_id`).
- **`POST /v1/images/edits`**: Image-to-Image form upload for prompt-based editing.
- **`POST /v1/chat/completions`**: Parses size preferences, aspect ratios, and reference images from message history.

### D. Robust Session Token Extraction
- **`ExtendedLoginSessionManager`** (Subclasses `google_flow.api.login_manager.LoginSessionManager`):
  - Fallback cookie extraction for NextAuth session token variants in Playwright browser contexts.

---

## 🎨 3. Skeuomorphic Web Console

All web management pages inside `google_flow_ext/static/` have been recreated using a **Skeuomorphic Design Language** (tactile 3D controls, metallic plates, glowing LED indicators, inset shadows, and CRT display screens):

| Page | URL Path | Skeuomorphic Design Concept |
| :--- | :--- | :--- |
| **System Dashboard** | `http://127.0.0.1:8787/` | Hardware Server Rack & LED Health Meters |
| **Setup Wizard** | `http://127.0.0.1:8787/setup` | 3-Step Physical Console Terminal with dynamic status LEDs |
| **Captcha Admin** | `http://127.0.0.1:8787/admin` | Cluster Hardware Control Board & CRT Logs |
| **User Console** | `http://127.0.0.1:8787/portal` | Executive Metallic Desk & API Credential Card |
| **API Docs (Swagger)** | `http://127.0.0.1:8787/docs` | Interactive API Testing |

---

## 📁 4. Project Directory Structure

```text
c:\Users\ngkie\Downloads\google-flow-2.0.0/
├── google_flow/                 # Core Base Repo (100% Clean Untouched 3mora2/google-flow)
├── google_flow_ext/             # Extension Package
│   ├── __init__.py
│   ├── client.py                # ExtendedFlowClient
│   ├── generator.py             # ExtendedImageGenerator
│   ├── sdk.py                   # ExtendedFlowSDK
│   ├── login_manager.py         # ExtendedLoginSessionManager
│   ├── schemas.py               # API Pydantic Schemas
│   ├── api/
│   │   ├── app.py               # Extended FastAPI app mounting base + ext routes
│   │   └── routes/
│   │       └── openai_ext.py    # Extended API Endpoints
│   └── static/                  # Skeuomorphic Web Interface
│       ├── skeuo.css            # Skeuomorphic Design System Stylesheet
│       ├── app.js               # Dynamic Frontend Controller
│       ├── index.html           # System Dashboard
│       ├── setup.html           # Setup Terminal Wizard
│       ├── admin.html           # Captcha Admin Rack
│       └── portal.html          # Commercial User Console
├── start-flow-ext.py            # Python Launcher (Port 8787)
├── start-flow-ext.bat           # Windows Batch Launcher
├── test_flow_ext.py             # Automated Verification Suite
├── update.md                    # Upgrade Architecture Report (English)
└── ARCHITECTURE.md             # System Architecture Document (English)
```