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
2. 测试工程必须跨平台可运行。
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
4. 若后续需要展开长文分析，可在 `docs/audit/` 建立专题文档，并在本节保留一条通用规则加链接，不在本节堆叠事故叙事。
5. 当前沉淀的通用经验规则：
   - 触发条件：新增或修改控件选择态、焦点态、展开态、容器语义或可访问性投影；原生期望行为：这些状态继续由原生控件的状态模型产生，应用层只提供数据和用户意图；禁止做法：在 ViewModel、样式补丁或代码后置中反向回写原生视觉状态，或用模板替换掩盖状态来源冲突；验证方式：覆盖键盘、鼠标/触控、焦点恢复和辅助功能语义的行为测试或 GUI smoke。
   - 触发条件：新增或修改导航、选择、内容切换、加载阶段或错误恢复链路；原生期望行为：一次用户意图只能进入一条 authoritative 状态变更链路，选中态、内容区和加载状态必须同源；禁止做法：用第二套事件、延迟回写、事后纠偏或局部缓存补偿主状态链路；验证方式：覆盖快速切换、重复点击、失败恢复和过期回调的顺序测试。
   - 触发条件：异步结果、远端事件、后台任务或搜索结果进入 UI 可绑定状态；原生期望行为：结果必须先完成 UI 线程封送，并验证仍匹配最新用户意图后再投影；禁止做法：后台线程直接触发可视状态变更，或允许 stale 回调覆盖当前状态；验证方式：覆盖并发完成、取消、反序返回和最新意图判定的行为测试。
   - 触发条件：远程会话切换、连接恢复、后台提醒、前台清理或运行态复用；原生期望行为：所有决策必须以统一的连接身份、会话身份和当前意图为准；禁止做法：把普通投影变化误判为连接变化，或用额外刷新制造提醒、恢复或一致性假象；验证方式：覆盖连接身份变化、会话身份变化、同身份热切换和清理时机的矩阵测试。
   - 触发条件：新增或修改发现/列表接口、详情加载接口、会话恢复接口或运行上下文投影；原生期望行为：发现性接口只提供元数据，交互性接口才提供正文、上下文和可执行状态；禁止做法：用目录/列表结果回写已建立的执行上下文，或把发现性数据冒充正文真源；验证方式：覆盖仅发现态、详情加载、恢复失败和已连接会话不被发现结果覆盖的行为测试。
   - 触发条件：新增或修改客户端缓存、去重、确认、恢复或离线投影逻辑；原生期望行为：缓存只能作为运行期优化，事实来源必须来自协议或平台返回的 authoritative 标识；禁止做法：用本地请求 id、文本比对、时间戳猜测或历史缓存替代 authoritative 标识；验证方式：覆盖重复结果、乱序确认、缓存命中但远端身份不匹配和 stale 恢复被拒绝的测试。
   - 触发条件：同时存在前台会话、后台已连接会话、未连接远端条目或冷启动恢复数据；原生期望行为：已连接运行态属于同一 authoritative 层级，未连接条目只能展示最小发现元数据；禁止做法：在 authoritative 加载前泄露旧正文、伪造预览或把未连接条目提升为可交互运行态；验证方式：覆盖前后台切换、冷/热恢复、连接断开和首次权威加载前 UI 内容边界的测试或 GUI smoke。
   - 触发条件：执行 GUI smoke、安装包验证、发布前回归或跨平台运行验证；原生期望行为：验证对象必须是本次构建实际产出的安装物、二进制或发布包；禁止做法：用旧安装、旁路产物、开发服务器缓存或来源不明的运行实例替代；验证方式：记录构建产物路径、版本/提交来源和启动实例来源，并在验证脚本或报告中可追溯。
   - 触发条件：新增或修改协议方法、字段、能力声明或自定义扩展；原生期望行为：标准能力严格对照官方 schema 与 capability 协商，自定义扩展必须使用协议允许的命名和 payload contract；禁止做法：把扩展 payload 冒充标准字段、兼容未声明的 legacy 扩展，或在未声明能力时实际执行受限请求；验证方式：标准方法覆盖 schema/行为测试，扩展方法覆盖 capability gating、method-not-found 和 contract round-trip 测试。详见 `docs/audit/acp-standard-vs-extension-contracts.md`。
   - 触发条件：运行态复用、前后台切换、跨配置切换或后台连接恢复；原生期望行为：只有当前配置、远端会话、连接实例和运行期投影身份都由 authoritative runtime 证明一致时，才允许零往返复用；禁止做法：用单一配置名、本地缓存、最近可见状态、reason 字符串或身份解析失败的占位值替代完整身份比较；验证方式：覆盖身份匹配矩阵、同身份不重复加载、身份变化必须重新恢复，以及真实用户路径 GUI smoke。详见 `docs/hard-constraints-session-navigation-and-search.md` 的 Warm Reuse 约束。
   - 触发条件：缓存页面、延迟加载区域、嵌套 ViewModel 或只读 UI 文本在事实源更新后需要刷新；原生期望行为：View 继续由单一 ViewModel 事实源驱动，并通过框架生成的绑定刷新机制投影到原生控件；禁止做法：手写控件属性、让 ViewModel 反向感知控件状态，或新增第二套 UI 文本/标题状态；验证方式：用合同测试断言事实源变化只触发绑定刷新机制，并用真实 GUI 路径覆盖重复切换时选中态、正文和标题同源。
   - 触发条件：可选择富文本同时包含链接、代码、自动识别内容或其它可交互内联元素；原生期望行为：继续使用控件原生文本选择能力，并通过官方配置让选择语义与交互语义互斥；禁止做法：用指针事件、命中测试、手动恢复选择、模板替换或 ViewModel 感知鼠标状态来补偿原生选择行为；验证方式：覆盖跨可交互内联元素拖选不丢失选择、不触发交互动作、不崩溃，并增加防止输入拦截式修复回归的合同测试。
   - 触发条件：新增或修改设置页二级导航、嵌套 Frame 编辑页、表单首项焦点、焦点恢复或 `XYFocus*` 链接刷新；原生期望行为：选中态和方向键关系可由 ViewModel 与原生控件投影，但焦点只能响应明确的用户意图、系统导航或显式入口请求，不能在页面已可交互后异步覆盖后续用户输入；禁止做法：在 section 激活、Frame 导航或 Loaded 回调后用 `DispatcherQueue` 延迟把焦点拉回导航项、标题栏、容器或旧控件，导致 TextBox/ComboBox 等原生输入控件第一次点击或输入被吞；验证方式：源码合同测试禁止重新引入 section 导航延迟焦点回拉，并用本次构建产物的 WASM/GUI smoke 覆盖进入编辑页、填写首个输入框、保存后刷新仍持久化。
   - 触发条件：新增或修改跨平台能力声明、本地资源访问、平台受限入口、或配置保存与执行链路；原生期望行为：发现入口、协议能力声明和实际执行权限必须由同一个平台能力事实源驱动，不支持的平台必须在产生副作用前明确拒绝，并保留用户的原始配置意图；禁止做法：在 ViewModel 或业务逻辑中散落平台判断、用默认值隐式开启受限能力、静默改写用户配置、或绕过能力边界直接创建平台资源；验证方式：覆盖能力策略、配置投影和执行边界的行为测试，断言不支持平台不会创建受限资源或声明不可执行能力，并通过至少一个支持平台与一个受限平台的真实构建验证。
   - 触发条件：新增或修改配置项、导航项、远端目录、profile 选择、会话恢复入口或其它跨层 semantic id；原生期望行为：semantic id 的构造、解析、分类和运行时事实映射必须由单一 resolver/catalog owner 提供，ViewModel、DI 和平台服务只传递用户意图并调用该 owner；禁止做法：在多个调用方复制字符串前缀、用本地项目 id 解析器处理远端目录 id、把未知远端 id 回退成本地路径、或静默改写用户填写的 endpoint/配置值；验证方式：resolver 行为测试覆盖本地、远端、未知和冲突 id，真实用户路径 smoke 覆盖创建会话到发送消息，并用源码合同测试防止前缀/解析规则重新散落到 ViewModel、DI 或平台实现。
   - 触发条件：新增或修改配置、缓存、日志、诊断包、导出文件或安全存储等跨平台持久化入口；原生期望行为：构造函数只绑定依赖和逻辑位置，真实文件系统副作用必须延迟到明确的读写、导出或迁移操作，并通过统一存储抽象或平台能力事实源执行；禁止做法：在构造函数、属性 getter、ViewModel 初始化或 DI 注册期创建目录、枚举文件、写入日志、执行迁移，或让业务层直接假设所有平台都有桌面文件系统；验证方式：覆盖服务构造不触盘、首次写入才创建容器、不支持平台在副作用前返回明确失败，以及至少一个支持平台与一个受限平台的构建验证。
   - 触发条件：新增或修改指针光标、系统 picker、外部打开、剪贴板、标题栏、窗口控制或其它平台原生 affordance；原生期望行为：共享 UI 只表达用户意图和绑定状态，平台原生对象只出现在 `Platforms/` 实现、平台 partial 或平台服务中，并由能力事实源统一投影到 ViewModel；禁止做法：在共享控件、共享 code-behind、ViewModel 或业务服务中直接引用平台原生类型、用异常探测能力、或让按钮/命令绕过能力边界静默失败；验证方式：用源码合同测试断言共享层无平台原生类型泄漏，用行为测试覆盖不支持平台的禁用/明确失败路径，并至少通过一个受限平台构建。
   - 触发条件：新增或修改应用标题栏、系统窗口 inset、移动端浏览器 chrome、全屏/窄屏 shell 布局或平台标题栏适配；原生期望行为：平台服务只报告系统提供的原生窗口/安全区信息，应用 shell 是否显示、占位高度和内容避让必须由布局事实源基于可见应用 chrome 语义统一推导；禁止做法：把平台返回的 `0`/不可用 inset 直接解释为隐藏应用标题栏，混淆系统 chrome 与应用 chrome，或只用构建通过、桌面窗口截图、远端门禁状态替代真实目标 viewport 的可见性验证；验证方式：行为测试覆盖 inset 缺失、为零、负值和恢复路径，并通过 WASM/移动窄屏 GUI smoke 断言应用标题栏、导航入口和内容首屏同时可见且无手写控件状态补偿。
   - 触发条件：新增或修改 UI 语言、资源目录、语音/区域设置、WASM satellite resources 或平台语言覆盖；原生期望行为：持久化值、资源目录、平台 override 和打包白名单全部来自同一个 canonical BCP-47 语言事实源；禁止做法：在 XAML、ViewModel、平台服务或构建配置中分别硬编码 `zh`/`zh-CN`/`zh-Hans` 等别名，或依赖平台 fallback 掩盖标签不一致；验证方式：catalog 单元测试覆盖 legacy alias 到 canonical tag，持久化测试断言只写 canonical tag，XAML 合同测试禁止绕过语言 catalog。
   - 触发条件：新增或修改页面级辅助面板、弹出层、抽屉、底部面板或其它由布局状态控制的可见区域；原生期望行为：面板是否存在、是否可见、尺寸和互斥关系必须由布局 ViewModel / Store 的单一事实源投影，并交给平台控件执行显示、焦点和可访问性语义；禁止做法：在 code-behind 中用 Storyboard、Timer、Completed handler、局部 phase enum、手写 `Visibility` / `Opacity` / `RenderTransform` 状态机补偿 ViewModel 状态，或保留已废弃动效 coordinator 作为兼容层；验证方式：布局策略/Store 行为测试覆盖互斥与清理，XAML 合同测试断言可见性绑定到布局事实源且禁止重新引入手写面板动画状态机，并通过目标平台构建或 GUI smoke 验证。
   - 触发条件：新增或修改会挤压主内容区的辅助 pane、右侧面板、详情抽屉、聊天区与底部面板共享宽度的 shell 布局；原生期望行为：使用 `SplitView` / `NavigationView` 等原生布局控件与其官方 `DisplayMode` 语义表达开闭和重排，主内容尺寸变化必须来自控件模板与布局系统，应用层只绑定 `IsPaneOpen` / `OpenPaneLength` 等声明式状态；禁止做法：用 `CompactInline` 搭配 `CompactPaneLength=0` 冒充隐藏 pane，用平台 Composition `ImplicitAnimations`、手写 `Size` / `Offset` 动画、Storyboard、定时器或 code-behind 事件补偿内容区宽度跳变，或为单一平台添加不同于 Uno/WinUI 原生控件的过渡路径；验证方式：XAML 合同测试断言 mode 与 pane 长度符合官方语义并禁止手写布局动画 hook，布局 ViewModel/Store 测试覆盖开闭和尺寸投影，必要时用目标平台 GUI smoke 验证右侧 pane 开闭时聊天区、底部面板和内容区同源重排。
   - 触发条件：新增或修改应用级动画开关、无障碍动效偏好、页面/状态过渡或会影响原生控件模板 motion 的资源字典；原生期望行为：系统/平台继续拥有原生控件模板动效时长和可访问性降级，应用层用官方 `UISettings.AnimationsEnabled` 读取系统动画偏好，用 `Timeline.AllowDependentAnimations` 表达应用级 dependent-animation 策略，并只在自己显式声明的 `NavigationTransitionInfo`、`TransitionCollection` 或 ViewModel 投影上表达偏好；用户可见文案必须合并为一个“动画效果”心智模型，只描述已经真实接入的页面切换、应用内状态提示等可感知场景，不暴露 dependent animation、native control template 等实现术语；禁止做法：覆盖 `SplitViewPaneAnimation*`、`Control*AnimationDuration`、`ScrollBar*Duration` 等 WinUI 主题资源，改写 `FeatureConfiguration.ThemeAnimation.DefaultThemeAnimationDuration`，用全局资源字典把原生控件 motion 置零，用普通应用开关调用受限系统管理 API 改写系统全局设置，或在设置文案中把应用自有过渡描述为原生控件模板动效总开关；验证方式：XAML 合同测试扫描禁止覆盖原生 motion theme resource keys，源码合同测试断言应用启动和运行时服务不注入 reduced-motion 字典、不改写平台默认动效时长，并用设置页文案合同测试断言开关 scope 明确且不泄露实现术语。
   - 触发条件：新增或修改 `NavigationView` 的顶部导航、横向折叠、overflow 菜单、选择投影或菜单项来源；原生期望行为：继续使用原生 `NavigationView` 的 Top/overflow 布局与选择语义，菜单内容由单一 ViewModel/catalog 事实源投影到稳定的原生菜单项；禁止做法：自制横向菜单替代原生 overflow、在 overflow 打开期间改写第二套选择状态，或在已知会造成 Uno Top overflow 分裂集合索引失配的场景中绑定可变 `MenuItemsSource`；验证方式：合同测试断言顶部导航保留 `PaneDisplayMode="Top"`、禁止重新引入脆弱 `MenuItemsSource` 路径，并通过窄屏设置页 overflow GUI smoke 或目标平台构建验证。
   - 触发条件：新增或修改 WASM 可见导航、设置页二级导航、窗口宽度断点、或 `NavigationView` overflow 可交互路径；原生期望行为：真实浏览器运行实例必须能通过用户可见控件触发原生 overflow、选择 overflow 内项目并更新同一 ViewModel 驱动的内容区；禁止做法：只用 Windows/FlaUI 结果推断 WASM 行为、用隐藏测试入口绕过原生 overflow、或在 smoke 中依赖旧构建/开发服务器缓存；验证方式：执行 `scripts/gates/run-wasm-smoke-gates.sh`，确认本次 `net10.0-browserwasm` 产物在窄屏设置页 overflow 路径无 `NativeDispatcher` / `NavigationView.GetItemFromIndex` / `ArgumentOutOfRange`，且 overflow 项能切换内容页。
   - 触发条件：新增或修改全局搜索、命令面板、最近会话、更多列表、分页列表或其它可从完整 catalog 激活会话的入口；原生期望行为：语义选中会话在投影给 `NavigationView.SelectedItem` 前，必须由同一导航 ViewModel/catalog 事实源物化为当前 `MenuItemsSource` 中的稳定菜单项；禁止做法：把未物化的搜索结果、分页外对象或本地临时对象直接投影为原生 `SelectedItem`，或在 `SelectionChanged` / code-behind 中事后回写、清空、重选来修补；验证方式：行为测试覆盖从完整 catalog 激活分页外 session 后，左侧导航仍选中同一 session、菜单项已物化且 More 计数一致，并通过真实 GUI smoke 覆盖搜索进入会话后继续导航不乱序。
   - 触发条件：新增或修改可编辑列表、设置行、工具配置行等会在同一页面内新增、删除、再新增的 item action；原生期望行为：每个可交互行的命令身份必须由稳定的行 ViewModel 或显式 command parameter 提供，集合变更后由绑定重新投影到原生 item 容器；禁止做法：让父 ViewModel 在集合变更时给行对象反复注入可变命令、依赖当前选中项/可视容器/旧 item index 执行动作，或用刷新整个页面掩盖 stale command；验证方式：单元测试覆盖行命令稳定性和集合变更行为，并通过真实 GUI smoke 覆盖新增、删除、再新增后页面仍可交互且无崩溃。
   - 触发条件：新增或修改物理手柄、RawGameController、DPad、摇杆、按钮或跨平台输入映射；原生期望行为：标准 `Gamepad` 与 `RawGameController` 都作为同一平台输入事实源的可用投影，标准读数无 active input 时仍允许 Raw fallback 读取同一物理设备，Raw axis 必须按官方 `0.0..1.0` 范围归一化到应用统一 reading，八向 switch 必须按 enum 离散值映射而不是按 flags 拆位；禁止做法：因 Raw 与标准 Gamepad 匹配就跳过 Raw fallback、把 `GameControllerSwitchPosition` 当 flags、把 `axis - 0.5` 当完整摇杆范围、按设备名称或经验猜测按钮 index、或让 ViewModel/UI 直接引用 `Windows.Gaming.Input` 类型；验证方式：用 Core 行为测试覆盖 axis 归一化和八向 switch 映射，用源码合同测试禁止重新引入 Raw fallback 跳过和 flags 解析，用真实 Windows 探测或诊断页 smoke 记录 VID/PID、button labels、switch/axis 值，并通过本次 MSIX GUI smoke 验证导航链路。
   - 触发条件：新增或修改手柄/遥控器对 `NavigationView`、列表、按钮、标题栏或其它原生可聚焦控件的导航与激活；原生期望行为：标准控件继续拥有自己的键盘、DPad、焦点、层级导航和激活语义，应用层只读取设备事实、处理明确 opt-in 的语义消费者和应用级返回意图；禁止做法：把 `Gamepad` / `RawGameController` 读数在 shell 层翻译成全局 `FocusManager.TryMoveFocus`、AutomationPeer invoke/toggle/expand、`SelectedItem` 回写、AutomationId/Tag 匹配或控件特例导航；验证方式：源码合同测试断言 shell dispatcher 不合成原生控件焦点或激活，GUI smoke 覆盖 `NavigationView` 键盘层级路径（如 project -> session child）继续可用，并用真实设备或诊断页确认硬件输入事实源是否进入应用。
   - 触发条件：新增或修改键盘、手柄、遥控器、快捷键、无障碍开关或其它输入设备接入原生已支持的控件语义；原生期望行为：设备采集可以有平台服务，但一旦目标控件原生已支持该类导航/激活/选择语义，最终必须收敛到同一条 authoritative 原生输入语义链，避免键盘、虚拟按键、物理设备各走一套状态机；禁止做法：在保留原生键盘/焦点链的同时，再额外建立一条应用层 polling/intent dispatcher 去直接驱动第二套焦点、激活、选中或展开状态，或让诊断输入链与真实 UI 行为链长期分叉；验证方式：覆盖同一控件在键盘、虚拟输入和真实设备下表现一致的行为测试或 GUI smoke，源码合同测试断言设备服务只负责事实采集与 opt-in 语义分发，不在 shell 层平行拥有原生控件状态机。
   - 触发条件：同一物理输入事件可能同时被平台原生控件、应用层输入轮询、虚拟按键桥接或诊断注入路径观察到；原生期望行为：每个用户意图只能进入一条 authoritative 行为链，原生已支持的焦点移动、选择、值编辑和激活继续由控件原生输入语义处理，应用层补偿只能在已证实缺口处消费该意图并阻止二次派发；禁止做法：让同一个 DPad/方向/确认/返回意图同时触发原生事件和应用层 fallback，把手柄/遥控器方向键全局模拟成普通键盘方向键，或用“测试能移动焦点”作为理由保留会改变 selector/value control 值的并行输入链；验证方式：合同测试断言 fallback 不覆盖原生已支持方向，GUI smoke 必须记录一次输入前后的焦点路径和值变化，真实设备验证必须确认一次物理按下只产生一次用户可见行为，且 selector/value control 在未显式进入编辑/展开态前不会因经过而改值。
   - 触发条件：新增或修改 WinUI 3 / Uno 下物理手柄已能进入应用、但原生控件对某个特定 gamepad 路径仍存在已确认缺口（例如 `NavigationView` 吞掉 `DPadRight`，或页面级返回未沿原生 back 语义触发）；原生期望行为：默认仍以物理手柄事实源和控件原生焦点/激活语义为准，只有在明确、局部、可复现的控件缺口处，才允许使用最薄的平台补偿将该意图桥接回现有原生键盘/内容入口语义；禁止做法：把全部 gamepad 输入统一降级为键盘模拟、让 synthetic `VirtualKey`/UIA 焦点结果凌驾于真实物理手柄行为之上、或为通过 smoke 继续在共享层/页面层叠加第二套横向导航状态机；验证方式：先用真实物理手柄或手柄诊断事实源确认产品路径，再用新构建产物上的 targeted GUI smoke 验证补偿只命中该局部缺口；当 synthetic smoke 与物理手柄结论冲突时，必须先修验证链或测试夹具，禁止直接据此修改产品逻辑。
   - 触发条件：新增或修改 `NumberBox`、`ComboBox`、`Slider`、`ListView` 等 selector/value control 的手柄编辑、展开、engagement 或方向键行为；原生期望行为：只有官方文档明确支持 focus engagement 或 gamepad 编辑语义的控件，才把 `A/确认` 进入编辑态、`B/返回` 退出编辑态作为产品契约；未被官方列入支持范围的控件，只能保证可聚焦、可离开、不误改值，不能把偶发平台行为升级为必须保持的编辑能力；禁止做法：仅凭 `IsFocusEngagementEnabled="True"` 就假定任意控件拥有原生手柄编辑态，针对 unsupported control 叠加页面级 key handler / 自定义编辑状态机补足“进入修改态”，或编写要求 synthetic `Activate + DPad` 必须改值的 GUI smoke 来倒逼产品偏离原生；验证方式：先核对该控件官方文档是否明确声明 focus engagement / gamepad 语义，再用真实手柄验证“到达、离开、是否误改值、是否能退出”，测试仅锁定文档支持的原生契约，不能把未文档化的 incidental behavior 写成长期门禁。
   - 触发条件：新增或修改 Top `NavigationView`、二级导航、设置页内容区、重复行控件或底部动作之间的方向键/手柄焦点关系；原生期望行为：`NavigationView` 自己的 pane 内键盘语义以官方文档为准，Top 模式 `Up`/`Down` 不代表进入外部内容区，跨区域移动只能用原生 `XYFocus*` 在真实可聚焦 `Control` 之间表达，列表外观不需要选择态时应避免让 `ListViewItem` 成为行级焦点 owner；禁止做法：把 Top `NavigationView` 的 `Down` 进入内容写成官方键盘行为、用页面级 KeyDown/DPad handler、`FocusManager.TryMoveFocus`、非可聚焦 `Border`/`UIElement` 代理、隐藏元素或行级 `ListView` 选择容器补偿焦点路径，或让测试因为 synthetic 输入可达就要求产品覆写原生 `NavigationView` 语义；验证方式：用本次构建产物的 targeted GUI smoke 分别覆盖选中二级导航、进入内容、内容首项返回二级导航、底部动作返回上一真实控件，并在失败信息中记录焦点 AutomationId、ControlType、ClassName、可见性和 bounds；真实手柄与 synthetic 结论冲突时先修验证链。
   - 触发条件：新增或修改远程会话的选择、连接、overlay、hydration、warm reuse 或历史恢复链路；原生期望行为：有 `remoteSessionId` 的会话在 authoritative runtime/hydration 证明前只允许 selection-only 投影，连接能力判断必须发生在对应 profile 连接完成后，正文只能来自协议恢复或已证明同身份 warm runtime；禁止做法：用当前 `_chatService` 能力缺失、workspace snapshot、本地缓存或空 transcript 把冷启动远程会话降级为已加载本地正文，或在未连接前把 recovery capability 缺失解释为成功；验证方式：单元测试覆盖无 ready chat service 的远程选择必须使用 `SelectionOnly`，冷启动 WebSocket 历史会话必须进入连接 overlay 并发起 `session/load`，缺失 recovery capability 必须 fault 且不得调用恢复协议。

   - 触发条件：新增或修改会话激活、hydration、连接恢复或远端 session/load 等链路，并且其失败路径通过 ViewModel 错误状态（如 HasError/ErrorMessage）上报；原生期望行为：View 层必须为 ViewModel 的每一类可见错误状态提供对应的 UI 投影（callout、banner 或错误区），使用户在激活/恢复失败时能看到明确的错误提示，而不是空白内容区；禁止做法：只为 turn 级失败（如工具调用、prompt rejection）提供 UI callout，而把激活/hydration 级失败（如 Resource not found、协议 fault）留在 ViewModel 中静默吞掉，或用 Debug 日志替代用户可见投影；验证方式：XAML 合同测试断言 ChatView 与 MiniChatView 中存在 AutomationId 绑定到 ViewModel.HasError/ViewModel.ErrorMessage 的激活失败 callout，行为测试覆盖 TrySetActivationErrorAsync 设置错误后 callout 可见且内容与错误消息一致，GUI smoke 覆盖打开已过期 WebSocket/Stdio 历史会话时聊天区显示红色错误提示而非空白。