# Flow Image API Wrapper

This project now includes an OpenAI-compatible local API wrapper.

For the current guided deployment flow, the browser extension is not required.
Users only need to sign in to Google Flow on the setup page. Token sync is handled automatically by the local service.

## Install

For a new machine, run:

```powershell
install.bat
```

## Start

One-click startup:

```powershell
start-flow-api.bat
```

After startup, open:

```text
http://127.0.0.1:8787/setup
```

User flow:

1. Double-click `start-flow-api.bat`
2. Sign in to Google Flow in the opened browser
3. Wait for the setup page to finish automatically
4. Copy the returned `URL`, `API Key`, and `Model`

Manual startup:

```powershell
cd c:\Users\Administrator\.codex\flow-image-cli
.\.venv\Scripts\Activate.ps1
$env:FLOW_API_KEY="flow-local-key"
flow-api --host 127.0.0.1 --port 8787
```

Base URL:

```text
http://127.0.0.1:8787/v1
```

API Key:

```text
flow-local-key
```

## Supported Endpoints

- `GET /v1/models`
- `POST /v1/images/generations`
- `POST /v1/images/edits`
- `GET /v1/files/{filename}`

## Model Examples

- `gemini-3.1-flash-image-landscape`
- `gemini-3.1-flash-image-portrait`
- `gemini-3.1-flash-image-square`
- `gemini-3.0-pro-image-landscape`
- `imagen-4.0-generate-preview-landscape`
- `nano-banana-2-landscape`
- `nano-banana-2-portrait`
- `nano-banana-2-square`
- `nano-banana-2-ultrawide`
- `nano-banana-pro-landscape`
- `nano-banana-pro-portrait`
- `nano-banana-pro-square`

You can also use these family aliases with `size`:

- `gemini-3.1-flash-image`
- `gemini-3.0-pro-image`
- `imagen-4.0-generate-preview`
- `nano banana2`
- `nano banana pro`

Size mapping:

- `1K` -> original
- `2K` -> 2K upscale
- `4K` -> 4K upscale
- `1536x1024` -> landscape
- `1024x1536` -> portrait
- `1024x1024` -> square

Aspect ratio mapping:

- `16:9` -> landscape
- `21:9` -> ultrawide, only supported by `nano banana2`
- `9:16` -> portrait
- `1:1` -> square

Quality mapping:

- `standard` -> original
- `hd` or `2k` -> 2K upscale
- `4k` -> 4K upscale

## Text-to-Image Example

```bash
curl http://127.0.0.1:8787/v1/images/generations ^
  -H "Authorization: Bearer flow-local-key" ^
  -H "Content-Type: application/json" ^
  -d "{\"model\":\"gemini-3.1-flash-image\",\"prompt\":\"a cinematic cat\",\"size\":\"1536x1024\",\"quality\":\"hd\",\"response_format\":\"url\"}"
```

## Image-to-Image Example

```bash
curl http://127.0.0.1:8787/v1/images/edits ^
  -H "Authorization: Bearer flow-local-key" ^
  -F "model=gemini-3.1-flash-image" ^
  -F "prompt=convert to watercolor" ^
  -F "size=1024x1024" ^
  -F "quality=2k" ^
  -F "image=@input.jpg"
```
