# Flow Image Local API & Programmatic SDK (Unified)

[![Python Version](https://img.shields.io/badge/python-3.10%20%7C%203.11%20%7C%203.12-blue)](https://www.python.org/)
[![License](https://img.shields.io/badge/license-MIT-green)](./LICENSE)

Arabic Version: [README-ar.md](./README-ar.md) | Architecture Details: [ARCHITECTURE-ar.md](./ARCHITECTURE-ar.md)

This repository provides a unified local deployment of Google Flow image generation, combining several core tools into one unified library. It can be used as a programmatic Python SDK or deployed as a local OpenAI-compatible HTTP API server.

---

## 🗺️ Architectural Structure

The project is divided into **4 core modules** that handle different aspects of the service:

### 📦 Part 1: Programmatic Python SDK (`FlowSDK`)
The core Python SDK allows you to embed Google Flow image generation directly into other applications without network bridges or separate servers. It features:
* **Thread-Safe Context Isolation**: `async with FlowSDK(...)` isolates configuration changes.
* **Direct Token & Profile Selectors**: Support for direct token inputs or automatic profile-based configuration querying from the database.
* **Usage Example**:
```python
import asyncio
from google_flow import FlowSDK

async def main():
    # Option 1: Direct Session Token Override
    async with FlowSDK(st_token="your-session-token", project_id="your-project-id") as sdk:
        image_path = await sdk.generate(
            prompt="A futuristic city in antigravity, neon lights, 4k",
            model="gemini-3.1-flash-image-landscape",
            output_path="output/direct_t2i.png"
        )
        print(f"Image saved to: {image_path}")

    # Option 2: Automatic Profile Selector (Querying local SQLite database)
    async with FlowSDK() as sdk:
        await sdk.select_profile("My_Google_Profile_Name")
        image_path = await sdk.generate(
            prompt="A majestic golden eagle flying over mountains",
            model="gemini-3.1-flash-image-square",
            output_path="output/profile_t2i.png"
        )
        print(f"Image saved to: {image_path}")

if __name__ == "__main__":
    asyncio.run(main())
```

---

### 🌐 Part 2: OpenAI-Compatible HTTP API
Exposes the image generation engine as a standard OpenAI-compatible REST API.
* **Compatibility**: Directly pluggable into third-party AI interfaces like Cherry Studio or Next Chat.
* **Interactive Dashboard (`/setup`)**: An automated web wizard that opens upon startup, helping you log in, detect session tokens, and manage settings.
* **Quick Start**:
  1. Double-click `install.bat` to setup the environment.
  2. Double-click `start-flow-api.bat` to launch the API server.
  3. Complete the interactive setup on `http://127.0.0.1:8787/setup`.
* **Endpoints**:
  * `POST /v1/images/generations` - Text-to-Image
  * `POST /v1/images/edits` - Image-to-Image
  * `GET /v1/models` - List models
  * `GET /setup` - Interactive setup wizard

---

### 🔄 Part 3: Automated Token Updater Daemon
A background updater service that ensures session tokens remain fresh and valid.
* **Keep-Alive Scheduling**: Uses `APScheduler` to run periodic background validation of account tokens.
* **Login Methods**: Supports silent protocol-based updates and interactive browser updates (opening a temporary browser window if Google requires human validation).
* **Proxy Isolation**: Supports configuring separate proxy servers per account profile to prevent footprint leaks.

---

### 🔑 Part 4: In-Process Captcha Solving Service
Resolves Google Flow's reCAPTCHA challenges dynamically within the same Python process.
* **No External Bridge Needed**: Integrates directly with the browser runtime via Playwright/nodriver.
* **Resident Browser Tabs**: Re-uses open browser tabs to drastically reduce cold start times and resource consumption by up to 60%.

---

## 📁 Repository Directory Map

```text
flow-image-cli-local-api/
├── google_flow/              # Unified core package
│   ├── api/                  # Part 2: FastAPI OpenAI web server & dashboard
│   ├── captcha/              # Part 4: In-process captcha provider integrations
│   ├── captcha_service/      # Part 4: Integrated reCAPTCHA crawler (nodriver/playwright)
│   ├── token_updater/        # Part 3: Automated session renewal daemon
│   ├── core/                 # Part 1: Programmatic SDK (FlowSDK)
│   └── utils/                # Shared parsing, DB, and proxy utilities
├── examples/                 # Programmatic usage examples
├── release-package/          # Scripts to build clean distribution zip packages
├── install.bat               # Installer for Windows
├── start-flow-api.bat        # Launcher for Windows
├── API_USAGE.md              # API endpoints & cURL request examples
└── README.md
```

---

## 📚 References & Heritage

This unified project incorporates and builds upon the following original repositories:
* **Captcha Service Module**: [flow_captcha_service](https://github.com/genz27/flow_captcha_service)
* **Token Updater Module**: [flow2api_tupdater](https://github.com/genz27/flow2api_tupdater)
* **Original CLI & Local API Codebase**: [flow-image-cli-local-api](https://github.com/cdm16888/flow-image-cli-local-api)

---

## 📝 License

This project is licensed under the MIT License.
