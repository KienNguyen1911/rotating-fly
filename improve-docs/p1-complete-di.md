# P1.3 — DI đầy đủ cho `PipelineOrchestrator`

## 🎯 Mục tiêu

Bỏ mọi `new ...Step()` và `ConfigService.Instance!` bên trong `PipelineOrchestrator` — inject tất cả dependency qua constructor (DIP — Dependency Inversion Principle).

## 🔍 Vấn đề trước refactor

```csharp
// ❌ Trong PipelineOrchestrator (trước):
private readonly GeminiApiService _geminiApiService;

public PipelineOrchestrator(GeminiApiService geminiApiService, ...) { ... }

// ❌ Trong RunStageDeepResearchAsync:
var topicResearchStep = new GeminiTopicResearchStep(_geminiApiService);  // new trực tiếp!

// ❌ Trong RunStageVoiceoverAsync:
var voiceoverStep = new VoiceoverGenerationStep(ConfigService.Instance!);  // Service Locator anti-pattern!

// ❌ Trong RunStageSceneCreatorAsync:
var sceneBreakdownStep = new GeminiPlaywrightSceneBreakdownStep();  // new trực tiếp!

// ❌ Trong RunStageImageGenAsync:
var imageBatchStep = new SceneImageBatchStep(new BatchImageGenService());  // new trực tiếp + new lồng new!
```

**Hậu quả:**
- DI container có nhưng không dùng → mất ý nghĩa.
- Unit test không thể mock step.
- `ConfigService.Instance!` là "Service Locator anti-pattern" + bypass toàn bộ DI.

## ✏️ Thay đổi

### Constructor mới của `PipelineOrchestrator`

```csharp
public PipelineOrchestrator(
    IConfigService configService,
    ILogService logService,
    GeminiApiService geminiApiService,
    GeminiTopicResearchStep topicResearchStep,
    VoiceoverGenerationStep voiceoverStep,
    GeminiPlaywrightSceneBreakdownStep sceneBreakdownStep,
    SceneImageBatchStep imageBatchStep,
    int maxDeepResearch = 2,
    int maxVoiceover = 3,
    int maxSceneCreator = 4,
    int maxImageGen = 1)
```

### Các `RunStage...Async` giờ chỉ gọi `_xxxStep.ExecuteAsync(...)`

```csharp
// Trước:
var topicResearchStep = new GeminiTopicResearchStep(_geminiApiService);
await topicResearchStep.ExecuteAsync(...);

// Sau:
await _topicResearchStep.ExecuteAsync(...);
```

### Call site trong `MainWindow.GeminiCreator.cs`

```csharp
_pipelineOrchestrator ??= new PipelineOrchestrator(
    configService: Services.ConfigService.Instance!,
    logService: _logService,
    geminiApiService: _geminiApiService,
    topicResearchStep: topicResearchStep,
    voiceoverStep: voiceoverStep,
    sceneBreakdownStep: sceneBreakdownStep,
    imageBatchStep: imageBatchStep,
    maxDeepResearch: 2,
    maxVoiceover: 3,
    maxSceneCreator: 4,
    maxImageGen: 1
);
```

Lưu ý: Ở `MainWindow.GeminiCreator.cs` các step service đã được khởi tạo cho `GeminiVideoPipelineService` (legacy) nên tái sử dụng — không duplicate.

## ✅ Verify

```bash
dotnet build
```

**Kết quả:** 0 errors, 33 warnings (đều pre-existing hoặc obsolete từ P1.5).

## 📊 Tác động

| Metric | Trước | Sau |
|---|---|---|
| `new ...Step()` trong orchestrator | 4 | 0 |
| `ConfigService.Instance!` trong orchestrator | 2 | 0 |
| Field dependencies của orchestrator | 1 (`GeminiApiService`) | 7 (đầy đủ) |
| Khả năng unit-test orchestrator | Không thể | Có thể (mock tất cả dep) |

## 💡 Bài học

DI không phải "đăng ký rồi thôi" — cần:
1. **Inject qua constructor** (không `new` bên trong).
2. **Không gọi Service Locator** (`ConfigService.Instance!`).
3. **Caller chịu trách nhiệm resolve** — caller nhận đầy đủ dep từ DI, sau đó pass xuống.

## 📌 Còn lại

- `App.xaml.cs` đã đăng ký `services.AddSingleton<PipelineOrchestrator>()` — DI sẽ tự resolve. Code thủ công ở `MainWindow.GeminiCreator.cs` chỉ là 1 chỗ demo lazy-init.
- `GeminiCookieSyncService` vẫn `new BrowserService(_log)` bên trong — sẽ xử lý ở P2.x.