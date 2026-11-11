// Toast 注册/activation 骨架（文档 15.2）：通知是提示，数据库卡片才是事实源。
// 合同：
//   - cardID 是首次发送幂等身份，revision 是更新身份；不含 cardID 的通知不构成可追踪通知。
//   - toast 只携带可公开摘要与 cardID，高风险批准不带 bearer token 进参数。
//   - 注册/激活、后台激活、勿扰、Narrator 是 Windows App SDK 运行时项，真机 NOT_RUN。
using System.Runtime.Versioning;

namespace WinHost;

/// <summary>一条要发给桌面的提示。标题/摘要是 Agent 写的字，永远当数据，不当脚本。</summary>
[SupportedOSPlatform("windows")]
public sealed record Toast(string CardId, int Revision, string Title, string Summary);

/// <summary>通知的注册/activation 契约；真实弹出需要 Windows App SDK，这里只落地骨架与自检。</summary>
[SupportedOSPlatform("windows")]
public static class Notifications
{
    // AUMID：Windows App SDK 用它在系统中定位这个应用的通知注册。
    public static string AppId(string installUuid) => $"agent-autoloop.{installUuid}";

    /// <summary>activation 参数：cardID 幂等身份、revision 更新身份，标题/摘要作独立参数传递。</summary>
    public static string[] ToastCommand(string appId, Toast toast) => new[]
    {
        "--app-id",
        appId,
        "--card-id",
        toast.CardId,
        "--revision",
        toast.Revision.ToString(),
        "--title",
        toast.Title,
        "--summary",
        toast.Summary,
    };
}
