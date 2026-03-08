# SalmonEgg UI/运行时 SPEC（.NET 10 + Uno + WinUI3 + Skia）

更新时间：2026-03-08（Asia/Shanghai）

本文档用于**钉死**本仓库后续的 UI/运行时设计与工程约束，避免 WinUI3（Windows/MSIX）与 Skia（跨平台 Desktop）之间的语义漂移，并消灭 `x:Bind + VM 生命周期` 类问题。

---

## 0. 目标与硬约束

### 0.1 目标
- Windows：使用 **Uno + 原生 WinUI 3** 跑桌面 UI（MSIX 打包运行），支持 Mica/过渡动画等 Win11 视觉能力。
- 跨平台：保持 Uno 的 **Skia Desktop** 路径可运行（Linux/macOS），并尽量复用同一套 UI 语义与业务状态。
- 语义一致：同一操作（连接、建会话、收消息、导航返回）在 WinUI3 与 Skia 上行为一致；视觉差异仅限材质/系统能力差异。

### 0.2 硬约束（不可违背）
- **必须使用 .NET 10**（项目 TargetFramework 以 `net10.0-*` 为主）。
- 必须同时支持：
  - `net10.0-windows10.0.26100.0`（WinUI3 / Windows）
  - `net10.0-desktop`（Skia Desktop / 跨平台）
- Windows 运行路径固定为 **MSIX**（不走 unpackaged exe 运行路径）。
- 平台差异只允许出现在**宿主层（Host）**与**平台服务适配层（Adapters）**，禁止散落在页面语义/业务流程中。

---

## 1. Target Framework 与运行方式（钉死）

### 1.1 项目框架
`C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\SalmonEgg.csproj`
- `TargetFrameworks`：`net9.0-browserwasm;net10.0-desktop;net10.0-windows10.0.26100.0`

### 1.2 Windows（WinUI3）必须使用 MSIX
- `WindowsPackageType=MSIX`
- 运行脚本统一入口：`C:\Users\shang\Project\salmon-acp\run.bat`
- MSIX runner：`C:\Users\shang\Project\salmon-acp\.tools\run-winui3-msix.ps1`

**MSIX 签名与信任**
- 使用自签名证书（Subject：`O=SalmonEgg`）签名。
- 首次安装可能需要以管理员方式运行一次，以写入 `LocalMachine` 证书信任存储。

### 1.3 Skia Desktop（跨平台）
- 运行入口：`C:\Users\shang\Project\salmon-acp\run.bat desktop`
- 该路径必须能编译且核心功能可用（连接/会话/对话/设置）。

---

## 2. 绑定策略（钉死）：`x:Bind` + 初始化顺序铁律

### 2.1 结论
本项目 UI 绑定策略固定为：**以 `x:Bind` 为主**（配合 `x:DataType` 的强类型模板），并通过约束与测试确保一致行为。

### 2.2 铁律：VM 必须在 `InitializeComponent()` 之前就位
任何 `*.xaml.cs` 只要满足：
- 使用 `x:Bind` 暴露 VM（`public XxxViewModel ViewModel { get; }`）
- 且 VM 通过 DI/ServiceProvider 获取

必须遵循以下顺序：
1) `ViewModel = App.ServiceProvider.GetRequiredService<...>();`
2) `InitializeComponent();`

禁止：
- 先 `InitializeComponent()` 再赋值 VM（会导致 WinUI3/Skia 对 `x:Bind` 的观察时机不一致，出现“看似绑定但实际没绑定”的漂移）。

**守门测试（必须保持通过）**
- `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Application.Tests\UiConventionsTests.cs`

---

## 3. ViewModel 生命周期（钉死）：共享状态必须 Singleton

### 3.1 结论
凡是跨页面共享“会话/连接”语义的 VM/状态容器，必须注册为 **Singleton**，否则会出现：
- Settings 页面连接成功但 Chat 页面仍显示“准备好开始了吗？”
- WinUI3 与 Skia 页面导航后状态不一致

### 3.2 当前钉死的注册项
`ChatViewModel` 必须是 Singleton：
- `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\DependencyInjection.cs`

> 后续如果要进一步洁癖化：建议把“连接/会话”抽为 `ChatSessionState`（Singleton）+ `ChatSessionService`（Singleton），让 `ChatViewModel` 可降为 transient；但在未完成该重构前，**ChatViewModel 必须保持 Singleton**。

---

## 4. UI 语义资源（钉死）：只用 App 语义 Key，禁止旧平台 Key

### 4.1 问题定义
Uno 的资源静态解析与 WinUI3 的资源体系并非完全一致，直接使用旧资源键会导致：
- `Couldn't statically resolve resource ...` 警告噪音
- 运行时 fallback 不一致
- Skia/WinUI3 视觉语义漂移（即便布局相同，交互态颜色/背景/描边不同）

### 4.2 结论（必须执行）
- 禁止在 XAML 中使用以下旧资源键（以及同类 `SystemControl*` 旧键）：
  - `SystemControlHighlightLowBrush`
  - `SystemControlHighlightBaseLowBrush`
  - `SystemControlHighlightAccentBrush`
  - `SystemControlForegroundBase*Brush`
- 统一使用：
  - WinUI3 token（`TextFillColorPrimaryBrush`、`ControlFillColor*`、`DividerStrokeColorDefaultBrush` 等）
  - 或者更优先：**App 语义 Key**（例如 `AccentBrush`）

### 4.3 当前钉死的 App 语义 Key
`AccentBrush`（强调色）：
- 定义：`C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\App.xaml`
- 使用：所有页面强调色必须引用 `{ThemeResource AccentBrush}`，不直接引用 `SystemAccentColor`。

**守门测试（必须保持通过）**
- `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Application.Tests\UiConventionsTests.cs` 会扫描 XAML 禁止旧键。

---

## 5. 导航体系（钉死）：全局返回按钮 + 统一后退栈

### 5.1 需求
增加一个“Windows 11 设置左上角”风格的**全局返回按钮**，可持续回退（类似前端 route 的后退栈）。

### 5.2 设计约束（必须满足）
- 返回按钮只能放在**壳层**（MainPage/顶栏/标题栏附近），不允许每个页面各写一套 back 逻辑。
- 回退必须作用于同一个导航容器（`ContentFrame`），并且 WinUI3/Skia 走同一条代码路径。

### 5.3 实现规范（钉死）
必须引入 `INavigationService`（Presentation 层接口）：
- `bool CanGoBack { get; }`
- `ICommand GoBackCommand { get; }`
- `Task NavigateAsync(Route route, object? parameter = null, bool clearBackStack = false)`
- 维护统一后退栈（优先使用 Frame 自带 backstack；必要时可叠加自定义 route 栈，但必须对齐行为）

UI 层提供 `FrameNavigationService` 实现：
- 使用 `ContentFrame` 作为唯一导航容器
- `CanGoBack` 与 `GoBack` 直接映射到 Frame 能力
- 将“页面类型映射”集中到一个字典（Route → PageType），禁止散落在各页面

返回按钮 UI：
- 绑定到 `INavigationService.CanGoBack/GoBackCommand`
- `CanGoBack=false` 时隐藏或禁用（最终以 Win11 设置交互为准）

---

## 6. ACP `session/update` 反序列化（钉死）：永不因新类型崩溃

### 6.1 约束
协议侧 `sessionUpdate` discriminator 值可能演进（例如 `config_option_update`、`agent_thought_chunk` 等）。

### 6.2 结论（必须执行）
- 反序列化必须：
  - 不因未知 discriminator 或额外字段崩溃
  - 能至少“跳过未知更新”并继续处理后续消息（UI 不允许整段消息流挂死）

### 6.3 当前落地
`SessionUpdate` 多态设置必须启用：
- `IgnoreUnrecognizedTypeDiscriminators=true`
- `UnknownDerivedTypeHandling=FallBackToBaseType`
- `JsonExtensionData` 用于保留未知字段（可选用于调试）

并显式支持：
- `config_option_update`（映射到 config update 类型）
- `agent_thought_chunk`（如不展示也必须能解析/跳过而不崩溃）

实现位置：
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Domain\Models\Protocol\SessionUpdateTypes.cs`

---

## 7. “尽量复用代码路径”落地清单（必须遵守）

### 7.1 禁止项
- 禁止在页面语义/业务流程里出现 `#if WINDOWS` 来实现不同逻辑（除非是“窗口/系统材质/平台 API”）。
- 禁止 UI 状态散落在多个 VM 实例中（必须通过 singleton 状态或 singleton VM 收敛）。
- 禁止直接依赖旧资源键，导致某 target 下缺失/警告/漂移。

### 7.2 允许项（平台差异出口）
- 窗口材质（Mica/Acrylic）与 windowing：只允许在 Host（`App`/Window 初始化）中做。
- 平台能力（文件选择器、剪贴板、通知等）：通过 `IPlatformService` 接口注入。

---

## 8. 验收标准（Definition of Done）

每次涉及 UI/导航/会话的改动，必须满足：
- `dotnet test tests\SalmonEgg.Application.Tests\SalmonEgg.Application.Tests.csproj` 通过
- `net10.0-desktop` 与 `net10.0-windows...` 均能编译
- 连接成功后：
  - Settings 侧有明确成功/失败反馈
  - Chat 侧不再显示“准备好开始了吗？”（会话激活）
  - 收到 `session/update` 不再出现“Failed to process session/update notification ... metadata property conflict”类错误
- WinUI3 与 Skia 导航行为一致（包括回退栈）

