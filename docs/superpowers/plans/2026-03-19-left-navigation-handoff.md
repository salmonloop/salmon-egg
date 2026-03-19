# Left Navigation SSOT/MVVM Handoff

## 背景

本轮工作的目标，是把左侧导航从“`MainPage` + `NavigationView` 控件行为 + 若干散落 ViewModel 调用”重构为更严格的 SSOT / MVVM 结构。

重点问题有两类：

1. 左侧导航在 compact / expanded 切换时，当前会话的可见焦点会丢失。
2. 点击具体会话时，Chat 页面有时不会切到对应会话。

另有一个独立的长期 review finding 需要保留：

- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs`
- P1: `ChatViewModel` 的 store subscription 生命周期管理问题

该问题不是本次 nav 重构主因，但仍是必须继续跟踪的架构问题。

---

## 已完成的架构重构

### 1. 语义选中状态与控件投影分离

已新增：

- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationSelectionState.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationViewProjection.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationSelectionProjector.cs`

当前设计：

- `NavigationSelectionState`
  - `Start`
  - `Settings`
  - `Session(sessionId)`
- `MainNavigationViewModel` 只持有语义真源
- compact / expanded 下该把谁投影给 `NavigationView.SelectedItem`，由 `NavigationSelectionProjector` 决定

这一步已经把“语义选中”和“控件可见选中”显式拆开。

### 2. 引入统一导航协调器

已新增：

- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\INavigationCoordinator.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationCoordinator.cs`

当前 coordinator 已负责：

- `ActivateStartAsync()`
- `ActivateSettingsAsync(string settingsKey)`
- `ActivateSessionAsync(string sessionId, string? projectId)`
- `ToggleProjectAsync(string projectId)`

作用：

- 统一编排 NavVM 选中
- 切换 shell 内容页
- 切换 chat 当前 session
- 同步 `LastSelectedProjectId`

### 3. 主要入口已迁移到 coordinator

已完成迁移：

- `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Start\StartViewModel.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\GlobalSearchViewModel.cs`

当前这些入口已经不再各自散落地做：

- `SelectSession`
- `NavigateToChat`
- `TrySwitchToSessionAsync`

而是改为走 `INavigationCoordinator`。

### 4. 抽出了 UI-only NavigationView adapter

已新增：

- `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs`

当前职责：

- 处理 `NavigationView.SelectedItem` 投影
- 处理 `SettingsItem` 选择
- 处理 `ItemInvoked` 的稳定 tag 路由

这使得 `MainPage.xaml.cs` 不再直接承担完整的 `NavigationView` 状态机职责。

### 5. 导航项统一使用稳定 Tag

已改造：

- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavItemTag.cs`
- `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml`

当前稳定 tag 包括：

- `Start`
- `Session:<id>`
- `Project:<id>`
- `SessionsHeader`
- `More:<projectId>`

并已移除 `MainPage.xaml.cs` 中旧的 `ResolveInvokedItem(...)` fallback。

也就是说，当前不再依赖模板内部 `Grid/DataContext` 猜测点击对象。

---

## 当前仍未解决的问题

### 1. compact 下可见焦点仍可能丢失

这是当前最核心、仍未收口的问题。

现象：

- expanded 下当前 session 行通常能正确高亮
- 切到 compact 后，父 project 的可见焦点/强调有时会丢失
- 用户已多次提供截图证明该问题仍存在

当前判断：

- 语义选中大概率没有丢
- 丢的是 `NavigationView` 在 compact 形态下的可见 selected visual
- 这更像 Uno / WinUI `NavigationView` 对层级项 + compact ancestor emphasis 的控件行为不稳定

当前代码虽然已将“语义选中”和“控件投影”拆开，但还没有彻底定义出：

- compact 下到底依赖原生 `SelectedItem` 可见投影
- 还是使用独立、原生风格的 ancestor emphasis visual

这条必须在后续继续明确。

### 2. ChatViewModel review finding 仍需保留

Review finding：

- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs`
- store subscription 生命周期管理不严谨

虽然这条此前已做过若干修复，但在交接中必须继续视为高优先级关注项，避免后续 nav/session 切换问题与订阅泄漏混淆。

---

## 当前结构现状

### 已较为干净的层次

1. 语义选中
- `MainNavigationViewModel`
- `NavigationSelectionState`

2. 投影规则
- `NavigationSelectionProjector`
- `NavigationViewProjection`

3. 跨 VM 协调
- `NavigationCoordinator`

4. 复杂控件适配
- `MainNavigationViewAdapter`

5. 壳层接线
- `MainPage.xaml.cs`

### 仍然不够洁癖的点

1. compact 焦点仍依赖 `NavigationView` 原生 selected visual
2. `ProjectNavTemplate` 当前仍然 `SelectsOnInvoked="True"`
   - 这是为了 compact 原生高亮的妥协
   - 但从语义上，`Project` 更像分组节点而非真实导航目标
3. `MainPage.xaml.cs` 虽然已经瘦身，但仍有：
   - 壳层导航
   - pane 变化
   - selection re-apply
   - 内容页切换
   这些逻辑仍在同一个文件里

---

## 已验证通过的内容

### 定向测试

已通过：

```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --no-restore -nodeReuse:false --filter "FullyQualifiedName~Navigation|FullyQualifiedName~StartViewModelTests"
```

结果：

- 33 passed
- 0 failed

### Desktop 编译

已通过：

```powershell
dotnet build C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\SalmonEgg.csproj -c Debug --framework net10.0-desktop --no-restore -nodeReuse:false
```

结果：

- 0 warning
- 0 error

### 未做的验证

本轮未做：

- `MSIX` 打包
- 全量测试
- 自动化 GUI 验证

原因：

- 用户设备此前出现过接近假死/强制重启
- 当前策略是优先小范围验证，避免再次用重命令拖死机器

---

## 后续建议的处理顺序

### 第一优先级：收口 compact 焦点问题

建议方向：

1. 明确决策：compact 焦点是否继续依赖 `NavigationView.SelectedItem`
2. 如果 Uno/WinUI 原生行为不稳定，则不要再继续赌控件默认 ancestor highlight
3. 引入“独立但原生风格”的 compact ancestor emphasis 视觉状态
   - 由 VM projection 提供明确状态
   - 不再混用 `SelectedItem` 语义
4. 保持：
   - 语义选中仍然是 session
   - compact 下 project 只承担视觉强调，不承担真实导航语义

### 第二优先级：把 `Project` 节点重新收回“分组节点”语义

当前妥协：

- `ProjectNavTemplate` 仍为 `SelectsOnInvoked="True"`

后续理想目标：

- `Project` 不作为真实语义 selected target
- 点击 project 只展开/收起
- compact emphasis 使用独立视觉状态实现

### 第三优先级：处理 ChatViewModel review finding

继续审查：

- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs`

重点：

- store subscription 是否完全有 CTS + dispose handle
- `Dispose` 后是否仍有 UI projection 回调
- 是否还有其它 ViewModel/service 使用类似反模式

---

## 关键修改文件列表

### 新增

- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationSelectionState.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationViewProjection.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationSelectionProjector.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\INavigationCoordinator.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationCoordinator.cs`
- `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs`
- `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationSelectionProjectorTests.cs`
- `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationCoordinatorTests.cs`

### 主要修改

- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModels.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavItemTag.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Start\StartViewModel.cs`
- `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\GlobalSearchViewModel.cs`
- `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml`
- `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs`
- `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`
- `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelPaneTests.cs`
- `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\NavigationCoreTests.cs`
- `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Start\StartViewModelTests.cs`

---

## 交接结论

本轮并非无效重构，已经完成了真正有价值的结构拆分：

- 语义状态从 View 逻辑中抽离
- 交互入口开始统一
- `NavigationView` 适配开始集中

但仍未达到“最终验收通过”状态。

当前结论应该如实表述为：

1. nav 架构已经明显更接近严格 SSOT / MVVM
2. 定向测试与 desktop 编译通过
3. compact 焦点丢失问题仍未最终解决
4. ChatViewModel 的 store subscription review finding 仍需继续跟踪

这份交接文档应作为后续继续修复 compact 焦点和 review finding 的起点，而不是作为最终完成报告。
