# AGENTS.md

## 适用范围

- 作用域：服务层、领域逻辑、数据访问、独立 API、缓存与后端测试
- 触发场景：涉及服务实现、DTO、数据访问、独立 API、缓存、异常处理、后端测试时阅读

---

## 技术栈

### 后端服务层
- ASP.NET Core Web API（如需独立 API 接口）
- Clean Architecture 分层架构：
  - `API`（表现层）→ `Application`（应用层）→ `Domain`（领域层）← `Infrastructure`（基础设施层）
- ORM：Entity Framework Core（可选）
- 测试框架：xUnit + Moq

### 核心库
- `Azrng.Core`（1.15.8）：基础类库（实体基类、扩展方法、工具类、结果包装、异常体系）
- `Azrng.Core.Json`（1.3.1）：JSON 序列化（基于 `System.Text.Json`）
- `Azrng.AspNetCore.Core`（1.3.0）：API 基础设施（统一响应、模型校验、异常中间件）
- `Azrng.SqlMigration`（0.4.0）：SQL 脚本迁移执行（可选）
- `Common.Cache.Redis`（2.0.0）：Redis 缓存封装（基于 StackExchange.Redis，可选）

### 依赖注入规范
- 使用 Azrng 标记接口方式：
  - `ITransientDependency`：临时注入
  - `IScopedDependency`：作用域注入
  - `ISingletonDependency`：单例注入
- 在 `Program.cs` 中调用 `services.RegisterBusinessServices(assemblies)` 扫描并注册服务
- Blazor Server 中 Scoped 服务基于 SignalR 连接生命周期

如果仓库已经有真实实现，以现有代码为准，不要强行重构或替换技术栈。

---

## 推荐目录结构

```text
src/
├── YourProject.Web/
├── YourProject.Application/
│   ├── Services/
│   ├── DTOs/
│   └── Interfaces/
├── YourProject.Domain/
│   ├── Entities/
│   ├── ValueObjects/
│   └── Enums/
└── YourProject.Infrastructure/
    ├── Data/
    └── Services/
```

---

## 阶段 2 — 服务逻辑与数据访问

**触发条件**：用户发出「开始开发」或「开始服务层开发」指令

**入场要求**：阶段 0 设计文档已由用户确认；若界面已先行搭建，相关页面状态和交互入口已明确

**工作内容**：
1. 实现服务层逻辑、数据访问和独立 API（如需要）。
2. 保持服务层、领域层、基础设施层职责清晰，不把复杂业务逻辑塞回 Razor 组件。
3. 所有增删改必须真实生效，并能通过页面、Swagger 或测试验证。
4. 涉及数据库结构变更时，使用 `Azrng.SqlMigration` 生成迁移脚本（如使用数据库）。

**门控规则**：
- 核心业务路径 smoke test 通过。
- 关键服务逻辑已补测试或在交付说明中解释未补原因。

---

## 后端规则

### 分层架构规则
```text
Web (Blazor Server) → Application → Domain ← Infrastructure
```
- 小型项目可合并到 `Web` 项目中，按目录分层。
- 大型项目建议拆分为独立类库。
- `Domain` 层：无外部依赖，包含核心业务模型。
- `Application` 层：依赖 Domain，定义服务接口和 DTO。
- `Infrastructure` 层：依赖 Application 和 Domain，实现具体技术细节。

### Controller 层规则（如提供 API 接口）
- Controller 只负责 HTTP 处理，不包含业务逻辑。
- 使用 `[ApiController]` 特性，自动处理 400 响应。
- 使用 `[Route("api/[controller]")]` 统一路由前缀。
- 注入 Application 层的服务接口，不直接依赖 Infrastructure。
- 统一使用 `ActionResult<T>` 或 `IActionResult` 返回类型。

### Service 层规则
- 服务类实现接口，接口定义在 `Interfaces/` 目录。
- 服务方法必须是异步的（返回 `Task<T>`）。
- 使用 DTO 进行数据传递，不直接暴露 Domain 实体。
- 业务异常抛出继承自 `BaseException` 的自定义异常。

### Domain 层规则
- 实体类继承自基类（包含 Id、创建时间等公共属性）。
- 值对象使用 record 类型，实现值相等比较。
- 禁止依赖外部库，保持纯净。

### 数据访问规则（可选）
- 使用 Entity Framework Core 进行数据访问。
- DbContext 通过构造函数注入。
- 使用 LINQ 查询，避免原生 SQL。
- 涉及事务时使用 `IDbContextTransaction`。
- 软删除通过全局查询过滤器实现。

### 统一响应格式
- 所有 API 响应统一使用 `ResultModel<T>` 包装。
- 成功响应：`ResultModel<T>.Success(data)`。
- 错误响应：`ResultModel<T>.Failure(message, errorCode)`。
- 状态码：200 表示成功，其他表示各类错误。
- 异常处理由 `Azrng.AspNetCore.Core` 中间件统一捕获。

### 依赖注入规范
- 服务类必须实现以下接口之一：
  - `ITransientDependency`：每次请求创建新实例
  - `IScopedDependency`：每个 SignalR 连接作用域内单例
  - `ISingletonDependency`：应用程序生命周期单例
- Blazor 组件通过 `@inject` 注入服务。
- 禁止使用静态服务定位器 pattern。

### 异常处理规范
- 业务异常继承 `BaseException` 或其子类：
  - `LogicBusinessException`：业务逻辑异常
  - `ParameterException`：参数校验异常
  - `NotFoundException`：资源不存在
  - `ForbiddenException`：禁止访问
  - `InternalServerException`：服务器内部错误
- Blazor 页面使用 `ErrorBoundary` 组件捕获渲染异常。
- API 接口异常由中间件统一捕获并转换为 `ResultModel` 响应。

### Redis 缓存使用规范（可选）
- 使用 `Common.Cache.Redis` 封装的 `IRedisProvider` 接口。
- 通过 `services.AddRedisCacheStore()` 注册 Redis 服务。
- 缓存操作必须设置合理的过期时间。
- 禁止在循环中频繁调用 Redis。

### 数据库迁移规则（可选）
- 使用 `Azrng.SqlMigration` 进行数据库脚本迁移。
- 迁移脚本放在 `Migrations/` 目录，按版本号命名。
- 迁移脚本必须幂等，可重复执行。
- 回滚脚本必须与正向脚本配套提供。

---

## 测试规则

### 总体要求
- 影响行为的改动应优先补充或更新测试。
- 若本次改动未补测试，必须在最终说明中写明原因和风险。
- 测试应覆盖真实业务行为。

### 后端测试
- 单元测试使用 xUnit + Moq。
- Service 测试：验证业务逻辑分支，使用 mock 隔离依赖。
- Repository 测试：使用 In-Memory 数据库或 Testcontainers。
- 集成测试：使用 `WebApplicationFactory` 测试完整请求管道。

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
