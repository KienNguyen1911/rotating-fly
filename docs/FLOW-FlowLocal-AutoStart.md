# Flow Diagram — Auto-start Google Flow Local khi `dotnet run`

> Companion doc cho `PLAN-FlowLocal-AutoStart.md`. Mermaid syntax — render trên GitHub hoặc VS Code extension `Markdown Preview Mermaid Support`.

---

## 1. Big-picture flow (khởi động app → server ready)

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant CLI as dotnet run
    participant App as App.OnLaunched
    participant DI as IHost (DI)
    participant MW as MainWindow
    participant Launcher as GoogleFlow2ServerLauncher
    participant Log as ILogService
    participant SB as SettingsPage.InfoBar
    participant Python as python.exe (subprocess)
    participant Port as 127.0.0.1:8787

    User->>CLI: dotnet run --project AssetAutomator.WinUI.csproj
    CLI->>App: OnLaunched(args)
    App->>DI: Host.CreateDefaultBuilder().Build()
    DI-->>App: IHost ready
    App->>MW: Activate()
    MW-->>User: Window 1240×700 hiện (Mica backdrop)
    App->>Launcher: Task.Run → EnsureRunningAsync()
    activate Launcher

    Launcher->>Log: Info("Google Flow Local: probing 127.0.0.1:8787...")
    Log-->>SB: StatusMessage = "🔄 Đang kiểm tra Google Flow Local..."

    Launcher->>Port: GET /health (HTTP probe)
    alt Server already running
        Port-->>Launcher: 200 OK
        Launcher->>Log: Success("Server already running")
        Log-->>SB: "✅ Google Flow Local sẵn sàng"
    else Server NOT running
        Port-->>Launcher: connection refused
        Launcher->>Log: Info("Spawning python...")
        Log-->>SB: "🔄 Đang bật Google Flow Local..."

        Launcher->>Launcher: ResolvePythonExecutable(rootPath)
        Note over Launcher: .venv\Scripts\python.exe ưu tiên

        alt python.exe = .venv\Scripts\python.exe
            Launcher->>Python: python -c "import httpx"
            alt httpx OK
                Python-->>Launcher: 0.28.1
            else httpx missing
                Python-->>Launcher: ModuleNotFoundError
                Launcher->>Python: python -m ensurepip --default-pip
                Launcher->>Python: python -m pip install httpx
                Python-->>Launcher: Successfully installed
                Launcher->>Python: python -c "import httpx" (verify)
                Python-->>Launcher: 0.28.1 ✅
            end
        else python.exe = system python
            Launcher->>Python: PYTHONPATH=rootPath python -m google_flow.api.app
        end

        Launcher->>Python: Spawn google_flow.api.app
        Python->>Port: bind 127.0.0.1:8787
        Python-->>Launcher: process started (PID xxx)

        loop Poll every 250ms-1s, max 15s
            Launcher->>Port: GET /health
            alt UP
                Port-->>Launcher: 200 OK
                Launcher->>Log: Success("Server ready, PID xxx")
                Log-->>SB: "✅ Google Flow Local sẵn sàng (PID xxx)"
            else NOT up yet
                Port-->>Launcher: timeout/refused
                Note over Launcher: continue polling
            end
        end

        alt Timeout 15s vẫn down
            Launcher->>Log: Error("Server did not respond in 15s")
            App->>User: ContentDialog("⚠️ Không bật được")
            Note over App: User chọn [Mở Settings] hoặc [Đóng]
        end
    end
    deactivate Launcher

    User->>MW: Click "Batch Image Gen" tab
    MW->>Launcher: (indirect) FlowLocalImageGenProvider calls 8787
    Launcher->>Port: POST /v1/images/generations
    Port-->>Launcher: image bytes
    Launcher-->>MW: hiển thị ảnh
```

---

## 2. Lifecycle flow (start → running → stop)

```mermaid
stateDiagram-v2
    [*] --> AppStarting: dotnet run
    AppStarting --> DIBuild: OnLaunched starts
    DIBuild --> WindowShown: Window.Activate()
    WindowShown --> Probing: Task.Run(EnsureRunningAsync)
    Probing --> AlreadyRunning: HTTP /health = 200
    Probing --> Spawning: HTTP /health = refused

    AlreadyRunning --> [*]: OK (no-op)
    Spawning --> CheckingDeps: ResolvePythonExecutable
    CheckingDeps --> InstallingDeps: httpx missing
    CheckingDeps --> Starting: httpx OK
    InstallingDeps --> Starting: pip install httpx OK
    InstallingDeps --> Failed: pip install fail
    Starting --> Polling: spawn python.exe
    Polling --> Running: /health 200
    Polling --> Failed: timeout 15s
    Running --> UserCancels: MainWindow.Closed
    Running --> ProcessExit: AppDomain.ProcessExit
    UserCancels --> Stopping: launcher.Stop()
    ProcessExit --> Stopping: launcher.Stop()
    Stopping --> [*]: process tree killed, port released
    Failed --> ShowDialog: ContentDialog
    ShowDialog --> [*]: user dismisses
```

---

## 3. Component diagram (kiến trúc)

```mermaid
graph TB
    subgraph WinUI[AssetAutomator.WinUI - Presentation]
        App[App.xaml.cs<br/>Composition root]
        MW[MainWindow.xaml.cs<br/>Closed → Stop]
        SP[SettingsPage.xaml<br/>InfoBar StatusMessage]
        SVM[SettingsViewModel<br/>3 fields + 3 commands]
    end

    subgraph AppLayer[AssetAutomator.Application - Business]
        BIS[BatchImageGenService]
        FLIGP[FlowLocalImageGenProvider]
    end

    subgraph Infra[AssetAutomator.Infrastructure - Technical]
        GFSL[GoogleFlow2ServerLauncher<br/>★ ENHANCED]
        PSM[PythonServerManager<br/>- Gemini, port 8000]
        LogSvc[ILogService]
    end

    subgraph Core[AssetAutomator.Core - POCO]
        Cfg[AppSettings<br/>GoogleFlow2*]
        ICfgSvc[IConfigService]
    end

    subgraph External[External]
        PyProc[python.exe<br/>google_flow.api.app]
        Port8787[(127.0.0.1:8787<br/>OpenAI-compatible API)]
    end

    App -->|DI Singleton| GFSL
    App -->|Task.Run<br/>fire-and-forget| GFSL
    MW -->|Stop on close| GFSL

    GFSL -->|reads| Cfg
    GFSL -->|uses| LogSvc
    GFSL -->|spawn| PyProc
    PyProc -->|binds| Port8787

    BIS -->|via| FLIGP
    FLIGP -->|HTTP POST| Port8787

    SP -->|bind| SVM
    SVM -->|reads/writes| Cfg
    SVM -->|urges restart| GFSL

    GFSL -.->|Reuse pattern| PSM

    classDef enhanced fill:#fef3c7,stroke:#d97706,stroke-width:2px
    class GFSL enhanced
```

---

## 4. Fix mapping (bug → giải pháp)

```mermaid
flowchart LR
    subgraph Current[Bugs hiện tại]
        B1[B1: httpx missing<br/>❌ Server crash]
        B2[B2: silent launch<br/>❌ User không biết]
        B3[B3: no Stop on close<br/>❌ Orphan port 8787]
        B4[B4: Settings ignores<br/>❌ Path không sửa được]
        B5[B5: no failure dialog<br/>❌ Phải mò log]
        B6[B6: no self-heal<br/>❌ Fresh install fail]
    end

    subgraph Fix[Giải pháp]
        F1[F1: EnsurePythonDependenciesAsync<br/>+ RunPythonAsync helper]
        F2[F2: replace Debug.WriteLine<br/>+ ILogService + Dialog]
        F3[F3: MainWindow_Closed<br/>+ ProcessExit safety net]
        F4[F4: 3 fields + 3 commands<br/>+ 1 new card in Settings]
        F5[F5: ShowFlowLocalStartupFailureDialogAsync<br/>+ Open Settings button]
        F6[F6: pip install httpx auto<br/>★ covered by F1]
    end

    B1 -->|fixed by| F1
    B2 -->|fixed by| F2
    B3 -->|fixed by| F3
    B4 -->|fixed by| F4
    B5 -->|fixed by| F5
    B6 -->|fixed by| F6
    F1 -.->|also covers| F6
```

---

## 5. User-visible timeline (UX)

```mermaid
gantt
    title Auto-start Google Flow Local — User Experience Timeline
    dateFormat  X
    axisFormat %S s

    section App
    dotnet run spawn                   :a1, 0, 1s
    DI build + host start              :a2, after a1, 1s
    Window 1240×700 activate           :crit, a3, after a2, 1s

    section Flow Local
    Probe /health (background)         :b1, 0, 0.5s
    Spawn python.exe (if needed)       :b2, after b1, 0.5s
    Install httpx (if missing)         :b3, after b2, 30s
    Cold start uvicorn                 :b4, after b2, 8s
    Server ready on 8787               :crit, b5, after b4, 0s

    section UI Feedback
    InfoBar "🔄 Đang bật..."           :c1, 0, 1s
    InfoBar "✅ Sẵn sàng"              :c2, after b5, 0s
    ContentDialog (if fail)            :c3, after b4, 0s
```

**Happy path total**: ~2s (server đã up sẵn) → ~10s (cold start) → ~35s (cold start + httpx install lần đầu).

---

## 6. Decision tree — Settings "Restart Server" button

```mermaid
flowchart TD
    Start[User clicks 'Restart Server' in Settings]
    Start --> Save[SaveSettings first]
    Save --> Read{Settings.GoogleFlow2AutoLaunch?}
    Read -->|false| DisableMsg[StatusMessage: 'Auto-launch is OFF. Bật trước.']
    Read -->|true| CheckPath{Path exists?}
    CheckPath -->|no| AskClone[StatusMessage: 'Path không tồn tại. Cài repo về đường dẫn đã nhập.']
    CheckPath -->|yes| IsRunning{IsRunningAsync?}
    IsRunning -->|yes| KillFirst[launcher.Stop + await 1.5s]
    IsRunning -->|no| Skip
    KillFirst --> Skip[Spawn new process]
    Skip --> Poll[Poll 8787]
    Poll -->|up in 15s| OK[StatusMessage: '✅ Server restarted']
    Poll -->|timeout| Fail[StatusMessage: '❌ Restart failed - xem log']
```

---

## 7. Self-healing — pip install httpx edge cases

```mermaid
flowchart TD
    Probe[python -c 'import httpx']
    Probe -->|OK| UseIt[Use existing venv]
    Probe -->|No module named 'httpx'| BugPip{'pip available?'}
    Probe -->|No module named 'pip'| SkipPip[Step 2: ensurepip]
    BugPip -->|yes| InstallPip[pip install httpx]
    BugPip -->|no| SkipPip
    SkipPip --> EnsurePip[python -m ensurepip --default-pip]
    EnsurePip -->|OK| InstallPip
    EnsurePip -->|fail| Abort[Error: cannot bootstrap pip]
    InstallPip --> Verify[pip show httpx]
    Verify -->|OK| UseIt
    Verify -->|fail| Abort
    Abort --> Dialog[ContentDialog: 'Run install.bat manually']
```

---

## 8. Files modified (visual)

```mermaid
graph LR
    subgraph Before[Hiện tại]
        F1[GoogleFlow2ServerLauncher.cs<br/>268 lines]
        F2[App.xaml.cs<br/>186 lines]
        F3[MainWindow.xaml.cs<br/>218 lines]
        F4[SettingsViewModel.cs<br/>263 lines]
        F5[SettingsPage.xaml<br/>199 lines]
    end

    subgraph After[Sau khi áp dụng plan]
        G1[GoogleFlow2ServerLauncher.cs<br/>~328 lines +60]
        G2[App.xaml.cs<br/>~226 lines +40]
        G3[MainWindow.xaml.cs<br/>~226 lines +8]
        G4[SettingsViewModel.cs<br/>~313 lines +50]
        G5[SettingsPage.xaml<br/>~239 lines +40]
    end

    F1 ==> G1
    F2 ==> G2
    F3 ==> G3
    F4 ==> G4
    F5 ==> G5

    style G1 fill:#dcfce7
    style G2 fill:#dcfce7
    style G3 fill:#dcfce7
    style G4 fill:#dcfce7
    style G5 fill:#dcfce7
```

**Total delta**: ~198 LOC, **0 files mới**, **0 dependency mới**, **0 project mới**.

---

## 9. Render trong IDE

Các diagram ở trên dùng **Mermaid** — render ngay trong:

- **VS Code**: cài extension `Markdown Preview Mermaid Support` (bcmartins.vscode-mermaid)
- **GitHub**: render tự động khi xem file `.md` trên repo
- **Cursor**: render trong preview pane

Nếu bạn muốn file PNG/SVG, nói tôi export bằng `mmdc` (Mermaid CLI).
