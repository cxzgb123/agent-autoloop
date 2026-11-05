// 执行账本（文档 9.1/12）：记录一次 launch 的启动与回收，区分「还在跑 / 已退 / 启动失败」。
// 这是内存账本的最小切片，只证明状态可区分；持久化与 Hub 重启对账（reconciliation）是后续阶段。
// 对账的关键：同 launchID 只记一棵进程树；启动失败也留记录，不静默消失。
namespace WinHost;

public enum ExecutionState { Running, Exited, LaunchFailed }

public sealed record ExecutionRecord(long LaunchId, ProcessIdentity? Identity, ExecutionState State, int ExitCode, string? Error);

/// <summary>按 launchID 记启动/退出/失败。PID 不进键，只用身份指纹核对。</summary>
public sealed class ExecutionLedger
{
    private readonly Dictionary<long, ExecutionRecord> _records = new();

    public IReadOnlyCollection<ExecutionRecord> Records => _records.Values;

    public void RecordLaunch(ProcessIdentity identity)
        => _records[identity.LaunchId] = new(identity.LaunchId, identity, ExecutionState.Running, -1, null);

    public void RecordExit(long launchId, int exitCode)
    {
        if (_records.TryGetValue(launchId, out var r))
            _records[launchId] = r with { State = ExecutionState.Exited, ExitCode = exitCode };
    }

    public void RecordLaunchFailed(long launchId, string error)
        => _records[launchId] = new(launchId, null, ExecutionState.LaunchFailed, -1, error);

    public ExecutionState StateOf(long launchId)
        => _records.TryGetValue(launchId, out var r) ? r.State : ExecutionState.LaunchFailed;
}
