# Flow Image Local API v1.0.0

Initial public release of the cleaned local deployment edition.

## Highlights

- Local OpenAI-compatible API for Google Flow image generation
- One-click install with `install.bat`
- One-click startup with `start-flow-api.bat`
- Guided `/setup` page with human-readable API info cards
- User only needs to log in to Google Flow
- No browser extension required
- Compatible with third-party tools that only need:
  - `URL`
  - `API Key`
  - `Model`

## Included In This Release

- `GET /v1/models`
- `POST /v1/images/generations`
- `POST /v1/images/edits`
- `POST /v1/chat/completions`
- Guided setup endpoints under `/setup`
- Playwright-based local browser login / captcha flow
- Model alias support:
  - `gemini-3.1-flash-image`
  - `gemini-3.0-pro-image`
  - `imagen-4.0-generate-preview`
  - `nano banana2`
  - `nano banana pro`
- Size mapping:
  - `1K`
  - `2K`
  - `4K`
- Aspect ratio mapping:
  - `1:1`
  - `9:16`
  - `16:9`
  - `21:9`

## Notes

- `21:9` is only supported by `nano banana2`
- 4K generation still depends on upstream account permissions
- The local default API key is `flow-local-key`
- Default API base URL is `http://127.0.0.1:8787/v1`

## Distribution

This version includes a release packaging workflow:

- `release-package/build-release-package.bat`

It generates:

- `release-package/dist/flow-image-cli-local-api-v1.0.0/`
- `release-package/dist/flow-image-cli-local-api-v1.0.0.zip`

## Recommended GitHub Release Title

`v1.0.0 - Initial public local API release`

## Recommended GitHub Release Description

```md
Flow Image Local API v1.0.0 is the first cleaned public release of this local deployment edition.

It packages Google Flow image generation into an OpenAI-compatible local API for Windows, with a guided setup page and one-click install/start scripts.

Highlights:

- No browser extension required
- User only logs in to Google Flow
- Local service handles the rest
- Easy third-party integration with only URL, API Key, and Model
- Supports text-to-image and image-to-image
- Supports 1K / 2K / 4K and 1:1 / 9:16 / 16:9 / 21:9

Important notes:

- 21:9 is only supported by nano banana2
- 4K availability depends on the upstream Flow account permissions
- Default API URL: http://127.0.0.1:8787/v1
- Default API Key: flow-local-key
```
