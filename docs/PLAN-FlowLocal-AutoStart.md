# Plan — Auto-start Google Flow Local cùng `dotnet run` (AssetAutomator.WinUI)

> **Mục tiêu của user** (high-level, đã có sẵn một phần):
> > "khi chạy `dotnet run` => mở app thì sẽ đồng thời bật google-flow để tạo ảnh"
>
> User muốn **một entry-point duy nhất**: chạy app WinUI → server Python Google Flow Local tự bật → user có thể bắt đầu tạo ảnh ngay (qua tab **Batch Image Gen** hoặc **Pool**).

---

## 1. Trạng thái hiện tại (audit codebase)

### 1.1 Đã có sẵn ✅

| Thành phần | File | Trạng thái |
|---|---|---|
| `GoogleFlow2ServerLauncher` (probe + spawn + poll) | `src/AssetAutomator.Infrastructure/Helpers/GoogleFlow2ServerLauncher.cs` | ✅ OK, 268 dòng |
| Auto-launch fire-and-forget trong `App.OnLaunched` | `src/AssetAutomator.WinUI/App.xaml.cs:156-174` | ✅ Có, NHƯNG silent — không có UI feedback |
| `AppSettings` có đủ `GoogleFlow2RootPath`, `GoogleFlow2Port`, `GoogleFlow2AutoLaunch` | `src/AssetAutomator.Core/Models/AppSettings.cs:32-44` | ✅ Có |
| `BatchImageGenService` + `FlowLocalImageGenProvider` (HTTP client tới 8787) | `src/AssetAutomator.Application/Services/...` | ✅ Có, sẵn sàng dùng |
| `ILogService` (log realtime — `InfoBar` đang bind `StatusMessage`) | `src/AssetAutomator.Infrastructure/Logging/LogService.cs` | ✅ Có |
| `SettingsPage` đã có `InfoBar` ở dòng 17 (`StatusMessage`) | `src/AssetAutomator.WinUI/Views/Pages/SettingsPage.xaml:17` | ✅ Có |

### 1.2 Còn thiếu / bug ❌

| # | Vấn đề | Ảnh hưởng | Mức |
|---|---|---|---|
| **B1** | `.venv` của `google-flow-2.0.0` thiếu `httpx` (install.bat chỉ ship standalone wheels, thiếu pure-Python libs) | Server crash ngay khi start với `ModuleNotFoundError: httpx` | **Critical** — chưa fix cho auto-launch flow |
| **B2** | Auto-launch trong `App.xaml.cs` chỉ `Debug.WriteLine`, không đẩy lên `ILogService` | User mở app không thấy gì, tưởng chưa chạy | **High** |
| **B3** | `MainWindow_Closed` chỉ stop `PythonServerManager` (Gemini), **không stop Flow Local** → orphan process giữ port 8787 | App tắt → server vẫn sống → khởi động lần sau có thể fail "address already in use" | **High** |
| **B4** | `SettingsViewModel.LoadSettings()` / `SaveSettings()` **ignore** `GoogleFlow2RootPath`, `GoogleFlow2Port`, `GoogleFlow2AutoLaunch` | User không sửa được path từ UI nếu clone repo chỗ khác | **Medium** |
| **B5** | Khi auto-launch **fail**, không có ContentDialog nổi lên — user phải tự mò vào `Settings` | UX kém | **Medium** |
| **B6** | Không kiểm tra/install `httpx` self-healing → fresh install trên máy mới sẽ fail | B1 chỉ fix 1 lần, không self-healing | **Low** (đã doc ở turn trước) |

---

## 2. Đề xuất kiến trúc (tối ưu nhất)

### 2.1 Nguyên tắc thiết kế

1. **Không viết business logic trong code-behind UI** (tuân thủ `.agents/AGENTS.md` rule §2)
2. **DI-first** — `GoogleFlow2ServerLauncher` đã là singleton, không cần đổi
3. **Tái sử dụng** `ILogService` cho UI feedback (đã có sẵn `SettingsPage.InfoBar` bind `StatusMessage`)
4. **Single entry-point** — không tạo API mới, chỉ làm trọn vẹn flow đã có
5. **Self-healing** — auto-install `httpx` nếu thiếu (fix B1 + B6 cùng lúc)

### 2.2 Cấu trúc thay đổi

```
src/AssetAutomator.Infrastructure/Helpers/GoogleFlow2ServerLauncher.cs   ← SỬA (B1, B6)
src/AssetAutomator.WinUI/App.xaml.cs                                     ← SỬA (B2, B5)
src/AssetAutomator.WinUI/MainWindow.xaml.cs                              ← SỬA (B3)
src/AssetAutomator.WinUI/ViewModels/SettingsViewModel.cs                 ← SỬA (B4)
src/AssetAutomator.WinUI/Views/Pages/SettingsPage.xaml                   ← SỬA (B4)
```

**Không tạo project mới, không tạo service mới** — tận dụng 100% hạ tầng đã có.

---

## 3. Flow tổng thể (end-to-end)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  USER: dotnet run --project .../AssetAutomator.WinUI.csproj               │
└────────────────────────────────┬────────────────────────────────────────────┘
                                 │
                                 ▼
   ┌──────────────────────────────────────────────────────┐
   │  App.OnLaunched  (src/AssetAutomator.WinUI/App.xaml) │
   │  ── 1. Build IHost (DI) ─────────────────────────── │
   │  ── 2. Activate MainWindow ──────────────────────── │
   │  ── 3. Fire-and-forget Task.Run:                   │
   │       await launcher.EnsureRunningAsync()           │
   └─────────────────────────┬────────────────────────────┘
                             │
                             ▼
   ┌──────────────────────────────────────────────────────────────┐
   │  GoogleFlow2ServerLauncher.EnsureRunningAsync() (NEW)       │
   │  ─────────────────────────────────────────────────────────── │
   │  1. settings.GoogleFlow2AutoLaunch ?                        │
   │     │                                                        │
   │     ├── false → log "disabled", return (true, ...)          │
   │     │                                                        │
   │     └── true ↓                                               │
   │  2. settings.GoogleFlow2RootPath exists ?                   │
   │     │                                                        │
   │     ├── no → ❌ ContentDialog "Chưa cài Google Flow Local"  │
   │     │       → show "Open Settings" button                  │
   │     │       → return (false, ...)                          │
   │     │                                                        │
   │     └── yes ↓                                               │
   │  3. IsRunningAsync() (HTTP probe /health) ?                 │
   │     │                                                        │
   │     ├── already running → return (true, ...)                │
   │     │                                                        │
   │     └── not running ↓                                       │
   │  4. EnsurePythonDependencies()  ← NEW (B1+B6)              │
   │     ├── probe .venv\Scripts\python.exe                      │
   │     ├── python -m ensurepip --default-pip (if missing)      │
   │     ├── python -m pip install httpx (if missing)            │
   │     └── log progress to ILogService                        │
   │  5. python -m google_flow.api.app --host 127.0.0.1 --port 8787  │
   │  6. Poll port 8787 every 250ms → ~15s timeout               │
   │     │                                                        │
   │     ├── up → ✅ log Success → return (true, ...)            │
   │     │                                                        │
   │     └── timeout → ❌ ContentDialog "Không bật được server" │
   │                    → show "Xem log" / "Open Settings"       │
   │                    → return (false, ...)                     │
   └──────────────────────────────────────────────────────────────┘
                             │
                             ▼
   ┌──────────────────────────────────────────────────────────────┐
   │  App closes (MainWindow.Closed)                             │
   │  ─────────────────────────────────────────────────────────── │
   │  GetRequiredService<GoogleFlow2ServerLauncher>().Stop()      │
   │  (kills python.exe tree, releases port 8787)                 │
   └──────────────────────────────────────────────────────────────┘
```

### 3.1 Timeline trên UI (user thấy gì)

| T+ (s) | UI state | Log |
|---|---|---|
| 0.0 | App boot, MainWindow 1240×700 hiện | `[Info] AssetAutomator starting...` |
| 0.1 | Mica backdrop load xong | `[Info] Google Flow Local: probing 127.0.0.1:8787...` |
| 0.2 | nếu server up sẵn → StatusMessage = "✅ Google Flow Local sẵn sàng" | `[Info] Server already running` |
| 0.3 | nếu phải bật → StatusMessage = "🔄 Đang bật Google Flow Local..." | `[Info] Spawning python -m google_flow.api.app...` |
| 0.4 | (B1+B6) nếu `httpx` thiếu → `SettingsPage` show InfoBar Severity=Warning: "Đang cài httpx..." | `[Info] Installing httpx via pip...` |
| 2-5 | server bind port thành công → InfoBar chuyển Severity=Success: "✅ Google Flow Local sẵn sàng (PID xxxx, port 8787)" | `[Success] Server ready PID xxxx` |
| 5+ | user click **Batch Image Gen** → gọi provider → request tới 127.0.0.1:8787/v1 thành công | (happy path) |
| 5+ | nếu fail → **ContentDialog** mở lên: "Không bật được Google Flow Local. Nguyên nhân: [diag]. [Open Settings] [View Log] [Đóng]" | `[Error] {diag}` |

### 3.2 Edge cases đã handle

| Case | Hành vi |
|---|---|
| Repo chưa clone (path rỗng/sai) | ContentDialog "Cài Google Flow Local" + link GitHub |
| `httpx` thiếu (fresh install) | Auto `pip install httpx` trong `.venv`, retry 1 lần |
| Port 8787 bị chiếm (orphan) | Reuse `KillOrphanedListenersOnPort` pattern từ `PythonServerManager` (xref `PythonServerManager.cs:231`) |
| App tắt đột ngột (crash) | `MainWindow_Closed` đảm bảo kill tree; **B3 fix** |
| User tắt auto-launch trong Settings | `EnsureRunningAsync` early-return `(true, "disabled")` — không block app |
| User đổi `RootPath` lúc đang chạy | Cần reload — banner "Restart required" trong SettingsPage (B4) |

---

## 4. Chi tiết implementation

### 4.1 Fix B1 + B6 — `GoogleFlow2ServerLauncher.cs`

**Thêm method mới** `EnsurePythonDependenciesAsync(string venvPython, CancellationToken ct)`:

```csharp
private async Task<bool> EnsurePythonDependenciesAsync(
    string venvPython, CancellationToken ct)
{
    // 1. Probe: import httpx
    var probe = await RunPythonAsync(venvPython, "-c \"import httpx; print(httpx.__version__)\"", ct);
    if (probe.ExitCode == 0)
    {
        _log.Debug(LogCategory.PythonServer, $"httpx OK ({probe.Stdout.Trim()})");
        return true;
    }

    // 2. Bootstrap pip if missing
    if (probe.Stderr.Contains("No module named 'pip'", StringComparison.OrdinalIgnoreCase))
    {
        _log.Info(LogCategory.PythonServer, "Bootstrapping pip into .venv...");
        var ensure = await RunPythonAsync(venvPython, "-m ensurepip --default-pip", ct);
        if (ensure.ExitCode != 0)
        {
            _log.Error(LogCategory.PythonServer, $"ensurepip failed: {ensure.Stderr}");
            return false;
        }
    }

    // 3. Install httpx
    _log.Info(LogCategory.PythonServer, "Installing httpx into .venv...");
    var install = await RunPythonAsync(venvPython, "-m pip install httpx", ct, timeoutMs: 90_000);
    if (install.ExitCode != 0)
    {
        _log.Error(LogCategory.PythonServer, $"pip install httpx failed: {install.Stderr}");
        return false;
    }

    // 4. Verify
    var verify = await RunPythonAsync(venvPython, "-c \"import httpx; print(httpx.__version__)\"", ct);
    if (verify.ExitCode != 0)
    {
        _log.Error(LogCategory.PythonServer, $"httpx still missing after install: {verify.Stderr}");
        return false;
    }
    _log.Success(LogCategory.PythonServer, $"httpx installed: {verify.Stdout.Trim()}");
    return true;
}
```

**Helper `RunPythonAsync`**:

```csharp
private async Task<(int ExitCode, string Stdout, string Stderr)> RunPythonAsync(
    string pythonExe, string args, CancellationToken ct, int timeoutMs = 10_000)
{
    var psi = new ProcessStartInfo
    {
        FileName = pythonExe,
        Arguments = args,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    using var proc = Process.Start(psi)!;
    var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
    var stderrTask = proc.StandardError.ReadToEndAsync(ct);
    await using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeoutMs);
    try { await proc.WaitForExitAsync(cts.Token); }
    catch (OperationCanceledException) { try { proc.Kill(true); } catch { } }
    return (proc.ExitCode, await stdoutTask, await stderrTask);
}
```

**Trong `EnsureRunningAsync`, sau khi resolve `pythonExe`, thêm:**

```csharp
if (pythonExe != "python")
{
    bool depsOk = await EnsurePythonDependenciesAsync(pythonExe, CancellationToken.None);
    if (!depsOk)
    {
        string msg = "Thiếu dependency httpx và không cài được tự động. " +
                     "Chạy thủ công: .venv\\Scripts\\python.exe -m pip install httpx";
        _log.Error(LogCategory.PythonServer, msg);
        return (false, msg);
    }
}
```

### 4.2 Fix B2 — `App.xaml.cs`

**Thay dòng 156-174** (fire-and-forget silent) bằng:

```csharp
// Auto-start Google Flow Local server (fire-and-forget với UI feedback).
// CHỨA: 3 chỗ user có thể thấy feedback
//   1. ILogService → SettingsPage.InfoBar (real-time)
//   2. ContentDialog nếu fail (modal)
//   3. Debug.WriteLine (dev)
_ = Task.Run(async () =>
{
    var launcher = Services.GetService<GoogleFlow2ServerLauncher>();
    if (launcher == null) return;
    try
    {
        var (ok, diag) = await launcher.EnsureRunningAsync();
        if (!ok)
        {
            // Dispatch to UI thread for ContentDialog
            await _window!.DispatcherQueue.EnqueueAsync(async () =>
            {
                await ShowFlowLocalStartupFailureDialogAsync(diag);
            });
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[Flow Local] auto-launch threw: {ex}");
    }
});
```

**Thêm helper `ShowFlowLocalStartupFailureDialogAsync`** (private method trong App):

```csharp
private async Task ShowFlowLocalStartupFailureDialogAsync(string diagnostics)
{
    var dialog = new ContentDialog
    {
        Title = "⚠️ Google Flow Local không khởi động được",
        Content = $"Không bật được Google Flow Local server.\n\nChi tiết:\n{diagnostics}\n\n"
                  + "Bạn có thể tạo ảnh bằng provider khác (G-Labs) hoặc cấu hình lại đường dẫn trong Settings.",
        CloseButtonText = "Đóng",
        PrimaryButtonText = "Mở Settings",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = _window!.Content.XamlRoot
    };
    dialog.PrimaryButtonClick += (_, _) =>
    {
        // Navigate to Settings page
        if (_window?.Content is Grid root)
        {
            var navView = FindNavView(root);
            navView?.Navigate(typeof(Views.Pages.SettingsPage));
        }
    };
    try { await dialog.ShowAsync(); } catch { /* dialog is best-effort */ }
}
```

> **Lưu ý**: `_window.DispatcherQueue` cần dùng `Microsoft.UI.Dispatching.DispatcherQueueExtensions.EnqueueAsync` (extension). Đã có sẵn trong WinAppSDK 1.7.

### 4.3 Fix B3 — `MainWindow.xaml.cs`

**Thêm 1 dòng trong `MainWindow_Closed`** (sau `pythonServerManager?.StopServer();`):

```csharp
var flowLauncher = App.Services.GetService<AssetAutomator.Infrastructure.Helpers.GoogleFlow2ServerLauncher>();
flowLauncher?.Stop();
```

Plus nếu muốn robust hơn với crash exit, **bonus**: thêm ở `App.xaml.cs` cuối constructor:

```csharp
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    App.Services.GetService<GoogleFlow2ServerLauncher>()?.Stop();
    App.Services.GetService<PythonServerManager>()?.StopServer();
};
```

### 4.4 Fix B4 — `SettingsViewModel.cs` + `SettingsPage.xaml`

**Thêm 3 fields mới vào `SettingsViewModel`**:

```csharp
[ObservableProperty] private string _googleFlow2RootPath = @"D:\Dev\google-flow-2.0.0";
[ObservableProperty] private int _googleFlow2Port = 8787;
[ObservableProperty] private bool _googleFlow2AutoLaunch = true;
```

**Sửa `LoadSettings`**: thêm 3 dòng assign.

**Sửa `SaveSettings`**: thêm 3 dòng assign + check `RestartRequired` flag.

**Sửa `SettingsPage.xaml`**: thêm 1 card mới "Google Flow Local Server" vào cột phải (dưới `ChromeProfilesDir`), binding 3 fields + 1 button "Browse..." + 1 nút "Restart Server" (gọi launcher.EnsureRunningAsync).

```xml
<!-- Card mới: Google Flow Local Server -->
<Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
        BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
        BorderThickness="1"
        CornerRadius="8"
        Padding="16"
        Margin="0,16,0,0">
    <StackPanel Spacing="12">
        <TextBlock Text="Google Flow Local Server" Style="{ThemeResource SubtitleTextBlockStyle}" />

        <StackPanel Spacing="6">
            <TextBlock Text="Đường dẫn repo (GoogleFlow2RootPath)" Style="{ThemeResource CaptionTextBlockStyle}" Opacity="0.7" />
            <Grid ColumnSpacing="8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBox Text="{Binding GoogleFlow2RootPath, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                <Button Grid.Column="1" Content="Browse..." Command="{Binding BrowseFlow2RootCommand}" />
            </Grid>
        </StackPanel>

        <StackPanel Orientation="Horizontal" Spacing="12">
            <NumberBox Header="Port" Value="{Binding GoogleFlow2Port, Mode=TwoWay}"
                       Minimum="1024" Maximum="65535" SmallChange="1" Width="180" />
            <ToggleSwitch Header="Auto-launch on app start"
                          IsOn="{Binding GoogleFlow2AutoLaunch, Mode=TwoWay}"
                          OnContent="Bật" OffContent="Tắt"
                          VerticalAlignment="Bottom" />
        </StackPanel>

        <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Content="Restart Server" Command="{Binding RestartFlow2Command}" />
            <Button Content="Test Connection" Command="{Binding TestFlow2Command}" />
        </StackPanel>
    </StackPanel>
</Border>
```

**Thêm 3 commands mới vào `SettingsViewModel`**:

```csharp
[RelayCommand] private void BrowseFlow2Root() { /* reusable FolderPicker */ }

[RelayCommand]
private async Task RestartFlow2Async()
{
    var launcher = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
        .GetService<GoogleFlow2ServerLauncher>(App.Services);
    // ...await launcher.EnsureRunningAsync(...)
}

[RelayCommand]
private async Task TestFlow2Async()
{
    var launcher = ...;
    var (ok, diag) = await launcher.IsRunningAsync();
    StatusMessage = ok ? "✅ Flow Local OK" : $"❌ {diag}";
}
```

### 4.5 Bonus — Visual indicator trên BottomBar (optional)

Nếu user muốn thấy "Flow Local: ✅" / "❌" real-time trên **mọi page** (không chỉ Settings), thêm binding nhỏ vào `MainWindow.xaml` — 1 `TextBlock` cạnh `StatusMessage`:

```xml
<TextBlock x:Name="FlowLocalIndicator" Text="🔄 Flow Local..." />
```

bound tới 1 `MainWindowViewModel` mới (hoặc bind thẳng vào `GoogleFlow2ServerLauncher.IsRunning` raise event). **Để optional** — bạn quyết định.

---

## 5. Files sẽ tạo / sửa

| File | Action | LOC delta |
|---|---|---|
| `src/AssetAutomator.Infrastructure/Helpers/GoogleFlow2ServerLauncher.cs` | SỬA — thêm `EnsurePythonDependenciesAsync`, `RunPythonAsync` | +60 |
| `src/AssetAutomator.WinUI/App.xaml.cs` | SỬA — thêm `ShowFlowLocalStartupFailureDialogAsync`, sửa auto-launch | +40 |
| `src/AssetAutomator.WinUI/MainWindow.xaml.cs` | SỬA — thêm `flowLauncher?.Stop()` + ProcessExit safety net | +8 |
| `src/AssetAutomator.WinUI/ViewModels/SettingsViewModel.cs` | SỬA — 3 fields + 3 commands | +50 |
| `src/AssetAutomator.WinUI/Views/Pages/SettingsPage.xaml` | SỬA — 1 card mới "Google Flow Local Server" | +40 |

**Tổng ~198 LOC**, **0 file mới**, **0 project mới**, **0 dependency mới**.

---

## 6. Self-test plan (đã test 1 phần ở turn trước)

| # | Test | Expected | Status |
|---|---|---|---|
| 1 | `httpx` import lỗi → auto-install | Server bind 8787 thành công | ✅ Đã pass (turn trước) |
| 2 | Auto-launch fail (path rỗng) | ContentDialog hiện với nút "Mở Settings" | ⏳ Implement xong test |
| 3 | App đóng → process killed | `netstat -ano \| findstr :8787` rỗng | ⏳ Test |
| 4 | Sửa `RootPath` trong Settings → Save → Restart button | Server dừng + bật lại ở path mới | ⏳ Test |
| 5 | `dotnet run` fire-and-forget không block UI | MainWindow 1240×700 hiện trong <2s | ⏳ Test |

---

## 7. Rủi ro & mitigation

| Rủi ro | Mitigation |
|---|---|
| `pip install httpx` mất >30s (chậm mạng) | Tăng `timeoutMs` lên 90s; nếu timeout → ContentDialog gợi ý chạy tay |
| Port 8787 bị chiếm bởi app khác (không phải Flow Local) | Reuse `KillOrphanedListenersOnPort` pattern (`PythonServerManager.cs:231`) — đã có logic này |
| User downgrade `python` (xoá `.venv`) | `EnsurePythonDependenciesAsync` sẽ fail → ContentDialog "Clone repo lại" |
| Race condition: 2 instance app mở cùng lúc | `IsRunningAsync` sẽ thấy 1 instance đã bind → reuse (no double-spawn) |
| `ContentDialog` gọi 2 lần (re-fire-and-forget) | Dùng `_dialogShown` flag static trong App |

---

## 8. Câu hỏi cần user xác nhận trước khi code

| # | Câu hỏi | Default (nếu không trả lời) |
|---|---|---|
| **Q1** | Auto-restart server khi user đổi `RootPath` từ Settings? | **Có** — Save Settings → tự restart nếu path cũ đang được dùng |
| **Q2** | Có hiện ContentDialog khi launch fail, hay chỉ ghi log? | **ContentDialog** (đỡ phải mò) |
| **Q3** | Có thêm visual indicator (icon 🔄/✅) ở BottomBar không? | **Có** — minimal: 1 TextBlock cạnh status |
| **Q4** | `pip install httpx` mạng nội bộ có dùng custom index URL không? | **Không** — dùng pypi.org mặc định |

---

## 9. Next step

Bạn confirm:
1. ✅/❌ **Q1** (auto-restart on settings change)
2. ✅/❌ **Q2** (ContentDialog on fail)
3. ✅/❌ **Q3** (BottomBar indicator)
4. ✅/❌ **Q4** (custom pip index)

→ Tôi sẽ implement theo thứ tự: **B1+B6 → B3 → B2 → B4 → B5**, build sau mỗi bước, verify 0 errors 0 warnings (theo `.agents/AGENTS.md` rule §3).
