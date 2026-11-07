// 最小管理页 + 托盘（文档 15.1）：只消费状态，不决定业务，不解释模型内容。
// 合同：
//   - 状态只有四种：running / paused / needs_decision / error，由外部 status provider 给，这里不决定业务。
//   - 页面只显示文本，任何「别人写的字」（Agent 标题、项目名）都当普通文本渲染，绝不解释成标记或指令。
//   - tray-selftest 只构造页面与托盘标题，不 Show；高 DPI、Narrator、键盘导航、高对比、RTL 是桌面会话项，真机 NOT_RUN。
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace WinHost;

[SupportedOSPlatform("windows")]
public enum TrayStatus
{
    Running,
    Paused,
    NeedsDecision,
    Error,
}

/// <summary>托盘与最小管理页：只反映状态，不决定谁在哪、用什么权限。</summary>
[SupportedOSPlatform("windows")]
public static class TrayContext
{
    public static string StatusText(TrayStatus status) => status switch
    {
        TrayStatus.Running => "运行中",
        TrayStatus.Paused => "已暂停",
        TrayStatus.NeedsDecision => "需要决定",
        TrayStatus.Error => "错误",
        _ => "未知",
    };

    /// <summary>托盘图标骨架：只给四态标题；真正显示需要桌面会话与消息循环，真机 NOT_RUN。</summary>
    public static NotifyIcon CreateIcon(TrayStatus status) => new()
    {
        Text = StatusText(status),
        Icon = System.Drawing.SystemIcons.Application,
        Visible = false,
    };

    /// <summary>管理页：状态 + 启动/停止/刷新。状态文本作普通文本渲染，不解释成标记。</summary>
    public static Form CreatePage(Func<TrayStatus> status, Action onStart, Action onStop)
    {
        var form = new Form { Text = "Agent 中台", Width = 560, Height = 360, StartPosition = FormStartPosition.CenterScreen };
        var state = new Label { Left = 16, Top = 16, Width = 500, Text = "当前状态：" + StatusText(status()) };
        var start = new Button { Left = 16, Top = 56, Width = 100, Text = "启动" };
        var stop = new Button { Left = 128, Top = 56, Width = 100, Text = "停止" };
        var refresh = new Button { Left = 240, Top = 56, Width = 100, Text = "刷新" };
        start.Click += (_, _) =>
        {
            onStart();
            state.Text = "当前状态：" + StatusText(status());
        };
        stop.Click += (_, _) =>
        {
            onStop();
            state.Text = "当前状态：" + StatusText(status());
        };
        refresh.Click += (_, _) => state.Text = "当前状态：" + StatusText(status());
        form.Controls.AddRange(new Control[] { state, start, stop, refresh });
        return form;
    }
}
