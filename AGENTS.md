# SalmonEgg Agent 指南

本文件定义本仓库中 AI/Agent 的工作规则。若与其他文档冲突，以本文为准。

## 1. 目标与原则
1. 保持跨平台一致性，避免平台特定行为泄漏到业务逻辑。
2. 代码可读、可维护、可测试优先于“快速修补”。
3. 优先使用框架默认能力与系统排版，避免像素级 hack。

## 2. 必须遵循的规范
1. 代码规范与强约束：`docs/coding-standards.md`
2. 构建与运行指南：`BUILD_GUIDE.md`
3. 代码审查与提交习惯（如有）：`README.md`
4. 严格遵循 MVVM 开发模式，View 完全由 ViewModel 驱动
5. 会话/导航/搜索行为硬约束：`docs/hard-constraints-session-navigation-and-search.md`
6. 规划模式三原则（构建 / 重构 / 调试前必做）：
    - 构建功能前，先思考其架构；
    - 重构代码前，先明确最终理想状态；
    - 调试修复前，先梳理所有已知问题信息。
7. 最大程度遵守组件原生行为，不得覆写组件原生行为。
8. **严格遵循 ACP 协议标准**：https://agentclientprotocol.com/llms.txt。

## 3. 变更策略
1. 先确认问题边界与复现路径，再修改代码。
2. 修改必须最小化影响面，确保跨平台行为一致。
3. 任何偏离规范的实现必须在代码中注明原因，并在变更说明中记录。
4. 除非用户显式要求，否则 **禁止直接重写整个文件**；如果文件被损坏且改动跨度较小，可以用 git 恢复单文件来重新修复，否则需要询问用户。

## 4. 架构与分层（强约束摘要）
1. Core 层：纯 .NET，不允许引用 UI 类型；必须可被跨平台测试引用。
2. UI 层：只做展示与绑定，不包含业务规则。
3. 平台差异：必须集中在平台服务或 `#if`，禁止散落在业务逻辑中。

## 5. 测试与验证
1. 所有 Core 逻辑必须有单元测试。
2. Core / Presentation.Core 行为测试必须跨平台可运行；平台限定的 GUI smoke、安装包验证、硬件诊断或桥接测试必须隔离在明确的平台测试工程中，并在交付说明中标明平台前提。
3. 若变更涉及代码、可执行资源、XAML、构建脚本或运行行为，必须运行与影响面相符的构建/测试验证。
4. 纯文档改动（仅 `*.md`、不影响编译产物或运行行为）不需要把测试作为门禁，但必须在输出中明确说明“本次为文档-only 变更，未运行测试”。
5. 测试优先验证用户可观察行为、协议/状态契约和跨层数据流；禁止为了覆盖单行实现而长期保留脆弱的字符串扫描、反射探测或实现摆放断言。
6. 仅当测试夹具能防止明确的架构回归或原生行为覆写（例如禁止重新引入已移除的补偿策略、禁止第二套状态 owner）时，才允许保留实现形态类约束测试；否则应依赖行为测试、构建验证或 GUI smoke。

## 6. 日志与诊断
1. 仅保留可长期存在的业务日志。
2. 诊断日志必须移除或放入 `#if DEBUG`。
3. 禁止字符串插值日志，必须使用结构化模板。

## 7. UI 与 XAML 约束（摘要）
1. 绑定默认使用 `x:Bind`；使用 `Binding` 必须注释原因。
2. 优先系统布局控件；禁止像素微调 hack。
3. 禁止使用 Uno 未实现的属性；若必须用 WinUI-only，需平台条件保护。

## 8. 交付与沟通要求
1. 变更完成后，明确列出修改的文件与原因。
2. 如有风险或未验证项，必须显式说明。
3. 不得引入无关格式化或无意义改动。
4. 非文档-only 交付时，必须明确保证编译、测试可通过；纯文档交付必须明确说明未运行测试且原因是“文档-only”。

## 9. Uno / WinUI 跨平台目标（强约束）
1. Windows 平台必须使用 WinUI 3。
2. 非 Windows 平台必须使用对应的原生控件实现（由 Uno 平台映射）。
3. 尽量跨平台复用 UI 与业务代码，避免为单一平台编写重复实现。
4. 若使用 WinUI-only API 或属性，必须 `#if WINDOWS` 保护，并提供其它平台可编译的替代路径。
5. 平台差异实现必须集中到平台服务或 `Platforms/` 下，禁止散落在业务逻辑或 ViewModel 中。

## 10. 如果用户让你 commit，必须**严格采用英文 conventional message** 格式
1. 参考：https://www.conventionalcommits.org/en/v1.0.0-beta.4/
2. 要根据 1 准确分类
3. 每次 commit 前务必保证测试覆盖完善并且无报错，尽量减少警告

## 11. Case Study 规则沉淀（必做）
1. 对于重复出现、跨端不一致、或修复超过 1 天仍反复回归的问题，必须沉淀为 case study。
2. case study 默认写入本节，但必须沉淀为通用经验规则，不得停留在“某次事故经过”或“某个页面特例”。
3. 每条经验规则至少包含：触发条件、原生期望行为、禁止做法、验证方式；规则表述必须可执行、可验证，禁止写抽象口号。
4. 若后续需要展开长文分析，应在稳定的主题文档中沉淀，并在本节保留一条通用规则和链接；禁止重新引入临时审计目录。
5. 当前沉淀的通用经验规则以本节索引为准：
   - 原生控件状态：选择态、焦点态、展开态、可访问性和可交互内联元素继续由原生控件状态模型拥有；应用层只提供数据、意图和官方配置，不用 ViewModel、样式补丁、指针事件或 code-behind 回写视觉状态；验证覆盖键盘、鼠标/触控、焦点恢复、文本选择和辅助功能语义。
   - 单一状态链路：导航、选择、内容切换、加载、错误恢复、搜索和全局入口必须进入同一 authoritative 状态链路；禁止第二套事件、延迟回写、局部缓存或事后纠偏覆盖主状态；验证覆盖快速切换、重复点击、失败恢复、分页外激活和 stale 回调。
   - UI 线程与 latest intent：异步结果、远端事件、后台任务、语音/诊断结果和搜索结果进入 UI 可绑定状态前，必须完成 UI 线程封送并确认仍匹配最新用户意图；验证覆盖并发完成、取消、反序返回和最新意图判定。
   - 远程会话事实源：远程 session 的正文、恢复、warm reuse、连接能力和发现列表必须来自 authoritative runtime / protocol / connection identity；发现接口只提供元数据，未连接条目不得泄露旧正文或提升为可交互运行态；验证覆盖冷/热恢复、连接身份变化、能力缺失、session/load fault 和首次权威加载前 UI 内容边界。
   - 远程冷激活正文边界：当远程会话不满足 warm reuse 的 runtime、remoteSessionId、profileId 与 connectionInstanceId 全量匹配时，selection 投影前必须清空非权威正文/plan/config，activation/hydration overlay 在 `session/load` 或等价权威恢复完成前必须阻塞 header、transcript 与 input；禁止先展示本地 content slice、workspace snapshot、preview 或旧可见 transcript 再用 skeleton 纠偏；验证覆盖左侧导航 background hydration、同会话 cached content slice、connection identity mismatch、hydration replay 期间 visibility 与 warm reuse 不触发 `session/load`。
   - 协议与扩展边界：ACP 标准方法、字段和能力门控严格按官方 schema；自定义扩展必须使用协议允许的命名和 `_meta` / capability contract；禁止 legacy root 字段、未声明能力执行或扩展 payload 冒充标准字段；验证覆盖 schema、capability gating、method-not-found 和 contract round-trip。
   - 协议宽松度不得反向收紧：协议故意留松的地方（可选字段缺省、未知 transport/枚举值、前向兼容分支）client 不得自作聪明地额外收紧为必填、抛错或拒绝。触发条件：schema 未标 required、未标 error-on-unknown 或明确要求 preserve raw payload。原生期望行为：缺省可选字段还原为「未提供」而非报错；未知判别值走 passthrough 并原样 round-trip，由 Agent 而非 client 决定接受或拒绝。禁止做法：把可选当必填抛 `JsonException`、把未知 transport/枚举当非法拒绝、丢弃未知字段。但类型契约不放宽——字段一旦提供却类型错误（如可选数组给了字符串）仍须抛错，不可反向过度容忍。验证覆盖可选字段缺省、未知判别值 round-trip、类型错误仍抛错，以及协商版本下的 wire 形态分流。
   - 缓存与持久化边界：缓存、去重、确认、恢复、安全存储、日志、诊断包和导出只作为优化或平台服务副作用，事实来源必须是协议或平台 authoritative 标识；构造函数、getter、ViewModel 初始化和 DI 注册不得触发真实文件系统副作用；验证覆盖首次写入、unsupported platform、身份不匹配、重复/乱序结果和 stale 恢复拒绝。
   - 平台能力与原生 affordance：本地资源访问、系统 picker、剪贴板、标题栏、窗口控制、指针光标、受限平台配置和能力声明必须由统一平台能力事实源驱动；共享 UI 和业务层不得直接引用平台原生类型或绕过能力边界；验证覆盖支持平台、受限平台和共享层无原生类型泄漏。
   - Shell 与布局事实源：应用标题栏、安全区、页面级面板、右侧 pane、底部面板和主内容尺寸必须由布局 ViewModel / Store 与原生布局控件共同投影；禁止 Storyboard/Timer/code-behind 状态机、手写宽度动画、隐藏 pane hack 或把系统 inset 误当应用 chrome；验证覆盖布局策略、互斥清理、目标 viewport 和目标平台 GUI smoke。
   - Motion 与本地化事实源：应用动画偏好、语言标签、资源目录、平台 override 和打包白名单必须来自单一事实源；不得覆盖原生控件 motion 资源、改写系统全局设置、硬编码语言别名或暴露实现术语；验证覆盖资源目录、持久化 canonical tag、motion scope 文案和禁止覆盖原生 motion key。
   - 运行时语言重载：当 Uno / WinUI 应用允许用户在运行时切换语言时，平台 `PrimaryLanguageOverride`、UI 线程 `.NET` culture 和 ViewModel 本地化投影必须由同一语言服务按顺序更新；已加载的 `x:Uid` 页面按框架原生要求重新导航或重载，持久 singleton 只从 authoritative 状态重新投影文本；禁止在纯 `net*` 程序集中用目标平台编译常量假装应用 override、遍历视觉树逐控件改字、延迟刷新或用旧字符串反推语义状态；验证覆盖当前页面与导航栈重载、singleton 缓存文本、BCP-47 持久化、Desktop/WASM 构建以及目标平台真实安装包 GUI smoke。
   - 输入设备语义：键盘、手柄、遥控器、RawGameController、虚拟输入和诊断注入必须收敛到一条 authoritative 输入语义链；设备服务只采集事实和处理明确 opt-in 缺口，不在 shell 层平行驱动原生控件焦点、激活、选中或值编辑；验证覆盖真实设备、synthetic 差异、一次物理输入只产生一次用户可见行为，以及 selector/value control 不误改值。
   - 可编辑行与语义 ID：配置项、导航项、远端目录、profile、可编辑行和跨层 semantic id 必须由单一 resolver/catalog/row ViewModel owner 构造、解析和承载命令身份；禁止多处复制前缀、未知远端 id 回退本地路径、父 ViewModel 反复注入 stale command 或静默改写用户配置；验证覆盖本地/远端/未知/冲突 id 和新增、删除、再新增交互。
   - 真实构建验证：GUI smoke、安装包验证、发布前回归、WASM 导航和跨平台验证必须使用本次构建实际产出的安装物、二进制、发布包或静态产物；禁止旧安装、旁路产物、开发服务器缓存、隐藏测试入口或来源不明的运行实例替代；验证报告必须记录产物路径、版本/提交来源和启动实例来源。
   - 打包本地化资源：当共享 `.resx` 需要投影为 Uno / WinUI 原生资源时，生成项必须在各目标平台收集资源输入前进入对应资源图，并由平台原生资源加载器解析；禁止只挂接单一平台构建目标、用源码字符串扫描代替真实包检查或在 ResourceMap 缺失时静默回退；验证必须检查本次 MSIX 的 `resources.pri` ResourceMap，并覆盖 Desktop/WASM 构建与 Windows 安装包启动 smoke。
   - 层级菜单 ContextFlyout 所有权：对 `NavigationViewItem` 等层级控件，`ContextFlyout` 只能挂在被右键的语义叶子（例如 project content grid / session item），不得挂在拥有 `MenuItemsSource` / `MenuItemsHost` 的父容器上；禁止用 code-behind 吃掉 `RightTapped`/`ContextRequested`、遍历视觉树补丁菜单，或假设子菜单打开后父菜单一定不会再开；验证覆盖父+子同时有菜单、仅父有菜单、仅子有菜单，以及 Skia Desktop 与 WinUI 同路径右键。
   - 可选中文本叶子的 ContextFlyout 所有权：`ContextRequested` 是冒泡路由事件，任何元素一旦设了 `ContextFlyout` 或本身带内建文本选择菜单（`IsTextSelectionEnabled` 文本控件默认挂 `TextCommandBarFlyout`）就会弹菜单并把事件标 handled、停止冒泡。因此当消息气泡/卡片内部含可选中文本（纯文本 `TextBlock`、markdown 宿主、复合 pill 的 detail/raw 文本）时，`ContextFlyout` 必须挂在真正接住右键的文本叶子上，不得挂在外层 `Border`/容器——否则父容器菜单只会在无文本的边框/padding 空隙触发。触发条件：叶子 `IsTextSelectionEnabled=True` 或叶子自带上下文菜单。原生期望行为：Copy/Report 等应用菜单直接挂在文本叶子（或经我方自有复合控件的 DP 原生转发到其持有的单一宿主控件）；对第三方 markdown 复合控件只赋值给我们创建持有的那一个宿主实例。禁止做法：把菜单留在父 Border 靠冒泡、用 code-behind 吃 `ContextRequested`、遍历渲染出的 run/`DataTemplate` 叶子补丁菜单、或往 data VM 注入命令身份以跨 namescope 取命令。验证覆盖纯文本、markdown（含内部可选中 `RichTextBlock`）、复合 pill 常显面与展开可选中区，以及 Skia Desktop 与 WinUI 同路径右键。
   - Transcript 视口事实源：当消息 transcript 需要在用户脱底、内容流式更新、会话热切回、冷进入或 overlay 暂停/恢复之间保持阅读位置时，follow/detached、per-conversation restore token、scroll request generation 和 restore lifecycle 必须由单一 Core controller 拥有；View 只采集原生 ListView viewport 事实并执行 native scroll/restore 意图；禁止 no-op 兼容 API、View-local overlay shadow flag、容器 realized 状态驱动 loading、或用 projection/list layout tick 推断业务状态；验证覆盖 following 内容追加、pinned 内容更新、warm return、cold enter、overlay 同/异会话恢复、Desktop/WASM 构建和真实 Skia Desktop GUI smoke。
    - 后台恢复完成的权威晋升：当前台激活可被更新意图 supersede、而后台会话恢复（`session/load` / `resume`）在与激活解耦的 request token 上继续跑完时，被 supersede 的完成只要 binding、profile 与 connection instance identity 仍与该 conversation 匹配，就必须落 projection 并把 runtime 晋升为 authoritative Warm（`SessionLoadCompleted` / `SessionResumeCompleted`），使回切成为零往返 warm reuse 而非再次慢恢复；晋升工作必须按 conversationId 隔离，禁止触碰前台（`HydratedConversationId` / overlay / 选中态由最新激活拥有）。唯一例外：若**同一** conversation 已有更新的在途激活（runtime 已被重置为 `Selecting` / `Selected` / `RemoteConnectionReady` 等更早 pending 阶段），旧的后台完成不得晋升 Warm 或落正文，须让更新激活驱动自己的权威恢复。禁止做法：superseded 完成分支只清 buffering 而不晋升（会让掠过的会话永远停在 `RemoteHydrating`，每次回切都 `RuntimeStateNotWarm` 重跑慢恢复）；也禁止无条件晋升而忽略同会话更新激活。验证覆盖：不同会话 supersede 后旧会话晋升 Warm 且前台不被夺、同会话更新激活在途时旧完成不晋升且新激活重跑恢复、binding/connection identity 不匹配时不晋升。
   - 测试缝隙与风险归属：在判定一个逻辑类型“无法被干净测试”或“不值得测”之前，必须先排查既有测试缝隙（projector / factory / `.Empty` / 带默认值的 record / adapter / 测试工程已有 mock 模式），不得仅凭“构造参数多”“看起来是框架胶水”就下结论并推迟覆盖；测试对象按风险归属划分，框架与运行时保证的能力（如 `.WaitAsync` 计时、record 结构 `Equals` 等语言契约）不重复测，应用层接线、封装、诊断文案、null 缝、适配层、状态机顺序与门控才是测试对象，断言须复用被测类的契约常量（如 policy 常量）而非魔法数，且不重复其下层单元测试已覆盖的功劳；耦合若是有意设计（如全字段结构相等以避免“字段变了也抑制”的行为退化），测试须基于真实实例构造而非缩小相等面或改生产契约；验证覆盖构造路径确认（`.Empty` / 默认值 / mock seam 是否能造出真实实例）、状态机各分支与门控、null/空边界、诊断渲染与内层异常保留，以及新测试用例数与实际执行数一致。
   - 应用启动副作用所有权：当 singleton 运行态需要加载配置、恢复 workspace、重建导航树或自动连接时，必须由 application-scoped startup workflow 统一触发并共享在途任务；Page / Window 的 `Loaded` 只附着原生视图、焦点、viewport 与事件，不得直接加载 profile、恢复 conversation、另起 fire-and-forget 初始化或成为失败重试 owner。原生期望行为是 shell 首帧先完成挂载，运行态初始化随后由同一 owner 推进；失败只重试失败的子任务，不重复已完成恢复。验证覆盖冷启动、shell reload、Mini Window 首开、多个页面并发挂载、空配置目录、初始化失败重试与每个副作用调用次数。
   - 回收型容器的 per-container 状态与集合抖动：当控件把选中/焦点/展开等状态存在**被回收复用的容器**上（`ItemsRepeater` 家族，含 `NavigationView`、`ListView`、`ItemsView` 的虚拟化宿主）时，应用层在该状态存活期间不得搬动已渲染行。触发条件：绑定集合会因 recency/活跃度等易变排序键在用户交互或加载风暴期间重排。原生期望行为：把「已渲染行的位置」当作控件借出的资源——插入/删除安全（宿主只对存活元素重编索引，仅回收被删数据自身的容器），搬动不安全（`Move` 被 `ItemsRepeater` 拆成 Remove+Add，容器进回收池且池不复位 per-container 状态，父控件「取消上一个选中」在容器已非 realized 时会静默 no-op），故未静默期只保持既有顺序并追加新行，静默后再收敛到目标顺序。禁止做法：用 code-behind 遍历 realized 容器强清 `IsSelected` 等控件自有视觉状态（与控件自身对账竞态，且若真凶是 pointer 态则完全无效）；仅改集合事件形状（`Remove`+`Insert` 换成 `Move`）就认为保住了状态——WinUI 文档只承诺 `INotifyCollectionChanged` 能投递真 `Move`，不承诺宿主不拆解它；为保住某一行而重排其余所有行（总变更次数才是主因子，附带 churn 会把一个脏容器扩散成多行残留）。上游已修但未进当前依赖线时，必须在代码注释与提交信息中记录 issue/PR 号与「上游落地后删除本缓解」的条件。验证覆盖：快速连续切换叠加高频集合重排、加载期与静默期两种路径、收敛不饿死，以及在**真实控件树**上枚举全部 realized 容器断言该状态数量不超过控件契约上限。
   - 视觉树级缺陷的运行时门禁与反向验证：当缺陷只在真实控件树上可观测（容器回收、视觉状态、焦点/选中投影、布局回流）时，单元测试不构成安全网，必须建立运行时门禁：由**视图层**只读采集原生事实（枚举 realized 容器及其状态）并输出可断言标记，由**独立诊断组件**自持依赖、按环境变量自我门控地施加负载，门禁脚本对不变式做硬断言而非仅打印数值。禁止做法：让页面 code-behind 直接驱动下层服务制造负载（分层异味）；门禁只 `echo` 指标由人肉判读；断言 mock 调用或集合事件形状而非用户可观测的原生状态（会给出假信心）；把不可靠的观测字段（跨行同名模板部件、模板作用域视觉状态组）当证据。新增或修改此类门禁后必须做**反向验证**：临时移除修复并确认门禁失败（记录失败输出），否则不得认为门禁有效。验证覆盖：修复在位时多轮稳定通过、修复移除时必然失败、采样数下限校验以防空跑绿。
