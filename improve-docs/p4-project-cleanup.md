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

## P4.1 - Split into 4 csproj Projects (Done)

### New Project Structure

```
AssetAutomator/
├── src/
│   ├── AssetAutomator.Core/           (Class Library - no WPF)
│   │   ├── Constants/                  (AppConstants, TimingConstants)
│   │   ├── Interfaces/                 (IConfigService, IBrowserService, ILogService, IImageGenProvider)
│   │   └── Models/                     (12 model files: AppSettings, AutomationTask, BatchImageItem, etc.)
│   ├── AssetAutomator.Infrastructure/  (Class Library)
│   │   ├── Helpers/                    (6 helpers: DeviceHelper, HumanBehaviourHelper, ProxyHelper, PythonServerManager, SystemRequirementsChecker, YoutubeHelper)
│   │   ├── Logging/                    (LogService)
│   │   └── Services/                   (ConfigService)
│   ├── AssetAutomator.Application/    (Class Library - WPF)
│   │   ├── Services/                   (18 services: BatchImageGenService, ChatGptService, GeminiApiService, HistoryService, ImagePoolService, LicenseService, etc.)
│   │   └── Steps/                      (10 pipeline steps: ThumbnailDownload, YoutubeTopicSuggestion, TranscriptExtraction, VoiceoverGeneration, etc.)
│   └── AssetAutomator.UI/             (WPF Application)
│       ├── Models/Nodes/               (GeminiGraphModels - WPF VM layer)
│       ├── Windows/                    (Placeholder MainWindow)
│       ├── Converters/                 (Stub)
│       ├── Resources/                  (Styles)
│       └── (App.xaml, MainWindow, MainViewModel - stubs)
├── tools/                             (Runtime tools - Scripts, PythonEmbed)
├── AssetAutomator.sln                 (Updated solution file)
├── AssetAutomator.slnx                (Backup, also updated)
└── AssetAutomator.csproj.disabled     (Old monolithic csproj - disabled)
```

### Completed in P4.1

#### Project Setup
- ✅ Created project structure under `src/`
- ✅ Created `AssetAutomator.Core.csproj` (Class Library, no WPF)
- ✅ Created `AssetAutomator.Infrastructure.csproj` (depends on Core)
- ✅ Created `AssetAutomator.Application.csproj` (depends on Core + Infrastructure)
- ✅ Created `AssetAutomator.UI.csproj` (WPF App, depends on all 3)
- ✅ Updated `AssetAutomator.sln` to reference all 4 projects
- ✅ Disabled old monolithic `AssetAutomator.csproj` (renamed to `.disabled`)

#### Core layer (12 files)
- ✅ Moved Interfaces to Core (IConfigService, IBrowserService, ILogService, IImageGenProvider)
- ✅ Moved Constants and Models to Core (AppSettings, AutomationTask, BatchImageItem, BatchProjectModel, GeminiTaskModel, GemOptionItem, HistoryTaskModel, ImageGenRequest, LicenseModels, SceneItemModel, SharedVoiceModels, YoutubeTopicSuggestionModel)
- ✅ Refactored `IImageGenProvider` to use Core `BatchImageItem` (removed duplicate)

#### Infrastructure layer (6 files + 2 services)
- ✅ Moved Helpers to Infrastructure (DeviceHelper, HumanBehaviourHelper, ProxyHelper, PythonServerManager, SystemRequirementsChecker, YoutubeHelper)
- ✅ Cleaned `ConfigService` — removed static `Instance` property
- ✅ Cleaned `PythonServerManager` — removed static `Default` property
- ✅ Cleaned `LogService` — DI-driven

#### Application layer (18 services + 10 steps)
- ✅ Moved Services to Application (BatchImageGenService, ChatGptService, GeminiApiService, GeminiPlaywrightSceneCreator, HistoryService, ImagePoolService, LicenseService, VoiceSelectorService, etc.)
- ✅ Refactored services to use DI (`IConfigService`, `ILogService`, `IBrowserService`)
- ✅ Extracted `GeminiApiModels`, `GeminiGraphNodeItem`, `PipelineBatchResult` into dedicated files
- ✅ Moved pipeline Steps to Application (10 steps: ChatGptRewrite, GeminiPlaywrightSceneBreakdown, GeminiSceneBreakdown, GeminiTopicResearch, ImageGeneration, SceneImageBatch, ThumbnailDownload, TranscriptExtraction, VoiceoverGeneration, YoutubeTopicSuggestion)
- ✅ Removed legacy `Compile Include` bridge for `GeminiPlaywrightSceneCreator` (now lives in Application)

#### UI layer (WPF models)
- ✅ Moved `GeminiGraphModels.cs` (NodeStatus, GemOptionItem, RelayCommand, GeminiConnectionViewModel, GeminiNodeViewModel, GeminiGraphViewModel) to UI/Models/Nodes

### Build Validation
- ✅ All 4 projects build with **0 errors**:
  - `dotnet build src/AssetAutomator.Core/AssetAutomator.Core.csproj` → succeeded
  - `dotnet build src/AssetAutomator.Infrastructure/AssetAutomator.Infrastructure.csproj` → succeeded
  - `dotnet build src/AssetAutomator.Application/AssetAutomator.Application.csproj` → succeeded
  - `dotnet build src/AssetAutomator.UI/AssetAutomator.UI.csproj` → succeeded
- ✅ Full solution build: `dotnet build AssetAutomator.sln` → **0 errors**

### Deferred Items
The following items were deferred to a follow-up session (require extensive using/namespace updates across ~50+ files):
- **Full WPF UI migration** (MainWindow.xaml, MainWindow.xaml.cs, MainWindow.AutomationSteps.cs, MainWindow.BatchImageGen.cs, MainWindow.GeminiCreator.cs, MainWindow.History.cs, MainWindow.Profiles.cs, MainWindow.Tasks.cs, all child Windows, Converters, MainViewModel) — current UI project has a minimal placeholder window that builds
- **Root Models/ folder** — duplicate files left in place (not compiled by any project). Safe to delete once UI migration is complete.
- **Test scripts and probe_* files** in `tools/Scripts/` — runtime tools, not built into source
