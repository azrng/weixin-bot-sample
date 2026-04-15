# AGENTS.md

## 适用范围

- 作用域：Blazor 页面、组件、布局、导航、状态、样式与界面测试
- 触发场景：涉及 Razor 组件、布局、导航、样式、交互、bUnit、UI smoke test 时阅读

---

## 技术栈

### Blazor Server（界面层）
- .NET 10 + Blazor Server（SignalR 实时通信）
- BootstrapBlazor 组件库（基于 Bootstrap 5）
- BootstrapBlazor 图标库（Bootstrap Icons）
- Blazor 内置路由管理
- Blazor 状态管理（`CascadingValue` / `CascadingParameter` / Scoped 服务）

### 设计系统
- 使用 BootstrapBlazor 主题系统，所有视觉规范在 `design-system.yaml` 中定义
- 优先使用 BootstrapBlazor 组件和样式，禁止 inline style，禁止硬编码颜色 / 间距 / 圆角

如果仓库已经有真实实现，以现有代码为准，不要强行重构或替换技术栈。

---

## 推荐目录结构

若仓库尚未形成稳定结构，可优先参考以下组织方式；若仓库已有实现，以现状为准，不强制迁移。

```text
src/
└── YourProject.Web/
    ├── Components/
    │   ├── Layout/
    │   │   ├── MainLayout.razor
    │   │   ├── NavMenu.razor
    │   │   └── LoginLayout.razor
    │   ├── Common/
    │   └── Pages/
    ├── Models/
    │   ├── DTOs/
    │   └── Enums/
    ├── Services/
    │   └── Interfaces/
    ├── wwwroot/
    │   ├── css/
    │   │   ├── app.css
    │   │   └── theme.css
    │   └── js/
    ├── Program.cs
    └── appsettings.json
```

---

## 阶段 1 — 界面与交互实现

**触发条件**：用户发出「开始开发」或「开始界面开发」指令

**入场要求**：阶段 0 设计文档已由用户确认

**工作内容**：
1. 按设计文档实现 Razor 页面、布局、导航和组件，遵循 `design-system.yaml` 与 BootstrapBlazor 规范。
2. 补齐页面的 `loading`、`empty`、`error`、`no-permission` 等关键状态。
3. 先搭建完整可交互的页面骨架，再逐步接入服务层能力。
4. 需要共享状态时，优先通过 Scoped 服务或 `CascadingValue` 管理。

**门控规则**：
- 主要页面和关键交互已可演示。
- 页面状态完整，且与设计文档保持一致。

---

## Blazor 界面规则

### Razor 组件规范
- 页面组件使用 `@page` 指令定义路由。
- 组件代码使用 `@code { }` 块，复杂逻辑提取到 `@inject` 的服务中。
- 组件参数使用 `[Parameter]` 特性标注。
- 事件回调使用 `[Parameter] public EventCallback<T> OnXxx { get; set; }`。
- 组件命名使用 PascalCase，文件名与类名一致。

### 组件通信规范
- 父传子：使用 `[Parameter]` 属性，只读不可修改。
- 子传父：使用 `EventCallback<T>`。
- 跨级传值：使用 `CascadingValue` / `CascadingParameter`。
- 全局状态：使用 Scoped 服务注入。
- 避免直接修改参数，需要双向绑定时使用 `@bind-Value`。

### 路由管理规范
- 路由通过 `@page` 指令在页面组件顶部声明。
- 路由参数使用 `@page "/users/{id:int}"`。
- 导航使用 `NavigationManager.NavigateTo()`。
- 路由守卫通过 `AuthorizeView` 和 `[Authorize]` 特性实现。
- 路由命名使用有意义的路径（如 `/users/list`、`/users/detail/{id}`）。

### 状态管理规范
- 组件内状态：`@code` 块中的局部变量和 `@bind` 双向绑定。
- 页面间状态：Scoped 服务（如 `UserStateService`）。
- 全局共享状态：Singleton 服务（如配置、主题）。
- 修改共享状态必须通过服务方法，禁止直接修改。
- 异步操作使用 `async Task` 方法，UI 更新通过 `StateHasChanged()` 或 `InvokeAsync(StateHasChanged)`。

### 样式规则
- 优先使用 BootstrapBlazor 组件内置样式和 class。
- 自定义样式使用 CSS 变量和 `wwwroot/css/app.css`。
- 禁止 inline style，禁止硬编码颜色值（如 `#1A90FF`）。
- 主题覆盖统一在 `wwwroot/css/theme.css`。

### 组件规则
- 优先复用 `Components/` 下已有组件，禁止重复创建。
- 只有确实有复用价值时才新增共享组件，避免为单次需求过度抽象。
- 页面状态必须完整：`loading`、`empty`、`error`、`no-permission`。
- 除非仓库已在使用，否则不要引入新的组件库或样式体系。

### 服务调用规则
- 页面组件通过 `@inject` 注入服务。
- 服务层封装业务逻辑，组件层只负责 UI 交互。
- 异步操作使用 `await`，禁止同步阻塞。
- 服务方法命名规范：`GetXxxAsync`、`CreateXxxAsync`、`UpdateXxxAsync`、`DeleteXxxAsync`。

---

## 测试规则

### 总体要求
- 影响行为的改动应优先补充或更新测试。
- 若本次改动未补测试，必须在最终说明中写明原因和风险。
- 测试应覆盖真实交互行为，而不只是静态渲染。

### Blazor 组件测试
- 使用 bUnit 进行组件测试（如需要）。
- 测试组件渲染输出、用户交互、参数传递。
- 至少关注关键页面状态和核心交互结果。

### 界面验证
- 对关键页面执行 smoke test，确认页面可访问、主要交互可触发、核心状态可展示。
- 对登录、菜单导航、关键表单和列表页优先补验证。

### 外部依赖与数据
- 测试中不要真实调用第三方服务，统一使用 mock。
- 测试数据应尽量最小化、可读、可重复执行。
- 不要让测试依赖本地人工状态。

### 无法执行测试时
- 必须说明未执行的测试类型。
- 必须说明未执行原因。
- 必须说明潜在影响范围和风险。

---

文件结束。
