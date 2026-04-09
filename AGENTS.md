# AGENTS.md

## 目标
职责划分如下：
- `AGENTS.md`：唯一协作规范来源，统一定义工作规则、任务机制、交付要求
- `CLAUDE.md` / `GEMINI.md`：各 Agent 的入口说明文件，仅负责提醒先读取 `AGENTS.md`，不维护平行规则内容
- `design-system.yaml`：设计 token 与 UI 规范
- `TASK.md`：具体任务内容与任务进度

---

## 工作方法

1. 行动前先思考，写代码前先阅读现有文件并理解业务目标。
2. 输出保持简洁，但分析与落地必须完整。
3. 优先编辑现有文件，而不是无差别重写。
4. 已确认无变更风险的文件无需重复阅读，但遇到上下文缺口时必须补读。
5. 完成前尽量执行可落地的校验，无法执行时明确说明原因和影响。
6. 避免奉承性开头或冗余结尾，直接进入有价值的信息。
7. 保持解决方案简单直接，避免过度设计。
8. 用户指令始终优先于本文件内容。

---
## 注意事项
1. 你需要理解用户提出的需求的业务逻辑然后再做设计和开发
2. 你需要根据设计的功能需求设计多级的菜单，管理员默认有所有菜单权限
3. 功能模块必须有完整可操作的功能页面
4. 所有的功能至少要确保能够完成基本的流程操作，功能的按钮必须都是可以交互的，界面的内容尽量多填充一些信息
5. 整体系统需要有一个登录页面，并要有默认应用图标，登录页面，登录系统左上角应该有应用图标和应用名称。
6. 系统设计时能有一些酷炫的动效，界面简洁大方，操作流程清晰易懂。
7. 如系统内所有涉及到变更、新增、删除等业务逻辑，必须能够真实生效的，而不是假的交互，确保浏览器刷新页面后能看到生效后的内容
8. 由于此系统的功能模块较多，实现的时候先把系统的框架搭起来，然后按照一个一个子模块的方式来实现，而不是一下子实现全部功能

---

## 技术栈

### Blazor Server（前端 + 后端统一）
- .NET 10 + Blazor Server（SignalR 实时通信）
- BootstrapBlazor 组件库（基于 Bootstrap 5）
- BootstrapBlazor 图标库（Bootstrap Icons）
- Blazor 路由（内置路由管理）
- Blazor 状态管理（内置 ` cascading values` / `Scoped` 服务）

### 后端服务层
- ASP.NET Core Web API（如需独立 API 接口）
- Clean Architecture 分层架构：
  - `API`（表现层）→ `Application`（应用层）→ `Domain`（领域层）← `Infrastructure`（基础设施层）
- ORM：Entity Framework Core（可选）
- 测试框架：xUnit + Moq

### 核心库
- `Azrng.Core` (1.15.8)：基础类库（实体基类、扩展方法、工具类、结果包装、异常体系）
- `Azrng.Core.Json` (1.3.1)：JSON 序列化（基于 System.Text.Json）
- `Azrng.AspNetCore.Core` (1.3.0)：API 基础设施（统一响应、模型校验、异常中间件）
- `Azrng.SqlMigration` (0.4.0)：SQL 脚本迁移执行（可选）
- `Common.Cache.Redis` (2.0.0)：Redis 缓存封装（基于 StackExchange.Redis，可选）

### 依赖注入规范
- 使用 Azrng 标记接口方式：
  - `ITransientDependency`：临时注入
  - `IScopedDependency`：作用域注入
  - `ISingletonDependency`：单例注入
- 在 Program.cs 中调用 `services.RegisterBusinessServices(assemblies)` 扫描并注册服务
- Blazor Server 中 Scoped 服务基于 SignalR 连接生命周期

### 部署
- Docker + Docker Compose
- 数据库：PostgreSQL（可选）

### 默认端口
| 服务                 | 容器内端口 | 宿主机映射端口 |
| -------------------- | ---------- | -------------- |
| Blazor Server 应用   | 8080       | 8080           |
| 数据库（PostgreSQL） | 5432       | 5432           |

- 若宿主机端口冲突，优先调整 Compose 映射端口（宿主机侧），容器内端口保持不变

### 设计系统
- 使用 BootstrapBlazor 主题系统，所有视觉规范在 `design-system.yaml` 中定义
- 优先使用 BootstrapBlazor 组件和样式，禁止 inline style，禁止硬编码颜色/间距/圆角

如果仓库已经有真实实现，以现有代码为准，不要强行重构或替换技术栈。

---

## 推荐目录结构
若仓库尚未形成稳定结构，可优先参考以下组织方式；若仓库已有实现，以现状为准，不强制迁移。

```text
project-root/
├── src/
│   ├── YourProject.Web/                    # Blazor Server 主项目
│   │   ├── Components/                     # Razor 组件
│   │   │   ├── Layout/                     # 布局组件
│   │   │   │   ├── MainLayout.razor        # 主布局
│   │   │   │   ├── NavMenu.razor           # 导航菜单
│   │   │   │   └── LoginLayout.razor       # 登录布局
│   │   │   ├── Common/                     # 通用业务组件
│   │   │   └── Pages/                      # 页面组件（按业务域拆分）
│   │   │       ├── Index.razor
│   │   │       ├── Login.razor
│   │   │       └── ...
│   │   ├── Data/                           # 数据访问（可选）
│   │   │   ├── Context/                    # DbContext
│   │   │   ├── Configurations/             # EF Core 配置
│   │   │   └── Migrations/                 # 迁移文件（可选）
│   │   ├── Models/                         # 数据模型
│   │   │   ├── Entities/                   # 实体类
│   │   │   ├── DTOs/                       # 数据传输对象
│   │   │   └── Enums/                      # 枚举
│   │   ├── Services/                       # 服务层
│   │   │   └── Interfaces/                 # 服务接口
│   │   ├── wwwroot/                        # 静态资源
│   │   │   ├── css/
│   │   │   │   ├── app.css                 # 自定义样式
│   │   │   │   └── theme.css               # 主题覆盖
│   │   │   ├── js/
│   │   │   │   └── app.js                  # 自定义脚本
│   │   │   └── lib/                        # 第三方库
│   │   ├── Program.cs                      # 应用入口
│   │   ├── appsettings.json                # 配置文件
│   │   └── YourProject.Web.csproj
│   │
│   ├── YourProject.Application/            # 应用层（可选，大型项目）
│   │   ├── Services/
│   │   ├── DTOs/
│   │   ├── Interfaces/
│   │   └── YourProject.Application.csproj
│   │
│   ├── YourProject.Domain/                 # 领域层（可选，大型项目）
│   │   ├── Entities/
│   │   ├── ValueObjects/
│   │   ├── Enums/
│   │   └── YourProject.Domain.csproj
│   │
│   └── YourProject.Infrastructure/         # 基础设施层（可选）
│       ├── Data/
│       ├── Services/
│       └── YourProject.Infrastructure.csproj
├── tests/
│   ├── YourProject.UnitTests/
│   └── YourProject.IntegrationTests/
├── doc/
│   ├── devlog/
│   ├── design/
│   └── requirement.md
├── docker-compose.yml
├── Dockerfile
├── .env.example
├── .gitignore
├── AGENTS.md
├── CLAUDE.md
├── GEMINI.md
├── design-system.yaml
└── TASK.md
```

---

## 开发流程

### 总体阶段划分

由于 Blazor Server 是前后端统一框架，开发流程相比前后端分离更简洁：

```
阶段 0          阶段 1
设计文档   →   统一实现
（用户确认）   （Blazor Server）
```

**例外情况**（AI 自动判断，无需走阶段 0）：
- Bug 修复（功能行为不变，只修正错误）
- 已有页面的样式、文案微调
- 配置、环境变量、部署脚本调整
- 单个字段的增删（不涉及新页面或新业务流程）

---

### 阶段 0 — 设计文档（新功能强制前置）

**触发条件**：用户提出新功能需求

**执行方式**：由用户与 AI 对话协作产出，用户最终确认定稿

**产物**：`doc/design/设计文档.md`（全项目一份，新功能以新章节追加，不新建文件）

**文档必须包含以下四个部分**：

| 部分     | 内容要求                                                       |
| -------- | -------------------------------------------------------------- |
| 架构设计 | 模块划分、数据流向、涉及的技术决策说明                         |
| 功能需求 | 页面列表、功能点明细、业务规则、权限矩阵                       |
| 界面原型 | 每个页面的 ASCII 线框图，含关键状态（loading / empty / error） |
| 交互说明 | 操作流程、状态流转、边界场景、错误处理方式                     |

**门控规则**：
- 用户明确确认设计文档后，才允许进入阶段 1
- AI 在此阶段只输出文档内容，不写任何实现代码

---

### 阶段 1 — 统一实现（Blazor Server）

**触发条件**：用户发出「开始开发」指令

**入场要求**：阶段 0 设计文档已由用户确认

**工作内容**：
1. 按设计文档实现 Razor 页面和组件，遵循 `design-system.yaml` 和 BootstrapBlazor 规范
2. 同步实现服务层逻辑和数据访问
3. 数据层直接使用真实服务，不需要 mock
4. 涉及数据库结构变更时，使用 Azrng.SqlMigration 生成迁移脚本（如使用数据库）

**门控规则**：
- 核心业务路径 smoke test 通过
- `doc/devlog/` 已补充本阶段记录

---

### 独立 API 接口（如需要）

如果项目需要对外提供 RESTful API（如移动端调用），在 Blazor Server 项目中添加 `Controllers/` 目录：
- 使用 `[ApiController]` 特性
- 统一使用 `ResultModel<T>` 包装响应
- 遵循后端规则中的 Controller 层规范

---

## 使用方式
每个 AI Agent 在开始修改前都必须：
1. 先阅读本文档，理解核心规则和流程
2. 确认当前处于哪个开发阶段，检查该阶段的入场条件是否满足
3. 再阅读 `design-system.yaml`
4. 再阅读相关模块和现有实现
5. 优先复用当前结构，再决定是否新增文件

进行 Blazor 开发时：
1. `design-system.yaml` 是颜色、间距、圆角、排版、页面状态、组件使用规则的参考依据
2. 优先使用 BootstrapBlazor 组件，样式使用 Bootstrap CSS 变量和自定义主题
3. 图标统一使用 Bootstrap Icons
4. 全局状态使用 Scoped 服务，组件本地状态使用 `@bind` 和 `@code` 块

进行服务层开发时：
1. 服务类实现 `ITransientDependency`/`IScopedDependency`/`ISingletonDependency` 接口
2. 统一响应使用 `ResultModel<T>` 包装（API 接口场景）
3. 异常处理使用 Azrng 异常体系

进行部署改动时：
1. 优先复用现有 Dockerfile 和 Compose 链路
2. 镜像 tag 必须可追踪
3. 清理旧镜像前要保留可回滚版本

---

## 核心规则
- 先理解，再修改。
- 先复用，再新增。
- 交付必须可直接使用，不能只停留在演示层。
- 不允许新增平行配置体系。
- 不做与当前任务无关的重构。
- 不要猜测问题，要实证排查：通过日志、断点、数据等实际证据定位原因，禁止凭猜测修改代码。
- 所有改动都必须可说明、可验证。

## 思考与实现原则
- 先理解业务目标、用户角色、核心流程，再进行设计和开发。
- 优先选择符合当前需求的最优可行方案，兼顾正确性、可维护性与实现成本。
- 尽可能复用现有代码、结构与组件；只有在明确存在复用价值时才新增抽象。
- 严禁过度设计、过度封装和无意义冗余，避免为单次需求引入不必要的层次和复杂度。
- 代码实现应保持清晰、简洁、稳定，优先保证业务正确性、可读性和后续维护性。
- 遇到需求不清、规则冲突或实现复杂度明显升高时，先说明判断与权衡，再继续执行。

## 编码规则
- 所有源码、配置、文档文件统一使用 `UTF-8` 编码。
- 读取或修改含中文文件时，若出现乱码，先判断是终端显示问题还是文件编码损坏；未确认前禁止覆盖原文件。
- 禁止使用可能隐式改变编码的方式直接改写源码文件，如 shell 重定向、`Out-File`、`Set-Content`。
- Windows / PowerShell 下读取中文文件时，必须显式使用 `UTF-8`。
- 修改含中文内容后，必须重新读取一次并确认关键中文显示正常。
---

## Blazor 前端规则

### Razor 组件规范
- 页面组件使用 `@page` 指令定义路由
- 组件代码使用 `@code { }` 块，复杂逻辑提取到 `@inject` 的服务中
- 组件参数使用 `[Parameter]` 特性标注
- 事件回调使用 `[Parameter] public EventCallback<T> OnXxx { get; set; }`
- 组件命名使用 PascalCase，文件名与类名一致

### 组件通信规范
- **父传子**：使用 `[Parameter]` 属性，只读不可修改
- **子传父**：使用 `EventCallback<T>`
- **跨级传值**：使用 `CascadingValue` / `CascadingParameter`
- **全局状态**：使用 Scoped 服务注入
- 避免直接修改参数，需要双向绑定时使用 `@bind-Value`

### 路由管理规范
- 路由通过 `@page` 指令在页面组件顶部声明
- 路由参数使用 `@page "/users/{id:int}"`
- 导航使用 `NavigationManager.NavigateTo()`
- 路由守卫通过 `AuthorizeView` 和 `[Authorize]` 特性实现
- 路由命名使用有意义的路径（如 `/users/list`、`/users/detail/{id}`）

### 状态管理规范
- **组件内状态**：`@code` 块中的局部变量和 `@bind` 双向绑定
- **页面间状态**：Scoped 服务（如 `UserStateService`）
- **全局共享状态**：Singleton 服务（如配置、主题）
- 修改共享状态必须通过服务方法，禁止直接修改
- 异步操作使用 `async Task` 方法，UI 更新通过 `StateHasChanged()` 或 `InvokeAsync(StateHasChanged)`

### 样式规则
- 优先使用 BootstrapBlazor 组件内置样式和 class
- 自定义样式使用 CSS 变量和 `wwwroot/css/app.css`
- 禁止 inline style，禁止硬编码颜色值（如 `#1A90FF`）
- 主题覆盖统一在 `wwwroot/css/theme.css`

### 组件规则
- 优先复用 `Components/` 下已有组件，禁止重复创建
- 只有确实有复用价值时才新增共享组件，避免为单次需求过度抽象
- 页面状态必须完整：`loading`、`empty`、`error`、`no-permission`
- 除非仓库已在使用，否则不要引入新的组件库或样式体系

### 服务调用规则
- 页面组件通过 `@inject` 注入服务
- 服务层封装业务逻辑，组件层只负责 UI 交互
- 异步操作使用 `await`，禁止同步阻塞
- 服务方法命名规范：`GetXxxAsync`、`CreateXxxAsync`、`UpdateXxxAsync`、`DeleteXxxAsync`

---

## 后端规则（服务层）

### 分层架构规则
```
Web (Blazor Server) → Application → Domain ← Infrastructure
```
- 小型项目可合并到 `Web` 项目中，按目录分层
- 大型项目建议拆分为独立类库
- `Domain` 层：无外部依赖，包含核心业务模型
- `Application` 层：依赖 Domain，定义服务接口和 DTO
- `Infrastructure` 层：依赖 Application 和 Domain，实现具体技术细节

### Controller 层规则（如提供 API 接口）
- Controller 只负责 HTTP 处理，不包含业务逻辑
- 使用 `[ApiController]` 特性，自动处理 400 响应
- 使用 `[Route("api/[controller]")]` 统一路由前缀
- 注入 Application 层的服务接口，不直接依赖 Infrastructure
- 统一使用 `ActionResult<T>` 或 `IActionResult` 返回类型

### Service 层规则
- 服务类实现接口，接口定义在 `Interfaces/` 目录
- 服务方法必须是异步的（返回 `Task<T>`）
- 使用 DTO 进行数据传递，不直接暴露 Domain 实体
- 业务异常抛出继承自 `BaseException` 的自定义异常

### Domain 层规则
- 实体类继承自基类（包含 Id、创建时间等公共属性）
- 值对象使用 record 类型，实现值相等比较
- 禁止依赖外部库，保持纯净

### 数据访问规则（可选）
- 使用 Entity Framework Core 进行数据访问
- DbContext 通过构造函数注入
- 使用 LINQ 查询，避免原生 SQL
- 涉及事务时使用 `IDbContextTransaction`
- 软删除通过全局查询过滤器实现

### 统一响应格式
- 所有 API 响应统一使用 `ResultModel<T>` 包装
- 成功响应：`ResultModel<T>.Success(data)`
- 错误响应：`ResultModel<T>.Failure(message, errorCode)`
- 状态码：200 表示成功，其他表示各类错误
- 异常处理由 Azrng.AspNetCore.Core 中间件统一捕获

### 依赖注入规范
- 服务类必须实现以下接口之一：
  - `ITransientDependency`：每次请求创建新实例
  - `IScopedDependency`：每个 SignalR 连接作用域内单例
  - `ISingletonDependency`：应用程序生命周期单例
- Blazor 组件通过 `@inject` 注入服务
- 禁止使用静态服务定位器 pattern

### 异常处理规范
- 业务异常继承 `BaseException` 或其子类：
  - `LogicBusinessException`：业务逻辑异常
  - `ParameterException`：参数校验异常
  - `NotFoundException`：资源不存在
  - `ForbiddenException`：禁止访问
  - `InternalServerException`：服务器内部错误
- Blazor 页面使用 `ErrorBoundary` 组件捕获渲染异常
- API 接口异常由中间件统一捕获并转换为 `ResultModel` 响应

### Redis 缓存使用规范（可选）
- 使用 `Common.Cache.Redis` 封装的 `IRedisProvider` 接口
- 通过 `services.AddRedisCacheStore()` 注册 Redis 服务
- 缓存操作必须设置合理的过期时间
- 禁止在循环中频繁调用 Redis

### 数据库迁移规则（可选）
- 使用 `Azrng.SqlMigration` 进行数据库脚本迁移
- 迁移脚本放在 `Migrations/` 目录，按版本号命名
- 迁移脚本必须幂等，可重复执行
- 回滚脚本必须与正向脚本配套提供

---

## 测试规则

### 总体要求
- 影响行为的改动应优先补充或更新测试
- 若本次改动未补测试，必须在最终说明中写明原因和风险
- 测试应覆盖真实业务行为

### 后端测试
- 单元测试使用 xUnit + Moq
- Service 测试：验证业务逻辑分支，使用 mock 隔离依赖
- Repository 测试：使用 In-Memory 数据库或 Testcontainers
- 集成测试：使用 WebApplicationFactory 测试完整请求管道

### Blazor 组件测试
- 使用 bUnit 进行组件测试（如需要）
- 测试组件渲染输出、用户交互、参数传递

### 外部依赖与数据
- 测试中不要真实调用第三方服务，统一使用 mock
- 测试数据应尽量最小化、可读、可重复执行
- 不要让测试依赖本地人工状态

### 无法执行测试时
- 必须说明未执行的测试类型
- 必须说明未执行原因
- 必须说明潜在影响范围和风险

---

## Git 与提交流程

### 分支命名
- `feat/<desc>`：新功能
- `fix/<desc>`：缺陷修复
- `refactor/<desc>`：无行为变更的重构
- `chore/<desc>`：工具、依赖、配置调整
- `docs/<desc>`：仅文档变更

### Commit 规范
- 提交信息优先采用 Conventional Commits：
  - `<type>(<scope>): <简短描述>`
- 示例：
  - `feat(auth): add login page with BootstrapBlazor`
  - `fix(table): correct pagination reset`
  - `chore(dotnet): pin NuGet package version`

### 交付前自查
- 编译（`dotnet build`）、测试（`dotnet test`）
- 涉及数据库结构变更时已确认迁移脚本
- 新增环境变量已同步更新相关文档
- 涉及 Docker 时已确认构建或部署链路

### 提交触发规则
- 每次修改完后都必须提交代码
- 若本次开发生成了本地运行产物、缓存、测试数据库、构建产物或依赖目录，应同步检查并更新 `.gitignore`

---

## Docker 规范

### 总体要求
- 优先复用现有 Docker 与 Compose 文件
- 生产镜像优先采用多阶段构建
- 不要把密钥写入镜像
- 部署链路要可追踪
- 清理旧镜像时要保留最小回滚窗口

### Docker 交付规则
- 只要本次任务新增或修改了 Dockerfile、`docker-compose.yml`、运行端口、环境变量或启动命令，完成实现后应主动执行一次镜像构建和容器化启动验证；若因环境限制无法执行，必须明确说明
- 容器化验证至少包括：
  - 镜像构建是否成功
  - Compose 服务是否成功启动
  - 关键服务是否处于可用状态
  - Blazor 应用核心访问链路是否可用
- 若发现宿主机端口冲突，不要直接停止未知本地进程；优先调整 Compose 映射端口，并同步更新文档
- 对于依赖数据库或其他基础服务的容器，Compose 中应尽量补充健康检查或等价的启动等待策略
- 新增镜像构建链路时，应同步补充 `.dockerignore`

---

## 任务管理机制
`TASK.md` 是唯一任务记录文件，用于记录具体任务内容，不负责承载规则解释。

### 任务状态
- `TODO`：未开始
- `DOING`：进行中
- `BLOCKED`：被阻塞
- `REVIEW`：已完成开发，等待确认
- `DONE`：已完成

### 执行规则
1. 开始工作前，先查看 `TASK.md` 是否已有对应任务。
2. 如果已有任务，优先更新原任务，不重复新建。
3. 如果没有任务，在 `TASK.md` 中新增最小任务记录。
4. 开始执行时，将任务状态更新为 `DOING`。
5. 若任务无法继续，更新为 `BLOCKED` 并写明原因。
6. 开发完成但仍需确认时，更新为 `REVIEW`。
7. 验证完成并确认交付后，更新为 `DONE`。

### 每个任务至少包含
- 任务 ID
- 任务名称
- 任务目标
- 当前阶段
- 负责人 AI
- 任务状态
- 优先级
- 最近更新时间

### AI Agent 要求
- 开始任务前先同步任务状态。
- 开发过程中及时更新 `TASK.md`。
- 范围变化时同步补充任务说明、验收标准和风险。
- 每次开发完成后，都需要在 `doc/devlog/` 下新增一份本次开发简短说明的 Markdown 记录。
---

## 设计系统文件规范
`design-system.yaml` 是唯一设计系统文件，包含：
- 品牌与产品风格意图
- 语义化颜色 token
- 排版 token
- 间距、圆角、阴影 token
- 布局与断点规则
- BootstrapBlazor 主题配置映射
- 页面状态规范
- 组件使用规则
- 明确的 `do / dont` 规则

---

## 文档更新要求
以下内容发生变化时，必须同步更新相关文档：
- 命令、脚本或启动方式
- 环境变量
- 配置项
- 构建、发布或部署流程
- 数据库结构、迁移或初始化方式
- 对外使用方式或接口约定

优先更新已有文档，不新增重复说明。
每次开发完成后，必须在 `doc/devlog/` 下补充一份简短开发记录。

### 文档目录约定
- `doc/requirement.md`：需求文档。若文件已存在，AI 应基于用户新增需求进行补充或修订；若文件不存在，AI 必须根据用户输入先自动生成详细初稿。该文件仅承载业务需求，不写入设计说明或开发过程记录
- `doc/design/设计文档.md`：项目设计文档，全项目一份。包含架构设计、功能需求、界面原型（如适用）与交互说明。由用户与 AI 协作产出，用户确认后作为开发的唯一需求基准。AI 可在文件中追加新功能章节，不删除已有内容，不新建平行文件
- `doc/devlog/`：每次开发完成后的简短开发日志

### requirement 要求
- 若 `doc/requirement.md` 为模板初始状态，AI 应根据用户输入将占位内容替换为项目真实需求，而不是长期保留“待补充”空壳。
- 需求文档建议固定包含以下章节：项目背景、目标与范围、用户角色、页面或模块清单、核心业务流程、关键数据与字段、权限与可见范围、边界场景与异常、非功能要求、待确认项。
- 自动生成需求文档时，优先写清业务事实、流程与边界，不提前写入技术实现方案。

### devlog 要求
- 文件目录：`doc/devlog/`
- 文件格式：Markdown
- 文件命名建议：`YYYY-MM-DD-HH-mm-ss-中文任务简述.md`
- 文件名中的任务简述必须使用中文，不使用英文 slug、拼音缩写或中英混杂描述
- 内容保持简短，至少包含：
  - 本次目标
  - 核心改动
  - 修改文件
  - 校验情况
  - 风险或遗留项
---

## 交付收口规范
- 当用户请求“开始开发”而非“只出方案”时，AI Agent 在实现完成后应默认继续做交付收口。
- 交付收口至少应按实际情况依次检查：
  1. 编译 / 测试
  2. 构建
  3. 发布构建与启动验证
  4. 应用级 smoke test
  5. 文档、`TASK.md`、`doc/devlog/`、忽略文件更新
- 若某项验证未执行，最终输出必须明确说明未执行项、原因、影响范围和建议下一步。
---

## 常见禁止事项
- 禁止在 Razor 组件的 `@code` 块中编写复杂业务逻辑，应提取到服务层
- 禁止在 Controller 层编写业务逻辑，应放入 Application 层
- 禁止修改数据模型后不补迁移脚本（使用数据库时）
- 禁止在异步代码中使用阻塞式等待（.Result / .Wait()）
- 禁止绕过既有提交校验流程，如 `git commit --no-verify`
- 禁止使用 inline style
- 禁止硬编码颜色值（如 `#1A90FF`）、间距值
- 禁止在 Domain 层依赖外部库
- 禁止使用非 Bootstrap Icons 以外的图标库
- 禁止直接操作 Redis，必须通过 IRedisProvider 接口（使用 Redis 时）
- 禁止手动配置依赖注入，应使用标记接口方式（ITransientDependency/IScopedDependency/ISingletonDependency）
- 禁止返回非 ResultModel 包装的响应格式（API 接口场景）
- 禁止抛出非 Azrng 体系的自定义异常
- 禁止在 Blazor Server 中使用 JSInterop 调用 jQuery 操作 DOM（使用 Blazor 原生方式）

---

## 交付输出要求
最终输出优先使用中文，并至少说明：
- 本次改了什么
- 核心实现方式
- 修改了哪些文件
- 已执行和未执行的校验
- 当前任务状态
- 风险、阻塞或假设
- 是否已更新 `doc/devlog/`

---

文件结束。
