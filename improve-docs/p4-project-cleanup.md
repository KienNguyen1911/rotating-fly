# P4 — Project Cleanup & Configuration

## Changes

### P4.3 — Move Scripts to tools/

Moved `Scripts/` and `PythonEmbed/` into a dedicated `tools/` folder to separate runtime tools from source code.

**Before:**
```
AssetAutomator/
├── Scripts/           ← Test scripts mixed with source
├── PythonEmbed/       ← Runtime embedded Python
├── Models/
├── Services/
└── ...
```

**After:**
```
AssetAutomator/
├── tools/
│   ├── Scripts/       ← Test/integration scripts
│   └── PythonEmbed/   ← Embedded Python runtime
├── src/               ← Source code (future)
├── Models/
├── Services/
└── ...
```

### Updated References

1. **AssetAutomator.csproj** - Updated `CopyToOutputDirectory` paths:
   - `Scripts\**` → `tools\Scripts\**`
   - `PythonEmbed\**` → `tools\PythonEmbed\**`

2. **TranscriptExtractionStep.cs** - Updated fallback script paths:
   - `Scripts/fallback_transcript.py` → `tools/Scripts/fallback_transcript.py`
   - `PythonEmbed/python.exe` → `tools/PythonEmbed/python.exe`

3. **PythonServerManager.cs** - Updated Python executable resolution:
   - Added `tools/` prefix to embedded Python paths
   - Simplified path lookup (removed legacy relative paths)

### Files Modified
- `AssetAutomator.csproj`
- `Services/Steps/TranscriptExtractionStep.cs`
- `Helpers/PythonServerManager.cs`

## Validation
- `dotnet build` succeeds with 0 errors
- Python server manager correctly resolves embedded Python at `tools/PythonEmbed/python.exe`
- Transcript fallback script correctly resolves at `tools/Scripts/fallback_transcript.py`

## Future Work (P4.1 - Deferred)
Split into 4 csproj (Core, Infrastructure, Application, UI) - requires significant refactoring of cross-dependencies between Converters, Helpers, and WPF types.

## Future Work (P4.2 - Pending)
Move magic numbers (timeouts, retries, sleeps) into `AppConstants` / config for centralized configuration management.
