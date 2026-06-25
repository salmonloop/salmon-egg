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
    ├── SalmonEgg.IntegrationTests/
    ├── SalmonEgg.GamepadBridge.Windows/    # 手柄输入诊断（Windows）
    └── SalmonEgg.GuiTests.Windows/         # GUI smoke（Windows FlaUI）
```

## 能力边界（跨平台）

平台能力由统一的能力事实源（`IPlatformCapabilityService`）提供，禁止在 ViewModel 或业务层散落平台判断。

| 能力 | MSIX (Windows) | WASM | Desktop (Skia) |
|------|:--------------:|:----:|:--------------:|
| 本地文件系统访问 | ✅ | ❌ | ✅ |
| Stdio 子进程 | ✅ | ❌ | ✅ |
| 安全凭据存储 | Windows Credential Manager | Volatile（内存） | Keychain / Secret Service |
| WebSocket (`ws://`) | ✅ | 仅 `http://` 来源下允许 | ✅ |
| WebSocket (`wss://`) | ✅ | ✅ | ✅ |
| ACP `clientCapabilities.fs` | ✅ | ❌（不声明） | ✅ |
| ACP `terminal` | ✅ | ❌（不声明） | ✅ |

## 配置持久化

详见 `docs/SPEC-CONFIG-PERSISTENCE-YAML.md`。

- **非敏感配置**：YAML 文件，存储在平台 AppData 目录（Windows: `%LOCALAPPDATA%\SalmonEgg\`，WASM: 浏览器 IDBFS `/local/SalmonEgg`）。
- **敏感信息**（Token / API Key）：仅通过平台安全存储（`ISecureStorage`）持久化，永不落盘到 YAML。
- **WASM 持久化**：通过 Uno IDBFS 实现，可持久化 ACP profile YAML 和普通应用设置；安全凭据使用 volatile 存储（内存）。

## 传输层

支持三种 ACP 传输方式：

| 传输类型 | 适用平台 | 实现 |
|----------|----------|------|
| `WebSocket` | 全平台 | `src/SalmonEgg.Infrastructure/Network/` |
| `HTTP SSE` | 全平台 | `src/SalmonEgg.Infrastructure/Network/` |
| `Stdio` (含 SSH bridge) | 桌面（MSIX / Desktop） | `src/SalmonEgg.Infrastructure.Desktop/Transport/` |

> `ssh` 不是独立传输类型，SSH bridge 通过 `stdio` transport 的 `stdio_command`/`stdio_args` 字段配置。详见 `docs/SPEC-CONFIG-PERSISTENCE-YAML.md`。

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
