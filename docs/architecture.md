# SalmonEgg 架构文档

## 概述

SalmonEgg 是一个基于 Uno Platform 的跨平台原生应用程序，实现 Agent Client Protocol (ACP) 以与 AI 代理进行通信。该项目采用现代架构设计原则，强调代码质量、可维护性和可扩展性。

## 架构模式

本项目采用 **MVVM (Model-View-ViewModel)** 架构模式结合 **Clean Architecture** 原则：

> 行为级硬约束（会话切换 / 导航 / 搜索并发语义）见：`docs/hard-constraints-session-navigation-and-search.md`。  
> 如与一般性描述冲突，以硬约束文档与 `AGENTS.md` 为准。

### 分层架构

```
Presentation Layer (Uno/WinUI3 Views + ViewModels in Presentation.Core)
       ↓
Application Layer (Use Cases / Services)
       ↓
Domain Layer (Models / Interfaces)
       ↑
Infrastructure Layer (Network / Storage / Logging)
```

各层职责：

1. **Presentation Layer**：Uno Platform XAML 视图 (`SalmonEgg/SalmonEgg/Presentation/`) 和跨平台共享 ViewModel/Service 逻辑 (`src/SalmonEgg.Presentation.Core/`)。View 完全由 ViewModel 驱动，不包含业务规则。
2. **Application Layer** (`src/SalmonEgg.Application/`)：应用服务与用例编排。
3. **Domain Layer** (`src/SalmonEgg.Domain/`)：核心业务模型与接口。纯 .NET，不引用 UI 类型。
4. **Infrastructure Layer** (`src/SalmonEgg.Infrastructure/` + `src/SalmonEgg.Infrastructure.Desktop/`)：外部依赖实现（网络传输、存储、日志）。桌面专用能力（`Stdio` 子进程、本地文件系统）集中在 `Infrastructure.Desktop`。

平台差异实现必须集中在 `SalmonEgg/SalmonEgg/Platforms/` 下或平台服务中，禁止散落在 ViewModel 或业务逻辑里。

## 项目结构

```
SalmonEgg.sln
├── SalmonEgg/
│   └── SalmonEgg/                     # Uno Platform 主项目（单项目多 TFM）
│       ├── Presentation/
│       │   ├── Views/                 # XAML 视图
│       │   ├── ViewModels/            # 平台视图绑定层（薄层）
│       │   └── ...                    # Converters、Behaviors、Controls 等
│       ├── Platforms/                 # 平台专用代码（Windows/WebAssembly/Desktop/...）
│       └── DependencyInjection.cs     # DI 容器配置
│
├── src/
│   ├── SalmonEgg.Presentation.Core/   # 跨平台共享 ViewModel / Service 接口
│   │   ├── ViewModels/                # 主要 ViewModel 实现（Navigation、Chat、Settings 等）
│   │   └── Services/                  # Presentation 层服务接口与实现
│   │
│   ├── SalmonEgg.Application/         # 应用层（用例 / 服务编排）
│   │   ├── Services/                  # 应用服务
│   │   └── UseCases/                  # 业务用例
│   │
│   ├── SalmonEgg.Domain/              # 领域层（模型 / 接口）
│   │   ├── Models/                    # 领域模型（ACP 消息、配置、会话等）
│   │   └── Services/                  # 领域服务接口
│   │
│   ├── SalmonEgg.Infrastructure/      # 基础设施层（跨平台部分）
│   │   ├── Client/                    # ACP 客户端与传输工厂
│   │   ├── Network/                   # WebSocket / HTTP SSE 传输实现
│   │   ├── Storage/                   # 配置持久化（YAML + 安全存储）
│   │   └── Logging/                   # 日志配置
│   │
│   └── SalmonEgg.Infrastructure.Desktop/  # 基础设施层（桌面专用）
│       ├── Services/                  # 桌面专用平台服务（文件系统访问等）
│       └── Transport/                 # Stdio 子进程传输实现
│
└── tests/
    ├── SalmonEgg.Application.Tests/
    ├── SalmonEgg.Domain.Tests/
    ├── SalmonEgg.Infrastructure.Tests/
    ├── SalmonEgg.Presentation.Core.Tests/
    ├── SalmonEgg.GamepadBridge.Windows/    # 手柄输入诊断（Windows）
    └── SalmonEgg.GuiTests.Windows/         # GUI smoke（Windows FlaUI）
```

GUI smoke 不共享单一 driver。Windows 原生行为使用 FlaUI/UIA3，BrowserWasm 行为使用 Playwright/Chromium，Skia Desktop 行为使用 `scripts/gates/run-skia-desktop-gui-smoke-gates.sh` 在真实 `net10.0-desktop` 产物上验证 shell readiness；Linux 还通过 X11 probe 验证窗口映射、非空像素、host-window focus 和 XTest 键盘输入边界。当前 Uno Skia X11 host 未向 AT-SPI bus 暴露稳定语义 provider，Linux Skia smoke 不声明 AutomationId 或控件语义树覆盖；新增 Linux semantic GUI gate 的前提是使用系统原生 AT-SPI provider，而不是截图、X11 属性或应用内 test hook。跨平台一致性由共享 ViewModel/Core 行为测试和平台专属 GUI gate 共同保证。

## 能力边界（跨平台）

平台能力由统一的能力事实源（`IPlatformCapabilityService`）提供，禁止在 ViewModel 或业务层散落平台判断。

| 能力 | Windows | Linux Desktop | macOS Desktop | Android | iOS | WASM |
|------|:-------:|:-------------:|:-------------:|:-------:|:---:|:----:|
| 本地文件系统访问 | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| Stdio 子进程 | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| 安全凭据存储 | DPAPI | Secret Service + plaintext fallback | Keychain + plaintext fallback | AndroidKeyStore | Keychain | Plaintext file |
| WebSocket (`ws://`) | ✅ | ✅ | ✅ | ✅ | ✅ | 仅 `http://` 来源下允许 |
| WebSocket (`wss://`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| ACP `clientCapabilities.fs` | ✅ | ✅ | ✅ | ❌（不声明） | ❌（不声明） | ❌（不声明） |
| ACP `terminal` | ✅ | ✅ | ✅ | ❌（不声明） | ❌（不声明） | ❌（不声明） |

## 配置持久化

详见 `docs/SPEC-CONFIG-PERSISTENCE-YAML.md`。

- **普通配置**：YAML 文件，存储在平台 AppData 目录（Windows: `%LOCALAPPDATA%\SalmonEgg\`，WASM: 浏览器 IDBFS `/local/SalmonEgg`）。
- **敏感信息**（Token / API Key）：通过 `ISecureStorage` 抽象持久化；有 OS-backed secure store 的平台优先使用系统能力，受限平台或 Linux/macOS 系统安全存储不可用时可降级到应用数据目录下的 plaintext secure storage。
- **WASM 持久化**：通过 Uno IDBFS 实现，可持久化 ACP profile YAML、普通应用设置和 plaintext secure storage。
- **配置云同步**：同步核心只依赖 provider-neutral 接口；当前支持 OneDrive（MSAL.NET + Graph）、WebDAV（用户配置远端 ZIP 文件 URL）和 S3-compatible object storage（用户配置 endpoint/bucket/object key），UI 同一时间只能启用一个 provider。同步包包含 `config/` 和已登记的配置相关凭据，`secrets.json` 为明文内容。
- **OneDrive 应用注册配置**：`client_id` / tenant / redirect URI / scopes 只允许在 GitHub Actions 构建阶段注入为 MSBuild 属性并写入应用程序集元数据；运行时不读取用户配置或环境变量。Actions 必须同时支持 repository `secrets` 与 `variables`，并优先使用 `secrets`。

## 传输层

支持三种 ACP 传输方式：

| 传输类型 | 适用平台 | 实现 |
|----------|----------|------|
| `WebSocket` | 全平台 | `src/SalmonEgg.Infrastructure/Network/` |
| `HTTP SSE` | 全平台 | `src/SalmonEgg.Infrastructure/Network/` |
| `Stdio` (含 SSH bridge) | 桌面（MSIX / Desktop） | `src/SalmonEgg.Infrastructure.Desktop/Transport/` |

> `ssh` 不是独立传输类型，SSH bridge 通过 `stdio` transport 的 `stdio_command`/`stdio_arguments` 字段配置。详见 `docs/SPEC-CONFIG-PERSISTENCE-YAML.md`。

## 会话与导航

详见 `docs/hard-constraints-session-navigation-and-search.md`。

会话激活的唯一 owner 是 `INavigationCoordinator -> IConversationSessionSwitcher` 链路。项目/远端目录 ID 的构造、解析与分类由 `ProjectSelectionCwdResolver` 统一提供，ViewModel 和平台服务只传递用户意图并调用该 owner。

## 依赖注入

所有服务在 `SalmonEgg/SalmonEgg/DependencyInjection.cs` 中注册，按平台条件区分桌面/WASM 专用实现。平台专用服务通过接口绑定，业务层只与接口交互。

## 技术选型

| 技术 | 用途 |
|------|------|
| **Uno Platform** | 跨平台 UI 框架（WinUI3 / Skia / WASM） |
| **CommunityToolkit.Mvvm** | MVVM 代码生成（`ObservableProperty` / `RelayCommand`） |
| **System.Text.Json** | JSON 序列化（必须使用源生成上下文） |
| **YamlDotNet** | YAML 配置持久化 |
| **Serilog** | 结构化日志 |
| **System.Net.WebSockets** | WebSocket 传输 |
| **Polly** | 重试 / 断路器策略 |
| **xUnit + FsCheck** | 单元测试 + 属性测试 |

## 参考资料

- [Uno Platform 官方文档](https://platform.uno/docs/)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [ACP 协议标准](https://agentclientprotocol.com/llms.txt)
- 行为硬约束：`docs/hard-constraints-session-navigation-and-search.md`
- 代码规范：`docs/coding-standards.md`
- 构建指南：`BUILD_GUIDE.md`
