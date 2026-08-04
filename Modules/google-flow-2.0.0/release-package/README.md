# Release Package

This directory is used to build a cleaner distribution package for end users.

## Goal

Generate a folder and zip package that can be sent directly to another user, without including local development clutter such as:

- `.git`
- `.venv`
- local outputs
- editor files

## Build

From the project root, run:

```bat
release-package\build-release-package.bat
```

The script creates:

- `release-package\dist\flow-image-cli-local-api-v1.0.0\`
- `release-package\dist\flow-image-cli-local-api-v1.0.0.zip`

## What Gets Included

- `google_flow/`
- `install.bat`
- `start-flow-api.bat`
- `API_USAGE.md`
- `README.md`
- `README-ar.md`
- `requirements.txt`
- `pyproject.toml`
- `config.toml`
- `interactive_generate.py`
- `LICENSE`

## Recommended Delivery

Send the generated zip file to the end user.

After unzip, the user only needs to:

1. Double-click `install.bat`
2. Double-click `start-flow-api.bat`
3. Log in to Google Flow
4. Copy the API information shown on `/setup`
