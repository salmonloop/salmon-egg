# 会话/导航/搜索硬约束（锁定版）

本文件是“行为级硬约束”。目标：锁死会话切换、导航选择、全局搜索的架构与运行语义，禁止后续回归为“修修补补”。

## 1. SSOT 与所有权（必须）
1. 会话激活唯一 owner 必须是 `INavigationCoordinator -> IConversationSessionSwitcher` 链路。
2. View / Adapter 只能表达“用户意图”，不得直接成为会话真实状态来源。
3. 任何“预览态/加载态”必须是投影态，不能替代 SSOT。

## 2. 最新意图语义（Latest-Intent）（必须）
1. 用户最后一次点击的目标会话是唯一有效意图。
2. 旧请求取消/失败不得把 UI 选择回滚到更早会话。
3. 仅当“最新目标会话已无效/不存在”时允许回滚到安全兜底（如 Start）。

## 3. 会话切换状态机（必须）
1. 必须显式分阶段并可落日志：`Selecting -> Selected -> RemoteConnectionReady -> Hydrated | Faulted`。
2. 阶段推进必须是单向，禁止 UI 线程等待远程水合完成。
3. 任何阶段失败必须可诊断（结构化日志，包含 `ConversationId`、`ActivationVersion`、`Reason`）。

## 4. 导航与内容同步（必须）
1. 左侧导航选中态与主内容区必须通过 Coordinator 同步，不允许双写。
2. NavigationView 的视觉状态变化不得反向改写业务状态。
3. 导航失败后的恢复逻辑不得违反 latest-intent 规则。

## 5. 全局搜索状态机（必须）
1. 搜索状态必须显式：`Idle / Loading / Results / Empty / Error`。
2. 必须实现 latest-wins：旧查询结果或旧异常不得覆盖新查询状态。
3. 进入 `Loading` 时必须立即给出可视反馈（pill 或等价反馈）。
4. 搜索计算必须在可取消异步链路中运行，禁止阻塞 UI 线程。

## 6. ACP 协议一致性（必须）
1. 协议行为以官方规范为准，禁止凭记忆新增“隐式协议”。
2. 能力门控必须严格遵守：未声明能力不得调用对应方法（例如 `session/load`）。
3. 对可选字段必须做“存在即解析，不存在不伪造”。
4. 协议相关改动必须在 PR/交付说明中标注“依据的协议条目”。

## 7. 远程 Session 缓存与切换契约（必须）
1. 远程 ACP Server 是远程 session transcript、tool payload 与消息顺序的唯一 SSOT；客户端不得把远程 transcript 持久化为跨进程恢复真源。
2. 必须区分两类远程会话：
   - `Background Warm`：同一进程内仍持有可复用连接实例与热运行态的远程会话。
   - `Cold / Discover-Only Remote`：当前仅有 discovery 元数据、没有可复用连接实例或热运行态的远程会话。
3. `Background Warm` 切回前台时必须复用当前运行期的 authoritative runtime；若 `conversation binding` 与 `ConnectionInstanceId` 仍匹配，则禁止再次触发 `session/load`、禁止重新进入阻塞式慢加载。
4. `Cold / Discover-Only Remote` 只能保留 `remoteSessionId`、`title`、`updatedAt`、`meta`、profile binding 等发现性元数据；消息内容缓存数量必须为 `0`。
5. `session/list` / discover 属于 metadata-only 输入；它们可以刷新 title、updatedAt、meta 等发现性字段，但不得生成 transcript preview、不得回写 warm transcript、不得作为 warm/cold 分流之外的正文来源。
6. skeleton / loading overlay 出现之前，禁止泄露任何 stale transcript、cached transcript 或旧 header；若当前会话不是 `Background Warm` 可直接复用态，则正文必须等待 authoritative hydration。
7. UI 虚拟化、增量投影和按需加载属于渲染层优化，不得反向定义 session 事实；是否 warm、是否需要 `session/load`、是否可显示正文，必须由 authoritative session state 决定。
8. 若历史设计文档或旧实现把“本地持久化 transcript”或“discover transcript preview”当作远程真源，该约束自本文件起一律作废，以本节为准。

### 7.1 Warm Reuse 判定逻辑（必须）
1. `warm reuse` 的唯一含义是：远程会话切回时不调用 `session/load`，直接复用同一进程内仍有效的运行态与投影。它不是“同一个 profile 就可复用”，也不是“本地有缓存就可复用”。
2. 允许 `warm reuse` 必须同时满足全部条件：
   - `conversation binding` 存在且 `remoteSessionId` 非空；
   - runtime state 属于同一 conversation，`Phase == Warm`；
   - runtime reason 必须来自集中定义的 authoritative warm reason：`SessionLoadCompleted`、`SessionResumeCompleted`、`WarmReuse`、`WarmReuseAfterProfileReconnect`、`MarkedHydrated`；
   - runtime 的 `RemoteSessionId` 与 `ProfileId` 必须分别等于当前 binding 的 `remoteSessionId` 与 `profileId`；
   - 当前连接身份必须来自目标 profile 的 authoritative foreground connection，且 `current.ProfileId == binding.ProfileId`；
   - `current.ConnectionInstanceId` 必须非空，且等于 runtime 的 `ConnectionInstanceId`；
   - 必须存在可复用 projection：authoritative `RuntimeProjection`，或当前 store 中同一 conversation 的已投影正文 / session state；discover/list 元数据、restored cache、旧 header 不能单独构成可复用 projection。
3. 任一条件不满足都必须拒绝 `warm reuse`，进入协议恢复 / authoritative hydration 路径；不得因为“用户刚刚看过这个会话”“同 profile 已连接”“本地 snapshot 有内容”而绕过 `session/load` 能力门控。
4. 拒绝原因是行为契约，新增或修改条件必须同步维护测试。当前拒绝原因必须覆盖：`RuntimeStateNotWarm`、`WarmRuntimeNotAuthoritative`、`MissingBinding`、`ProjectionNotReady`、`ConnectionProfileMismatch`、`ConnectionInstanceIdMismatch`、`RemoteSessionIdMismatch`、`ProfileIdMismatch`。
5. `ConnectionInstanceId` 是运行期连接实例身份，不是 profile 身份。ACP session 运行在具体活连接上；同 profile 重新连接后，旧 remote session 只能走协议恢复，不能零往返 warm reuse，除非新的 authoritative runtime 明确重新标记为 warm。
6. `warm reuse` 的连接身份解析与“缓存投影恢复”的连接比较必须分离：
   - warm 判定必须使用 profile-aware authoritative identity，防止非目标 profile 的前台连接参与目标会话判断；
   - 缓存投影恢复只能用当前 raw connection id 与 snapshot 的 `ConnectionInstanceId` 比较，不能把 warm 身份解析失败折叠成 `null` 传入 snapshot policy；`null` 在 snapshot policy 中表示“未知当前连接”，不能表示“profile mismatch”。
7. 快速切换和 gate 排队场景必须多点重查同一判定：入口 current-session 快路径、selection 完成后、hydration/gate 内部短路都必须调用同一套 canonical warm decision。不得用 in-flight activation 的存在直接否定目标已经 `Warm` 且满足上述条件的会话。
8. 相关测试必须至少覆盖：
   - 同连接热切回不触发 `session/load`；
   - connection instance 变化后必须 reload；
   - profile mismatch 拒绝 warm reuse；
   - projection missing 拒绝 warm reuse；
   - profile reconnect warm reason 可复用；
   - snapshot restore 连接比较不被 profile-aware warm resolver 的 `null` 结果污染；
   - 真实用户配置 GUI smoke 中已 hydrate 的远程会话跨 profile 热切回不显示 blocking loading overlay。

## 8. 测试与验收门禁（必须）
1. 必须覆盖结果导向测试，不测试实现细节字符串。
2. 至少包含：
   - latest-intent 不回滚回归测试；
   - stale success/error 不覆盖最新查询的搜索测试；
   - 会话切换 UI 响应性 smoke（远程首进 + 快速切换）。
3. 若变更涉及代码、可执行资源、XAML、构建脚本或运行行为，合并前必须通过：
   - `dotnet build`（Core / Desktop / Wasm 验证）；
   - Windows 原生包使用 `build.bat msix` 或等价的 `.tools/run-winui3-msix.ps1 -SkipInstall`，禁止把 `dotnet build -f net10.0-windows10.0.26100.0` 当作唯一门禁；
   - 目标测试集；
   - GUI smoke。
4. 纯文档改动（仅 `*.md`、不影响编译产物或运行行为）不要求执行上述构建/测试/GUI smoke，但交付说明必须显式标注“文档-only”。

## 9. 禁止事项（必须）
1. 禁止在 View code-behind 写业务状态机。
2. 禁止同步阻塞（`.Result/.Wait`）进入切换/搜索主链路。
3. 禁止“失败即强制回滚到旧会话”的默认策略。
4. 禁止新增未落测试的关键并发/状态逻辑。
