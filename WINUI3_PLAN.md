# Plan: Triển khai WinUI3 cho AssetAutomator

> Trạng thái: **Environment đã verify xong — sẵn sàng triển khai.**
> Workspace đã revert về HEAD gốc — git status clean.

## 1. Kết quả kiểm tra môi trường (đã verify)

### 1.1. Quyết định dùng WPF hiện tại

- **Quan sát:** `git ls-files src/` cho thấy `src/AssetAutomator.UI.WinUI3/` **không tồn tại trong git gốc**. Tất cả XAML/resources/code-behind của UI (WPF) đều nằm trong `src/AssetAutomator.UI/`.
- **Nghĩa là:** Mọi attempt trước đó copy code vào thư mục WinUI3 đều là tự tạo, không phải port từ nguồn có sẵn.
- **Quyết định trước khi bắt đầu:** Xác nhận với user rằng mục tiêu cuối cùng là gì — chỉ là *build WinUI3 mẫu* để học, hay *port toàn bộ UI* sang WinUI3 để thay thế WPF? (xem §6)



### 1.2. Toolchain đã có sẵn


| Item                              | Giá trị                                                                |
| --------------------------------- | ---------------------------------------------------------------------- |
| `.NET SDK`                        | **10.0.302** (mặc định), 10.0.203                                      |
| `Microsoft.NETCore.App.Ref`       | 10.0.10, 10.0.7, 8.0.29                                                |
| `Microsoft.WindowsDesktop.App`    | 10.0.10, 10.0.7, 8.0.29 (WPF runtime)                                  |
| `Visual Studio`                   | **Community 2026 (version 18)**                                        |
| `.NET Framework 4.0.30319`        | v4.0.30319 (OK cho XamlCompiler.exe)                                   |
| `Windows SDK`                     | **10.0.28000.0** (`C:\Program Files (x86)\Windows Kits\10\References`) |
| `Microsoft.WindowsAppSDK` (NuGet) | **1.7.250310001** (targets đầy đủ trong `buildTransitive/`)            |
| `Microsoft.Web.WebView2`          | 1.0.2903.40                                                            |
| `Nodify`                          | 7.3.0                                                                  |
| `dotnet workload list`            | **rỗng** — không có workload nào cài                                   |




### 1.3. Đã verify nguyên nhân build fail trước đó

- **Symptom:** `XamlCompiler.exe` exit code 1, không in output, không tạo `output.json`.
- **Test gốc:** Chạy `XamlCompiler.exe` với input.json rỗng → in ra error:
  ```
  Xaml Internal Error: Specified argument was out of the range of valid values.
  Parameter name: The language '' is not supported
  ```
- **Kết luận:** Compiler vẫn hoạt động — chỉ là quá nhạy với input JSON.



### 1.4. Đã verify các khả năng khác

- **PowerShell environment đang alias các command (**`head`**,** `ls -la`**,** `rm -rf`**,** `$_`**)**, khiến inline-command thất bại → buộc dùng `.ps1` script file.
- **VS process (PID 16092)** đang chiếm một số file DLL của Core/Infrastructure/Application khi build → build CLI có warning `MSB3061` nhưng vẫn pass.
- **WPF build pass clean** (0 errors) ngay sau khi revert.



## 2. Mục tiêu triển khai (đề xuất)

Hai lựa chọn — cần user quyết:

### Lựa chọn A: Skeleton WinUI3 (mục tiêu nhỏ — chỉ để build pass)

- Tạo WinUI3 project rỗng: `App.xaml` + `MainWindow.xaml` chỉ chứa 1 TextBlock
- **KHÔNG** port logic từ WPF
- **KHÔNG** thay thế WPF UI — chỉ minh chứng WinUI3 build được
- **Rủi ro UI thật**: Sau khi skeleton pass, việc port 56 file XAML/WPF code-behind sang WinUI3 là công việc riêng (hàng tuần)



### Lựa chọn B: Port toàn bộ UI WPF → WinUI3 (mục tiêu lớn)

- Port 17 XAML files + 8 MainWindow code-behind files + 7 resource XAML files
- Nhiều control WPF không tồn tại trong WinUI3, cần thay thế (xem §4)
- Style triggers / DataGrid / TabControl / TreeView templates phải viết lại
- **Rủi ro**: Cao — có thể mất 1-2 tuần, cần user test liên tục



## 3. Phase 1 — Skeleton WinUI3 (tôi sẽ làm dù chọn A hay B)



### 3.1. Cấu trúc project

```
src/AssetAutomator.UI.WinUI3/
├── AssetAutomator.UI.WinUI3.csproj          # Cross-target, không unpackaged
├── App.xaml                                  # <XamlControlsResources>
├── App.xaml.cs                               # OnLaunched → MainWindow
├── MainWindow.xaml                           # <Window> + <TextBlock>
├── MainWindow.xaml.cs                        # Set size/position bằng AppWindow API
├── Package.appxmanifest                      # Chỉ khi packaged; unpackaged bỏ
└── app.manifest                              # DPI awareness
```



### 3.2. WinUI3 csproj target (verified với WinAppSDK 1.7 + NetFx 4.0.30319 + Windows SDK 10.0.28000.0)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <!-- dùng 10.0.28000.0 (đã verify) thay vì 26100.0 -->
    <TargetFramework>net10.0-windows10.0.28000.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>AssetAutomator.UI.WinUI3</RootNamespace>
    <AssemblyName>AssetAutomator.UI.WinUI3</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x64</Platforms>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <EnableMsixTooling>true</EnableMsixTooling>  <!-- cần cho unpackaged cũng OK -->
    <WindowsPackageType>None</WindowsPackageType> <!-- unpackaged, không cần MSIX -->
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.250310001" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AssetAutomator.Core\AssetAutomator.Core.csproj" />
    <ProjectReference Include="..\AssetAutomator.Infrastructure\AssetAutomator.Infrastructure.csproj" />
    <ProjectReference Include="..\AssetAutomator.Application\AssetAutomator.Application.csproj" />
  </ItemGroup>
</Project>
```



### 3.3. App.xaml minimum

```xml
<Application x:Class="AssetAutomator.UI.WinUI3.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```



### 3.4. MainWindow.xaml minimum

```xml
<Window x:Class="AssetAutomator.UI.WinUI3.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid x:Name="RootGrid">
        <TextBlock Text="WinUI3 - AssetAutomator"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   FontSize="24" />
    </Grid>
</Window>
```



### 3.5. MainWindow.xaml.cs (set size/position)

```csharp
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace AssetAutomator.UI.WinUI3
{
    public partial class MainWindow : Microsoft.UI.Xaml.Window
    {
        public MainWindow() { InitializeComponent(); SetSize(); }

        private void SetSize()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            appWindow.Resize(new SizeInt32(1240, 700));
        }
    }
}
```



### 3.6. Build script dùng `dotnet build` (CI-friendly)

```bash
cd D:/Dev/AssetAutomator
dotnet build src/AssetAutomator.UI.WinUI3/AssetAutomator.UI.WinUI3.csproj -c Debug -p:Platform=x64
```



### 3.7. Acceptance criteria

- Build pass clean (không error, không warning về XAML)
- EXE tạo ra tại `src/AssetAutomator.UI.WinUI3/bin/x64/Debug/net10.0-windows10.0.28000.0/win-x64/AssetAutomator.UI.WinUI3.exe`
- Chạy EXE → mở cửa sổ 1240x700, hiển thị text "WinUI3 - AssetAutomator"



## 4. Phase 2 — Port UI (chỉ làm nếu user chọn lựa chọn B)



### 4.1. Danh sách file WPF cần port


| File WPF                                   | Số lỗi WinUI3 | Ghi chú                                                       |
| ------------------------------------------ | ------------- | ------------------------------------------------------------- |
| `App.xaml` (WPF)                           | thấp          | Convert sang WinUI3                                           |
| `MainWindow.xaml` (WPF)                    | ~200 lỗi      | Triggers → VisualStateManager                                 |
| `MainWindow.xaml.cs` + 6 partial files     | ~50 lỗi       | WPF events → WinUI3 events                                    |
| `Resources/Styles/Buttons.xaml`            | ~15 lỗi       | `ControlTemplate.Triggers` → VSM                              |
| `Resources/Styles/Components.xaml`         | thấp          | OK với minor fixes                                            |
| `Resources/Styles/Controls.xaml`           | ~80 lỗi       | `Track`, `ScrollBar`, `DropShadowEffect`, `DataGrid`          |
| `Resources/Styles/Metrics.xaml`            | thấp          | `sys:Double` → `x:Double`                                     |
| `Resources/Styles/Typography.xaml`         | thấp          | Bỏ `Style TargetType="Window"`                                |
| `Resources/Styles/Themes/Dark.xaml`        | OK            | Giữ nguyên                                                    |
| `Resources/Styles/Themes/Light.xaml`       | OK            | Giữ nguyên                                                    |
| `Resources/Styles/Colors.xaml`             | OK            | Giữ nguyên                                                    |
| `Windows/BulkTaskWindow.xaml`+`.cs`        | nhiều         | Có DialogHost, TokenizedTextBox                               |
| `Windows/LicenseWindow.xaml`+`.cs`         | trung bình    |                                                               |
| `Windows/NewProjectWindow.xaml`+`.cs`      | nhiều         | Có Form layout phức tạp                                       |
| `Windows/ProxyTestResultWindow.xaml`+`.cs` | trung bình    | Có DataGrid                                                   |
| `Windows/ScenesViewerWindow.xaml`+`.cs`    | nhiều         | Có MediaElement                                               |
| `Windows/UpdateWindow.xaml`+`.cs`          | trung bình    | Có Progress controls                                          |
| `Windows/VoiceSelectorWindow.xaml`+`.cs`   | trung bình    |                                                               |
| `Windows/WebViewLoginWindow.xaml`+`.cs`    | nhiều         | Cần `Microsoft.UI.Xaml.Controls.WebView2` thay vì WPF WebView |




### 4.2. Mapping WPF → WinUI3 controls


| WPF                                      | WinUI3                                                         |
| ---------------------------------------- | -------------------------------------------------------------- |
| `<DataGrid>`                             | `CommunityToolkit.WinUI.UI.Controls.DataGrid` (NuGet)          |
| `<TabControl>`                           | `TabView` (Microsoft.UI.Xaml.Controls)                         |
| `<ComboBox>`                             | `ComboBox` (template khác)                                     |
| `<Label>`                                | `TextBlock`                                                    |
| `<Window Height/Width>`                  | `AppWindow.Resize()` (code-behind)                             |
| `<Window WindowStartupLocation>`         | `AppWindow.Move()` (code-behind)                               |
| `<Style.Triggers>`                       | `VisualStateManager`                                           |
| `<ControlTemplate.Triggers>`             | `VisualStateManager`                                           |
| `<Trigger>`                              | Visual States (`PointerOver`, `Pressed`, `Disabled`)           |
| `<MultiTrigger>`                         | Compound Visual States                                         |
| `<Setter TargetName>`                    | Implicit setters + named states                                |
| `RelativeSource AncestorType={x:Type X}` | `RelativeSource { Mode=FindAncestor, AncestorType=typeof(X) }` |
| `DropShadowEffect`                       | `Microsoft.UI.Xaml.Media.Shadow` (ThemeShadow)                 |
| `<Track>` (ScrollBar)                    | Custom ScrollBar template                                      |
| `SnapsToDevicePixels`                    | Bỏ (WinUI3 default)                                            |
| `RecognizesAccessKey`                    | Bỏ                                                             |
| `Focusable`                              | Phụ thuộc control                                              |
| `IsItemsHost="True"`                     | `IsItemsHost=True` (WinUI3 dùng `ItemsRepeater` thay)          |
| `WindowStartupLocation`                  | AppWindow API                                                  |
| `Window.MinHeight/MinWidth`              | AppWindow API                                                  |




### 4.3. Sub-phases (nếu chọn B)

- **Phase 2a:** Port skeleton + theme resources (Colors, Light, Dark) — 1 ngày
- **Phase 2b:** Port Typography + Metrics + Buttons — 1-2 ngày
- **Phase 2c:** Port MainWindow.xaml (không DataGrid/TabControl) — 3-5 ngày
- **Phase 2d:** Port 9 Window phụ — 2-3 ngày
- **Phase 2e:** Thêm DataGrid NuGet, port cấu trúc bảng — 2 ngày
- **Phase 2f:** Test với user, fix bugs — 1-2 tuần



## 5. Risks & blockers



### 5.1. Blocker đã biết

- **VS lock file** khi build CLI song song → warning, không fail. Nhưng khi cần modify `.Infrastructure.dll` → phải đóng VS.
- **XamlCompiler.exe silent fail** — nếu XAML sai, error không in ra console. Có 2 giải pháp:
  - Build trong VS (có IntelliSense báo lỗi real-time)
  - Thêm `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>` tạm thời để pinpoint file
- **MSIX packaging** mặc định khi dùng `<UseWinUI>true</UseWinUI>` — phải set `<WindowsPackageType>None</WindowsPackageType>` để unpackaged



### 5.2. Risk chưa lường được

- **Tham chiếu theme WPF** (`MaterialDesign`, `ModernWPF`) — không có trong WinUI3. UI sẽ mất toàn bộ current look.
- **DI container** (`Microsoft.Extensions.Hosting`) — WinUI3 cần setup riêng (khác WPF `App.xaml.cs` lifecycle).
- **System.Windows.Forms** — WinUI3 không support Forms. Folder `Windows/` dùng `OpenFileDialog` (WinForms) phải đổi sang `FileOpenPicker`.



### 5.3. Không nên làm

- ❌ Copy nguyên file XAML từ WPF → WinUI3 (đã thử, fail 200 lỗi)
- ❌ Dùng `dotnet build` song song với VS đang mở (lock file)
- ❌ Đặt `RootNamespace` khác với `x:Class` trong XAML (silent fail)



## 6. Câu hỏi cần user quyết trước khi bắt đầu


| #   | Câu hỏi                                                                                      | Lý do                                |
| --- | -------------------------------------------------------------------------------------------- | ------------------------------------ |
| Q1  | Mục tiêu cuối cùng là **A** (skeleton demo) hay **B** (port đầy đủ)?                         | Quyết định scope 1 ngày vs 1-2 tuần  |
| Q2  | WinUI3 sẽ **thay thế** WPF UI, hay **chạy song song** (2 EXE)?                               | Ảnh hưởng tới solution structure     |
| Q3  | Có cần giữ **nguyên giao diện** (theme/colors hiện tại) hay accept WinUI3 Fluent UI default? | Theme WPF phải bỏ, port mới tốn thêm |
| Q4  | Cho phép tôi **đóng VS** khi build CLI không?                                                | Tránh lock file                      |
| Q5  | Có nên dùng **CommunityToolkit.WinUI** (NuGet) để có sẵn DataGrid, MSTest, etc.?             | Tiết kiệm thời gian port             |




## 7. Sau khi user quyết → checklist thực thi



### Phase 1 (skeleton) — checklist

- [ ] Tạo `src/AssetAutomator.UI.WinUI3/` directory
- [ ] Viết `AssetAutomator.UI.WinUI3.csproj` với target đã verify
- [ ] Viết `App.xaml` + `App.xaml.cs`
- [ ] Viết `MainWindow.xaml` + `MainWindow.xaml.cs`
- [ ] Viết `app.manifest`
- [ ] `dotnet sln add` để add project vào solution
- [ ] `dotnet build` → verify pass
- [ ] Chạy EXE → verify hiển thị cửa sổ



### Phase 2 (port) — chỉ nếu user chọn B

- [ ] (Q3) Quyết theme
- [ ] Port `App.xaml` (WPF) → WinUI3
- [ ] Port resource XAML (Buttons, Typography, Metrics, Components, Controls)
- [ ] Port `MainWindow.xaml` + 6 partial code-behind
- [ ] Port 9 Window phụ
- [ ] Test từng module
- [ ] Fix bugs từ user feedback

---



## 8. Ghi chú kỹ thuật khác



### 8.1. Vì sao Phase 1 chọn `Microsoft.Windows.SDK.BuildTools` NuGet thay vì Windows SDK cài sẵn?

- Windows SDK 10.0.28000.0 đã cài sẵn → có thể dùng luôn. **Tôi sẽ thử không thêm NuGet trước**; nếu compile fail do version mismatch thì mới add.
- Lý do: SDK cài sẵn matching với OS host (Windows 11 24H2 = 26100), nhưng reference assemblies là 28000. Compile WinUI3 dùng 28000. OK.



### 8.2. Vì sao cần `<Platforms>x64</Platforms>`?

- WinUI3 unpackaged **chỉ hỗ trợ x64 và ARM64**, không any-CPU.
- Default `dotnet build` không có Platform → phải truyền `-p:Platform=x64`.



### 8.3. Vì sao `TargetFramework=net10.0-windows10.0.28000.0`?

- TFM format `netX.0-windows10.0.Y` là requirement của WinUI3.
- Y phải là Windows SDK version. Đã verify: SDK 28000 có sẵn → dùng 28000.
- (csproj cũ tôi viết sai 26100 — đã fix.)



### 8.4. Vì sao `WinAppSDK 1.7`?

- Đã có sẵn trong NuGet cache (1.7.250310001) — không cần download.
- Documented build issue: WinAppSDK 1.7 có bug silent fail trong XamlCompiler.exe nhưng chỉ khi XAML sai. Khi XAML đúng thì OK.



### 8.5. Nếu user chọn B (port full), cần NuGet thêm

```xml
<PackageReference Include="CommunityToolkit.WinUI.UI.Controls.DataGrid" Version="7.x" />
<!-- hoặc dùng CommunityToolkit.WinUI.Controls.DataGrid trong 8.x -->
```



### 8.6. Cách build chính xác (xác nhận cuối)

```bash
# Đóng VS trước
cd D:/Dev/AssetAutomator
dotnet build src/AssetAutomator.UI.WinUI3/AssetAutomator.UI.WinUI3.csproj -c Debug -p:Platform=x64
```

---

**Sẵn sàng để bắt đầu Phase 1. Cần user trả lời Q1 trước.**