# Weixin Bot Sample

基于 [微信 iLink API](https://ilinkai.weixin.qq.com) 的机器人示例项目，展示扫码登录、消息收发、自动回复等核心功能。

## 技术栈

- **.NET 10** + Blazor Server
- [BootstrapBlazor](https://www.blazor.zone/) - 企业级 UI 组件库
- [QRCoder](https://github.com/codebude/QRCoder) - 二维码生成

## 功能特性

- 扫码登录绑定微信账号
- 轮询接收消息并记录
- 自动回复与消息推送
- 会话状态管理
- 实时操作日志

## 快速开始

```bash
# 恢复依赖
dotnet restore

# 运行项目
dotnet run --project src/WeixinBotSample
```

访问 `https://localhost:5001` 即可使用。

## 配置说明

在应用界面中配置以下参数：

| 参数 | 说明 |
|------|------|
| BaseUrl | iLink API 地址（默认：`https://ilinkai.weixin.qq.com`） |
| RouteTag | 路由标签 |
| Token | 认证令牌 |
| AccountId | 账户 ID |
| UserId | 用户 ID |

## 项目结构

```
src/WeixinBotSample/
├── Components/Pages/       # Blazor 页面
├── Models/                 # 数据模型
├── Services/               # 业务服务
│   ├── WeixinBotDemoService.cs      # 核心服务
│   ├── WeixinBotDemoService.Binding.cs   # 绑定会话
│   ├── WeixinBotDemoService.Core.cs      # 核心逻辑
│   ├── WeixinBotDemoService.Polling.cs   # 消息轮询
│   └── WeixinBotDemoService.Transport.cs # HTTP 传输
```