# Case Study 规则目录

本文件承载 `AGENTS.md` 第 11 节的展开说明。`AGENTS.md` 只保留高频、可执行的规则索引；具体事故、旧症状和一次性调查记录不应继续堆叠到 agent-facing 入口。

## 维护规则

1. 每条规则必须能回答四个问题：触发条件、原生期望行为、禁止做法、验证方式。
2. 规则必须可复用到同类问题，不得只描述某个页面、某个异常字符串或某次修复经过。
3. 具体案例证据可以写入专题 audit 文档，并从本文件链接；不要把原始事故叙事复制到 `AGENTS.md`。
4. 当某条规则已经被更通用的规则覆盖，应合并或删除，避免同一原则以多个局部 case 重复出现。

## 规则目录

### 原生控件状态

- 触发条件：新增或修改控件选择态、焦点态、展开态、容器语义、可访问性投影、可选择富文本或可交互内联元素。
- 原生期望行为：状态继续由 WinUI / Uno 原生控件状态模型产生；应用层提供数据、用户意图和官方配置。
- 禁止做法：在 ViewModel、样式补丁、指针事件、命中测试或 code-behind 中反向回写原生视觉状态，或用模板替换掩盖状态来源冲突。
- 验证方式：覆盖键盘、鼠标/触控、焦点恢复、文本选择、辅助功能语义和跨可交互元素拖选的行为测试或 GUI smoke。

### 单一状态链路

- 触发条件：新增或修改导航、选择、内容切换、加载阶段、错误恢复、全局搜索、命令面板、分页列表或最近会话入口。
- 原生期望行为：一次用户意图只进入一条 authoritative 状态变更链路；选中态、内容区、加载状态和错误状态同源。
- 禁止做法：第二套事件、延迟回写、事后纠偏、局部缓存、未物化对象直接投影为原生 `SelectedItem`，或在 `SelectionChanged` / code-behind 中补偿。
- 验证方式：覆盖快速切换、重复点击、失败恢复、过期回调、分页外激活、搜索进入会话后继续导航的顺序测试或 GUI smoke。

### UI 线程与 Latest Intent

- 触发条件：异步结果、远端事件、后台任务、语音/诊断结果或搜索结果进入 UI 可绑定状态。
- 原生期望行为：结果先完成 UI 线程封送，并验证仍匹配最新用户意图后再投影。
- 禁止做法：后台线程直接触发可视状态变更，或允许 stale 回调覆盖当前状态。
- 验证方式：覆盖并发完成、取消、反序返回、最新意图判定和 UI dispatcher 线程语义。

### 远程会话事实源

- 触发条件：新增或修改远程会话选择、连接、overlay、hydration、warm reuse、历史恢复、发现/列表接口、详情加载接口或运行上下文投影。
- 原生期望行为：正文、恢复、warm reuse、连接能力和可交互状态来自 authoritative runtime / protocol / connection identity；发现接口只提供元数据。
- 禁止做法：用 workspace snapshot、本地缓存、空 transcript、discover/list 结果或未连接 profile 能力缺失推导已加载正文或恢复成功。
- 验证方式：覆盖 cold / warm 恢复、连接身份变化、能力缺失、`session/load` fault、metadata-only discovery 和首次权威加载前 UI 内容边界。

### 协议与扩展边界

- 触发条件：新增或修改 ACP 方法、字段、能力声明、标准请求/响应模型、自定义扩展或协议测试。
- 原生期望行为：标准能力严格对照官方 schema 与 capability 协商；自定义扩展使用协议允许的命名、`_meta` 和 capability contract。
- 禁止做法：把扩展 payload 冒充标准字段、兼容未声明 legacy 扩展、在未声明能力时执行受限请求，或保留非标准 root 字段。
- 验证方式：标准方法覆盖 schema/行为测试，扩展方法覆盖 capability gating、method-not-found 和 contract round-trip。
- 参考：`docs/audit/acp-standard-vs-extension-contracts.md`。

### 缓存与持久化边界

- 触发条件：新增或修改缓存、去重、确认、恢复、离线投影、配置、日志、诊断包、导出文件或安全存储入口。
- 原生期望行为：缓存只作为运行期优化；事实来源来自协议或平台 authoritative 标识；真实文件系统副作用延迟到明确读写、导出或迁移操作。
- 禁止做法：用本地请求 id、文本比对、时间戳猜测或历史缓存替代 authoritative 标识；在构造函数、getter、ViewModel 初始化或 DI 注册期创建目录、枚举文件、写日志或迁移。
- 验证方式：覆盖首次写入、unsupported platform、身份不匹配、重复/乱序结果、stale 恢复拒绝和服务构造不触盘。

### 平台能力与原生 Affordance

- 触发条件：新增或修改本地资源访问、系统 picker、外部打开、剪贴板、标题栏、窗口控制、指针光标、平台受限配置或能力声明。
- 原生期望行为：发现入口、协议能力声明和实际执行权限由同一个平台能力事实源驱动；共享 UI 只表达用户意图和绑定状态。
- 禁止做法：在共享控件、共享 code-behind、ViewModel 或业务服务中直接引用平台原生类型、异常探测能力、绕过能力边界执行副作用或静默改写用户配置。
- 验证方式：覆盖支持平台、受限平台、共享层无平台原生类型泄漏和不支持平台产生副作用前明确失败。

### Shell 与布局事实源

- 触发条件：新增或修改标题栏、安全区、移动端浏览器 chrome、页面级面板、弹出层、抽屉、底部面板、右侧 pane 或会挤压主内容区的布局。
- 原生期望行为：平台服务只报告系统窗口/安全区事实；应用 shell 是否显示、占位高度、互斥关系、开闭状态和内容重排由布局 ViewModel / Store 与原生布局控件投影。
- 禁止做法：Storyboard、Timer、Completed handler、局部 phase enum、手写 `Visibility` / `Opacity` / `RenderTransform` 状态机、手写宽度动画、隐藏 pane hack 或把系统 inset 误当应用 chrome。
- 验证方式：覆盖布局策略、互斥清理、inset 缺失/为零/恢复、目标 viewport 和目标平台 GUI smoke。

### Motion 与本地化事实源

- 触发条件：新增或修改动画开关、无障碍动效偏好、页面/状态过渡、资源目录、语言设置、WASM satellite resources 或平台语言覆盖。
- 原生期望行为：系统/平台继续拥有原生控件模板动效；应用层只在自有过渡和 ViewModel 投影上表达偏好；持久化值、资源目录、平台 override 和打包白名单来自 canonical BCP-47 事实源。
- 禁止做法：覆盖原生 motion theme resource keys、改写系统全局设置、把实现术语暴露给用户，或在 XAML/ViewModel/平台服务/构建配置中硬编码语言别名。
- 验证方式：覆盖资源目录、持久化 canonical tag、motion scope 文案、禁止覆盖原生 motion key 和禁止注入全局 reduced-motion 字典。

### 输入设备语义

- 触发条件：新增或修改键盘、手柄、遥控器、RawGameController、DPad、摇杆、按钮、快捷键、虚拟输入或诊断注入。
- 原生期望行为：设备服务采集事实；原生控件已支持的焦点、激活、选择和值编辑语义继续由原生输入链处理；应用层只处理明确 opt-in 的语义消费者和已证实缺口。
- 禁止做法：shell 层全局模拟键盘方向键、合成 `FocusManager.TryMoveFocus` / AutomationPeer 操作、平行驱动 `SelectedItem` / 值编辑、把 incidental behavior 写成产品契约。
- 验证方式：覆盖真实设备、synthetic 差异、一次物理输入只产生一次用户可见行为、selector/value control 不误改值，以及 unsupported control 只保证可达、可离开、不误改值。

### 可编辑行与语义 ID

- 触发条件：新增或修改配置项、导航项、远端目录、profile 选择、会话恢复入口、可编辑列表、设置行或工具配置行。
- 原生期望行为：semantic id 的构造、解析、分类和运行事实映射由单一 resolver/catalog owner 提供；可交互行命令身份由稳定行 ViewModel 或显式 command parameter 提供。
- 禁止做法：多处复制字符串前缀、本地项目 id 解析器处理远端目录 id、未知远端 id 回退本地路径、父 ViewModel 反复注入 stale command、依赖旧 index 或刷新整个页面掩盖 stale command。
- 验证方式：resolver 行为测试覆盖本地、远端、未知和冲突 id；真实用户路径 smoke 覆盖创建会话到发送消息；行命令测试覆盖新增、删除、再新增后仍可交互。

### 真实构建验证

- 触发条件：执行 GUI smoke、安装包验证、发布前回归、WASM 可见导航、跨平台运行验证或发布包验证。
- 原生期望行为：验证对象必须是本次构建实际产出的安装物、二进制、发布包或静态产物。
- 禁止做法：旧安装、旁路产物、开发服务器缓存、隐藏测试入口或来源不明运行实例替代真实用户路径。
- 验证方式：记录构建产物路径、版本/提交来源和启动实例来源；WASM 路径必须使用真实浏览器实例和当前 `net10.0-browserwasm` 产物。
