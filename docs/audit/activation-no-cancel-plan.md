# 开发方案：去掉取消旧激活、改为纯版本号守卫

## 目标产品语义

用户依次点击 A → B → C：
- A、B、C 的后台工作（远端连接、加载历史、缓存数据）全部继续执行到完成
- 屏幕焦点永远跟随最后一次点击（C）
- A、B 完成后，数据落入本地 workspace snapshot（允许），但不改：
  - HydratedConversationId（store 的"当前显示会话"）
  - LastActiveConversationId（workspace 的"上次活跃"）
  - SelectedProfileIntentId（连接 profile）
  - 导航焦点 / overlay 状态
- C 完成后，因为它仍是最新意图，正常提交所有屏幕状态

## 当前架构（要改的部分）

`ConversationActivationOrchestrator.BeginActivation()`:
1. 递增 `_activationVersion`
2. 创建新 CTS（linked to caller token）
3. **取消旧 CTS** ← 这是要去掉的
4. 返回 context（含 version + CTS）

`TryCompleteActivationCore()`:
- 检查 context 是否仍是 `_currentActivationCts` && version 匹配
- 只有匹配时才调用 `OnActivationCompletedAsync`

## 改动步骤

### Step 1: Orchestrator 去掉 previousCts.Cancel()

- `BeginActivation()` 不再 `previousCts?.Cancel()`
- 旧 context 的 CTS 不被取消，后台工作继续跑
- 保留 version 递增和 `_currentActivationCts` 替换逻辑
- `TryCompleteActivationCore` 不变——旧激活完成后发现自己不是 current，静默结束

**问题**：旧 CTS 不被取消也不被 dispose → 内存泄漏
**解决**：旧 context 完成（无论成功/失败）时在 `FinalizeActivationAsync` 里 dispose 自己的 CTS，
即使 `completedCurrentActivation == false` 也要 dispose。

### Step 2: Sink 侧的"版本守卫"替代"取消守卫"

当前 `ExecuteActivationAsync` 里大量 `cancellationToken.ThrowIfCancellationRequested()` 
和 `IsActivationContextStale(version, ct)` 检查。

去掉取消后，cancellationToken 只响应 caller 传入的取消（比如 dispose）。
版本守卫逻辑不变：每次要写屏幕状态前检查 `IsLatestActivationVersion(context.ActivationVersion)`。

**关键区分**：
- "写屏幕状态"操作（SelectConversationAction, HydrateConversationAction, 
  SetIsHydratingAction, CommitActivatedConversationStateAsync, overlay 状态等）
  → 版本守卫，过期则跳过
- "写本地缓存/workspace snapshot"操作 → 不受版本限制，继续执行

### Step 3: ConversationActivationCoordinator 调整

当前 coordinator 在每个 dispatch 前后都有 `cancellationToken.ThrowIfCancellationRequested()`。
去掉取消后这些不再被触发（除非 caller dispose）。

需要改为：coordinator 接收一个"是否仍是最新意图"的判定回调或版本号，
在写屏幕状态前检查。如果过期：
- 不写 SelectConversationAction / HydrateConversationAction
- 但仍然可以把 snapshot 数据更新到 workspace

**或者更简单的方案**：coordinator 不管版本，它只做"准备数据"；
屏幕状态的写入全部上移到 ViewModel 层（当前 commit 已经在 VM 了），
coordinator 的 SelectConversation / Hydrate 也上移。

→ 这个改动面太大。保持 coordinator 写 store，但让 orchestrator 在调用 coordinator 前检查版本。
如果过期，不调 coordinator 的屏幕写入部分，只调数据缓存部分。

### Step 4: 远端连接/加载生命周期

当前 `CompleteConversationRemoteActivationAsync` 用 activation version 判断 stale。
去掉取消后，远端加载会跑完。完成后：
- 检查版本 → 如果过期，数据写 workspace snapshot，不写屏幕
- 如果仍是最新 → 正常投影到屏幕

### Step 5: Foreground Gate 语义调整

当前 `_foregroundGate` 串行化激活。去掉取消后，旧激活不被打断，
新激活要等旧的 release gate 后才能进入前台。

这意味着 B 要等 A 的前台阶段完成后才能开始自己的前台阶段。
C 要等 B 完成。这是正确的——屏幕状态写入是串行的。

但后台工作（远端连接/加载）不需要等 gate，它们可以并行。

**当前问题**：`ExecuteActivationAsync` 整体在 gate 内部。
需要拆为：
- 前台阶段（store 投影、overlay 更新）在 gate 内
- 后台阶段（远端连接、加载）在 gate 外并行

这个拆分比较大，可能需要分步实施。

### Step 6: 测试

1. A→B→C 场景：A、B 的远端数据落入 workspace，屏幕始终是 C
2. A 完成后不改 HydratedConversationId
3. A 完成后不改 LastActiveConversationId
4. A 完成后不改 SelectedProfileIntentId
5. C 完成后正常提交所有状态
6. 现有 1940 测试兼容

## 风险评估

- **影响面大**：orchestrator、coordinator、VM activation 链路、远端生命周期
- **并发复杂度增加**：多个激活并行跑，共享 workspace/store 需要更细粒度的并发控制
- **资源消耗**：多个远端连接同时保持，服务器压力增大
- **回退路径**：如果远端连接资源有限，需要限流而非无限并行

## 建议分阶段

Phase 1: Orchestrator 去掉 cancel + 版本守卫替代（本次）
Phase 2: 前台/后台阶段拆分（需要更大重构）
Phase 3: 资源限流（连接池/并发上限）

## 方案自审修正（关键问题）

### 问题 1: Foreground Gate 串行等待

如果 A 占着 gate，B 和 C 要排队等 A release。
用户点了 C 但屏幕卡在 A 的前台阶段——不可接受。

**修正**：旧激活到 WaitForForegroundGateAsync 时，先检查版本：
- 如果已经不是最新 -> 不等 gate，直接走"后台只写缓存"路径
- 如果仍是最新 -> 正常等 gate 进入前台

这样只有最新意图会真正占 gate，旧的绕过。

### 问题 2: Store 状态闪烁

如果 A、B、C 都串行走前台，用户看到 A->B->C 的切换闪烁。

**修正**：结合问题 1 的方案，只有最新意图走前台，不存在闪烁。

### 问题 3: 旧激活的后台工作如何"只写缓存"

旧激活跑完远端加载后，需要一个"仅更新 workspace snapshot、不动 store/屏幕"的路径。

**修正**：在 sink 的 ExecuteActivationAsync 里，每个"写屏幕"操作前都用
IsLatestActivationVersion(context.ActivationVersion) 守卫。
过期时跳过 store dispatch，但继续执行 workspace snapshot 更新。

### 根本矛盾与解法

**矛盾点**：前台 gate 的设计目的是防止并发写 store。
但如果"远端加载"也在 gate 内，新激活就要等旧的远端加载完。

**真正的解法**：把"远端加载"移出 gate 外。

Gate 内（快速，<100ms）：
  - SelectConversationAction
  - HydrateConversationAction（本地 snapshot）
  - CommitActivatedConversationStateAsync
  - Overlay/loading 状态

Gate 外（可能很慢）：
  - 远端连接
  - 远端 loadSession
  - 远端回放

Gate 再次进入（回放完成后）：
  - 版本检查 -> 如果仍是最新 -> 投影远端数据到屏幕
  - 如果过期 -> 只写 workspace snapshot

### 结论：需要同时做 Phase 1 + Phase 2

不能只去掉 cancel 不拆分 gate，否则用户体验反而更差（等待时间变长）。

两个阶段必须一起做：
1. 去掉 cancel
2. 拆分 gate（快速前台 + 慢速后台）
3. 后台完成后版本守卫决定是否投影
