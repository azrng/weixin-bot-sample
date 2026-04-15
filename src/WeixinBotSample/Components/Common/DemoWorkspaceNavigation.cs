using Microsoft.AspNetCore.Components.Routing;

namespace WeixinBotSample.Components.Common;

public static class DemoWorkspaceNavigation
{
    public static IReadOnlyList<DemoWorkspaceShell.WorkspaceNavItem> Home { get; } =
    [
        new("/", "01", "总览", "绑定、监听、配置与能力矩阵", NavLinkMatch.All),
        new("/messages", "02", "消息", "联系人、主动推送、入站记录与日志"),
        new("/media", "03", "媒体", "上传、发送、下载回读与技术细节"),
        new("/checklist", "04", "联调", "真实账号检查项与环境证据"),
    ];

    public static IReadOnlyList<DemoWorkspaceShell.WorkspaceNavItem> Messages { get; } =
    [
        new("/", "01", "总览", "绑定、监听、配置与能力矩阵"),
        new("/messages", "02", "消息", "联系人、主动推送、入站记录与日志", NavLinkMatch.All),
        new("/media", "03", "媒体", "上传、发送、下载回读与技术细节"),
        new("/checklist", "04", "联调", "真实账号检查项与环境证据"),
    ];

    public static IReadOnlyList<DemoWorkspaceShell.WorkspaceNavItem> Media { get; } =
    [
        new("/", "01", "总览", "绑定、监听、配置与能力矩阵"),
        new("/messages", "02", "消息", "联系人、主动推送、入站记录与日志"),
        new("/media", "03", "媒体", "上传、发送、下载回读与技术细节", NavLinkMatch.All),
        new("/checklist", "04", "联调", "真实账号检查项与环境证据"),
    ];

    public static IReadOnlyList<DemoWorkspaceShell.WorkspaceNavItem> Checklist { get; } =
    [
        new("/", "01", "总览", "绑定、监听、配置与能力矩阵"),
        new("/messages", "02", "消息", "联系人、主动推送、入站记录与日志"),
        new("/media", "03", "媒体", "上传、发送、下载回读与技术细节"),
        new("/checklist", "04", "联调", "真实账号检查项与环境证据", NavLinkMatch.All),
    ];
}
