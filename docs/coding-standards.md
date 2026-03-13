# SalmonEgg 代码规范（强约束）

本规范是**强约束**，用于指导人类与 Agent 的所有提交。除非明确标注“例外”，否则必须遵守。

## 0. 适用范围
1. 所有 `src/`、`SalmonEgg/` 与 `tests/` 下的代码、XAML、配置与脚本。
2. 所有新增与修改必须同时满足：可读性、可维护性、跨平台一致性。

## 1. 架构边界（硬性）
1. **Core 层（纯 .NET）**：只能包含与 UI 无关的逻辑，可被跨平台测试引用。禁止引用任何 UI 类型（如 `Microsoft.UI.Xaml.*`）。
2. **UI 层（Uno/WinUI）**：只做展示与绑定，不包含业务规则与领域逻辑。
3. **服务层**：平台相关能力必须通过接口封装；UI 仅与接口交互。
4. **禁止跨层引用**：
   - UI 不可直接引用 Infra 细节实现。
   - Domain 不可引用 UI 与 Platform 相关类型。

## 2. 项目结构与命名
1. **目录命名**：PascalCase。
2. **命名空间**：与目录结构一致。
3. **类/接口/枚举**：PascalCase；接口以 `I` 开头。
4. **字段**：私有字段以 `_` 前缀，camelCase。
5. **常量**：PascalCase；仅在逻辑不变时使用常量。
6. **方法/属性/参数/局部变量**：
   - 方法名：PascalCase，必须是动词或动词短语。
   - 属性名：PascalCase，必须是名词或形容词。
   - 参数/局部变量：camelCase。

## 2.1 类成员排序（硬性）
类中成员必须按以下顺序排列：
1. 常量
2. 静态字段
3. 实例字段
4. 构造函数
5. 属性
6. 事件
7. 方法（先 public，再 internal，再 private）

## 3. C# 语言规范
1. **启用 Nullable**：所有项目必须开启 `Nullable`。
2. **异步**：
   - IO、网络、磁盘、数据库必须 `async/await`。
   - 禁止 `.Result` / `.Wait()`。
3. **异常**：
   - 只有在无法恢复且需要上抛时才抛异常。
   - 业务逻辑必须返回 `Result<T>` 或同等结构；禁止用异常表达业务分支。
   - 捕获异常必须记录日志或转换为用户可理解结果。
4. **LINQ**：
   - 只用于读操作或短小投影。
   - 复杂逻辑必须用显式循环表达。
5. **日志**：
   - 必须使用结构化日志（模板占位符），禁止字符串插值。
   - 只保留可长期存在的业务日志。
   - 调试日志必须删除或在 `#if DEBUG` 中。
6. **方法体长度**：
   - 方法体超过 60 行（不含空行和花括号）必须拆分或在代码旁注释说明不可拆分原因。

## 3.1 依赖注入（硬性）
1. 只能使用构造函数注入。
2. 注入依赖必须做空检查（`ArgumentNullException`）。
3. 生命周期选择：
   - `Singleton`：无状态服务。
   - `Scoped`：每会话/每请求状态服务。
   - `Transient`：短生命周期、轻量对象（如 ViewModel）。

## 4. 测试规范
1. **Core 逻辑必须有单元测试**。
2. **UI 相关逻辑不得进入测试项目**，测试只能引用 Core。
3. **测试可跨平台运行**（禁止依赖 Windows-only 目标）。
4. **测试命名**：`Method_Scenario_Expected`。
5. **测试结构**：必须使用 AAA（Arrange/Act/Assert）分段。
6. **关键逻辑属性测试**：能用属性测试覆盖的序列化/解析/规则逻辑必须提供属性测试。

## 5. UI / XAML 规范（重点）
1. **绑定**：
   - 强制使用 `x:Bind`，除非 `Binding` 有明确必要（如运行时动态路径）。
   - 若使用 `Binding`，必须在同文件中写出原因注释。
2. **布局**：
   - 优先使用系统布局控件（`Grid`、`StackPanel`、`AutoLayout`、`ItemsRepeater`）。
   - 禁止用“像素微调”解决布局问题（例如随手设置 `Margin="1,3,0,0"`）。
   - 若必须使用像素值，必须通过资源定义，并说明原因。
3. **样式**：
   - 颜色、间距、圆角必须通过 `ThemeResource` 或 `StaticResource`。
   - 禁止直接硬编码颜色值（除非用于调试）。
4. **可视状态**：
   - 尽量使用 `VisualState`、`AdaptiveTrigger` 或 `ResponsiveExtension` 解决响应式问题。
   - 禁止依赖 `ActualWidth/Height` 进行布局计算。
5. **跨平台**：
   - 任何平台差异必须集中在 `#if` 或平台服务，不允许散布在业务逻辑里。
6. **控件支持**：
   - 禁止在 Uno 未实现的属性上绑定（会产生 Uno0001 警告）。
   - 若 WinUI 支持、Uno 不支持，必须有平台条件保护。

## 6. 资源与本地化
1. 所有 UI 文本必须通过 `.resw` 提供（启用 `x:Uid` 或资源绑定）。
2. 语言目录必须使用标准 BCP-47 标签（如 `en`, `en-US`, `zh-Hans`）。
3. 资源文件必须显式列出 `PRIResource` 语言标签，避免自动推断失败。

## 7. 序列化与裁剪（Trimming）
1. `JsonSerializer` 必须使用 **源生成上下文**，禁止运行时反射序列化。
2. 必须提供 `JsonSerializerContext`，并在调用时指定 `TypeInfo`。

## 8. 性能与可维护性
1. 列表、消息流等必须启用虚拟化控件。
2. 禁止在 UI 线程执行长任务。
3. 所有定时器/事件必须有明确解绑逻辑。

## 9. 代码提交规则
1. 每个提交必须自洽且可编译。
2. 新增功能必须附带测试或说明为什么无法测试。
3. 不允许提交包含调试代码或无意义日志。
4. 代码注释只能解释“为什么”，禁止解释“做什么”。
5. 提交信息必须使用如下格式：
```
<type>(<scope>): <subject>

<body>

<footer>
```
6. 提交类型必须为以下之一：
   - `feat` `fix` `docs` `style` `refactor` `test` `chore`

## 11. 代码审查清单（提交前自检）
1. 命名、层次、依赖边界符合本规范。
2. 业务异常已用 `Result<T>` 表达，未滥用异常。
3. 无调试日志、无未使用字段、无 TODO。
4. 核心逻辑已覆盖单元测试。
5. UI 无像素微调 hack，绑定优先 `x:Bind`。

## 12. 参考资料（不构成强约束）
- [Microsoft C# 编码约定](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [CommunityToolkit.Mvvm 指南](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [Clean Code by Robert C. Martin](https://blog.cleancoder.com/uncle-bob/2010/03/22/Code-Smells.html)
- [xUnit 测试最佳实践](https://xunit.net/)

## 10. 允许的例外
任何违反本规范的实现必须：
1. 在代码中写出原因（注释）。
2. 在 PR 说明中注明。

---

## 附：本项目关键约束摘要
- `x:Bind` 是默认绑定方式。
- UI 层只做展示，业务逻辑在 Core/Service。
- Layout 优先使用系统排版，避免像素 hack。
- Serialization 必须使用源生成。
- 所有日志必须可长期存在；调试日志一律删除或 #if DEBUG。
