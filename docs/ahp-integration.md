# AHP 接入边界与宿主决策

本文件完成 [#197](https://github.com/salmonloop/salmon-egg/issues/197) 的调研和两项待定决策。未来的 AHP host 由 CLI 显式启动，默认只监听本机；跨设备连接使用 TLS，并在传输握手中校验设备 token。Agent 的 OAuth 授权另按 AHP 的 `protectedResources` / `authenticate` 处理。

这是已选定的架构方向。仓库尚未实现 AHP host、client 或 `salmon-egg serve` 命令，本文件不声明运行互操作、完整宿主或生产发布已经通过。

## 核实范围

调研日期：2026-09-12。AHP 官方源码固定为 [`d1a2b199cdb493e914165322a925c1a5f0113616`](https://github.com/microsoft/agent-host-protocol/tree/d1a2b199cdb493e914165322a925c1a5f0113616)，VS Code 源码固定为 [`a8f49160195d9e967d2d51e8544dc895207518e7`](https://github.com/microsoft/vscode/tree/a8f49160195d9e967d2d51e8544dc895207518e7)。AHP [变更记录](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/CHANGELOG.md) 的最新已发布协议条目为 `0.9.0`；源码 `main` 还含未发布工作，1.0 前 minor 更新允许破坏性变更。实现时必须固定协议版本和 SDK 版本，分别核对，不能根据包版本推断 wire 版本。

| 旧调研表述 | 当前可证实的边界 |
|---|---|
| “全球只有一个 host”“没有参考实现” | [官方实现目录](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/guide/implementations.md) 当前列出 VS Code reference server；这证明有参考源码，不证明其他实现不存在。 |
| “VS Code host 无法独立启动” | VS Code 已提供独立 Node 入口、端口参数和连接 token；详见下节。发布包是否携带这些构建产物、具体版本能否运行，仍需实测。 |
| “AHP OAuth 是设备互信的替代方案” | [传输规范](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/specification/transport.md#authentication) 将 endpoint access 放在传输握手；[协议授权](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/specification/authentication.md) 面向 Agent/MCP 等受保护资源，两者独立。 |
| “手机连接桌面 Agent 需要 AHP” | 单客户端远程连接已有 ACP WebSocket；AHP 增加的是多个客户端共享状态、动作排序与断线恢复，见 [AHP 与 ACP 分层](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/guide/ahp-and-acp.md)。 |
| “安装 GUI 就已有 AHP 宿主” | 桌面安装包已包含配置 CLI，并按平台注册命令；例如 Windows MSIX 的 [AppExecutionAlias](../SalmonEgg/SalmonEgg/Package.appxmanifest) 指向 `cli/salmon-egg.exe`。这不代表尚未实现的 AHP `serve` 命令已经存在。 |

官方 [.NET 工程](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/clients/dotnet/src/AgentHostProtocol/AgentHostProtocol.csproj) 是 client、reducer 和 WebSocket transport，目标为 `netstandard2.0;net8.0`，包含 `Microsoft.Extensions.*` 包依赖。它可作为 AHP 客户端起点，不能当成已经交付的 .NET server。

## VS Code 独立入口

[`agentHostServerMain.ts`](https://github.com/microsoft/vscode/blob/a8f49160195d9e967d2d51e8544dc895207518e7/src/vs/platform/agentHost/node/agentHostServerMain.ts) 文件头直接给出开发启动命令：

```text
node out/vs/platform/agentHost/node/agentHostServerMain.js --port <port> --host <host> --connection-token-file <path>
```

这是 VS Code 已构建源码的入口说明，不是 SalmonEgg 命令或本轮运行结果。该入口会创建 `WebSocketProtocolServer` 和 `ProtocolServerHandler`，输出 `READY:<port>`；支持显式 token 文件，未指定时生成随机 token。[WebSocket server](https://github.com/microsoft/vscode/blob/a8f49160195d9e967d2d51e8544dc895207518e7/src/vs/platform/agentHost/node/webSocketTransport.ts) 默认绑定 `127.0.0.1`，在 upgrade 前验证 query token，错误 token 返回 HTTP 403。

因此对接外部 host 的下一步可以是实际互操作探针，不再以“找不到启动入口”为阻断。探针须记录 VS Code 提交、构建产物、启动 PID、实际端口、协商版本和退出结果。只能从源码确认入口存在；本轮没有构建或启动 VS Code，也没有证实任一 VS Code 安装包中的 host 能直接运行。

## 宿主与模块边界

选择 CLI 显式启动。长期服务的启动、停止和日志归 CLI 进程管理，避免 GUI 页面关闭、重载或恢复时另起一套 host。沿用现有 CLI 交付链；`serve` 只是未来入口名称，具体参数在实现期定义。

AHP 运行时和第三方包放进独立工程，由 CLI 组装。AHP 客户端也经独立适配层投影到 UI，不能成为 `SalmonEgg.Acp` 的第四种 transport 或新包依赖；[ACP SDK 的零包依赖契约](../src/SalmonEgg.Acp/README.md)继续成立。Application、Domain 和 ViewModel 不直接引用平台宿主。

| 候选宿主 | 选择及代价 |
|---|---|
| CLI 显式服务 | 采用；复用桌面安装包中的 CLI 交付链，并有独立进程生命周期。移动端和网页作为客户端，不启动本地宿主。 |
| GUI 内后台服务 | 暂不采用；服务生命周期会与窗口关闭、导航及应用恢复耦合。GUI 后续可管理独立 CLI 服务。 |
| 另发一个独立可执行程序 | 暂不采用；会增加安装、升级与进程管理入口。独立协议工程并不要求再造发布入口。 |

## 多设备互信

下面是 SalmonEgg 的产品选择；AHP 只规定传输需要可靠、有序、双向并交付完整消息，不强制 WebSocket、TLS 或某种 endpoint 认证方案。

1. 首个宿主默认只监听 loopback，仍要求随机连接 token；显式启用远程监听后才接受其他设备。
2. 跨设备使用 `wss://` 和可验证的 TLS 身份，token 在 WebSocket upgrade 时检查，未通过时不能进入 AHP `initialize`。本机明文限 loopback；局域网不作为免认证边界。
3. 每台设备使用独立、可撤销的高熵 token；凭据经安全存储持久化，不进入 workspace、同步 state tree、日志或普通错误文案。撤销或轮换后，旧连接和重连都不能继续使用原权限。
4. 原生客户端可用握手 header。浏览器不能任意添加 header，采用同源 HTTPS 引导后设置 Secure/HttpOnly cookie，并校验 Origin；若部署只能提供 query token，使用短寿命、单次握手票据，不能把长期设备凭据放进 URL。具体端点属于部署契约，不扩充 AHP 标准字段。
5. Agent 需要 OAuth 时，客户端按 host 广告的资源取得 token，再调用 AHP `authenticate`；token 的 `resource` 必须匹配已广告的资源。设备连接 token 不冒充 Agent token，也不以一个客户端的 Agent 授权自动授权所有设备。

设备鉴权和 Agent 授权分层符合 [transport authentication](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/specification/transport.md#authentication) 与 [per-resource token delivery](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/specification/authentication.md#token-delivery)。OAuth `expiresIn` 的有效期、空 token 撤销、`-32007` 的资源数据和 `auth/required` 后重新授权须按该规范实现，不能盲目重放已过期 token。

## 状态事实源

AHP host 拥有 AHP channel state、排序、订阅及恢复；客户端只维护 confirmed state 和待确认意图，按 host 顺序 reconcile，遵守 [Doctrine](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/guide/doctrine.md) 和 [Reconciliation](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/guide/reconciliation.md)。一个 AHP 会话不能同时接入现有远程 ACP conversation owner，导致两个状态源互相覆盖。

Host 下游可复用 ACP client 与协议数据类型，但 AHP action 由专门 mapper 产生。ACP Agent 仍是下游会话事实源；不能把旧 issue 中“ACP 会话我们权威”的宽泛描述用于绕过 [远程 session 硬约束](hard-constraints-session-navigation-and-search.md#7-远程-session-缓存与切换契约必须)。远程终端由 host 能力承载，不能因此把客户端本地文件系统、stdio 或终端能力标为支持。

## 实施顺序与验收

| 顺序 | 独立交付 | 放行条件 |
|---|---|---|
| 1 | 对固定 VS Code host 做最小 AHP client 互操作 | 真实产物通过握手、版本协商、root/session 订阅；错误设备 token 在 initialize 前拒绝；确认包/API 版本和运行方式。 |
| 2 | CLI host 最小闭环：root/session、默认 chat、一个 ACP backend | 两个真实客户端观察同一 turn，重复 prompt/工具授权只执行一次；host 分配单调 `serverSeq`；断连后按 [reconnect](https://github.com/microsoft/agent-host-protocol/blob/d1a2b199cdb493e914165322a925c1a5f0113616/docs/specification/lifecycle.md#reconnection) 覆盖 replay 和 snapshot 两条恢复路径。 |
| 3 | 同一用户的多设备产品入口 | token 吊销、Origin 与 TLS 检查、Agent 授权隔离、后台退出与重连均在目标平台真实产物验收；不丢 pending 意图、不重复执行操作。 |
| 4 | 逐项增加分页、分叉/侧聊、MCP 状态、远程终端和遥测 | 每项跟随实际 host/backend 能力门控和独立验收；不能把 AHP 的 UI 形状直接替换 ACP wire 契约。 |

先做 ACP 已有功能的稳定交付，再按上表引入 AHP。第一步的外部 client 探针比直接自建完整 host 更小，且能验证版本和语义；不以“首个独立 host”或固定周数估算代替证据。多 host 路由、双向桥和远程终端不进入最小 host 的初始范围。

研究单的完成条件是：收益与难度排序有一手依据，宿主和设备互信已决策，旧事实已纠正，后续实现有验收条件。本文件满足这一范围；表中的软件交付仍须后续实现与运行验证，不能因研究单关闭就标成产品能力。
