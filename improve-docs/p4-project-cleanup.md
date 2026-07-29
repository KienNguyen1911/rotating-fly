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

## P4.1 - Split into 4 csproj Projects (In Progress)

### New Project Structure

```
AssetAutomator/
├── src/
│   ├── AssetAutomator.Core/           (Class Library - no WPF)
│   │   ├── Constants/                  (AppConstants, TimingConstants)
│   │   ├── Interfaces/                 (IConfigService, IBrowserService, ILogService, IImageGenProvider)
│   │   └── Models/                     (AppSettings, AutomationTask, etc.)
│   ├── AssetAutomator.Infrastructure/  (Class Library)
│   │   ├── Helpers/                    (DeviceHelper, HumanBehaviourHelper, ProxyHelper, etc.)
│   │   ├── Logging/                    (LogService)
│   │   └── Services/                   (ConfigService)
│   ├── AssetAutomator.Application/    (Class Library - WPF)
│   │   ├── Services/                   (HistoryService, LicenseService, ChatGptService, etc.)
│   │   ├── Steps/                      (All pipeline steps)
│   │   └── Providers/                  (Image generation providers)
│   └── AssetAutomator.UI/              (WPF Application)
│       ├── Windows/                    (All WPF windows)
│       ├── Converters/                 (WPF converters)
│       └── (App.xaml, MainWindow, ViewModels)
├── tools/                              (Runtime tools - Scripts, PythonEmbed)
├── Models/                             (Legacy - to be migrated)
├── Services/                            (Legacy - to be migrated)
├── Helpers/                             (Legacy - to be migrated)
└── ...
```

### Completed in P4.1
- ✅ Created project structure under `src/`
- ✅ Created `AssetAutomator.Core.csproj` (Class Library, no WPF)
- ✅ Created `AssetAutomator.Infrastructure.csproj` (depends on Core)
- ✅ Created `AssetAutomator.Application.csproj` (depends on Core + Infrastructure)
- ✅ Created `AssetAutomator.UI.csproj` (WPF App, depends on all)
- ✅ Moved Interfaces to Core (IConfigService, IBrowserService, ILogService, IImageGenProvider)
- ✅ Moved Constants and Models to Core (AppSettings, AutomationTask, etc.)
- ✅ Moved Helpers to Infrastructure (DeviceHelper, HumanBehaviourHelper, ProxyHelper, etc.)
- ✅ Cleaned ConfigService - removed static Instance property
- ✅ Cleaned PythonServerManager - removed static Default property
- ✅ Moved Services to Application (HistoryService, LicenseService, ChatGptService)
- ✅ Core and Infrastructure projects build successfully

### In Progress
- Moving remaining Steps to Application layer
- Moving remaining Services to Application layer
- Updating UI project references
- Verifying full solution build
