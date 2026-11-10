# Windows Phase 0：Job Object 监督闭环（技术尖峰）

本目录是方案文档（`docs/claude/agent-autoloop-windows-design-2026-11-04.md`）第 17 节「阶段 0」的最小可运行切片。

## 本次做了什么（NOT_RUN 之外的部分）

证明方案的核心技术假设成立：**用 Win32 Job Object 监督 agent 进程树**，这是文档第 8 节整个 Windows 监督层的根基。

| 交付 | 内容 | 对应文档 | 状态 |
|---|---|---|---|
| `mock-agent.mjs` | 零依赖假 Agent，normal/hang 两种模式，子进程继承 Job | 文档 16 节 `tests/fixtures/mock-agent.mjs` | 已运行验证 |
| `WinHost/ProcessSupervisor.cs` | `CreateProcessW(CREATE_SUSPENDED)` → `AssignProcessToJobObject(KILL_ON_JOB_CLOSE)` → `ResumeThread`，任一步失败 fail closed 且回收自己持有的句柄，绝不 taskkill 任意 PID | 文档 8.2 / 8.3 | 已运行验证 |
| `WinHost/Program.cs` | `rounds`（两轮闭环）/ `hang`（进程树清理）/ `pipe`（Named Pipe 控制面）/ `selftest` 四个动作 | 文档 462 行退出条件 | 已运行验证 |
| `WinHost/Native.cs` | `NtQueryInformationProcess` 读父 PID（Phase 0 粗证用） | — | 已运行验证 |
| `WinHost/ControlPipe.cs` | Named Pipe 入口：长度前缀协议、4KB 上限、单连接、方法白名单 `ping`/`status`、管道名带 installUUID、SDDL 拒绝网络/匿名 | 文档 10.1 | 白名单/长度/连接已运行验证 |

## 验证结果

```
=== 两轮闭环 ===
[round 1] launchID=... pid=... started
agent done: run=1 child exit 0
[round 1] 闭环成功
[round 2] ... 闭环成功
Phase 0 两轮闭环 PASS

=== 进程树清理 ===
[hang] 进程树内子进程数 = 1（期望 >= 1）
[hang] TerminateJobObject 后：父存活=False，子存活=False
Phase 0 进程树清理 PASS（KILL_ON_JOB_CLOSE + TerminateJobObject）
```

复现：`windows\verify-phase0.cmd`（需本机已装 .NET 10 SDK 与 node ≥ 24）。

## 明确 NOT_RUN（文档要求、本机无法伪造的项）

1. **runtime 打包的干净 VM 证据**：文档 462 行退出条件要求「runtime 打包方案有干净 Windows 11 VM 证据」，且第 7 节第 4 点强调 `.NET` 部署（framework-dependent / self-contained / MSIX）与 Windows App SDK runtime 必须分别在干净 VM 验证。本机只验证了 framework-dependent（`dotnet winhost.dll` 运行）。**self-contained 打包与干净 VM 安装未做。**
2. **Named Pipe 的 DACL 实机施加与「非预期身份被拒」**：`ControlPipe.SecuritySddl()` 给出只允许当前用户、拒绝网络/匿名的 SDDL 字符串（含 `(D;;GA;;;NU)` 与 `(D;;GA;;;AN)`），并在 `pipe` selftest 里断言存在。但真正把 DACL 施加到管道需要 `CreateNamedPipeW` 的 P/Invoke 携带 `SECURITY_ATTRIBUTES`，且「非预期身份被拒」必须用第二个 Windows 账号来连；本机只有一个账号，**这两个都记 NOT_RUN**。方法白名单、4KB 长度上限、单连接限制是已运行验证的。
3. **Task Scheduler 部署**（文档 15.3）与**通知**（文档 15.2）：本次未实现，只留脚本骨架（`scripts/` 目录，标注 NOT_RUN）。
4. TS 内核的 Windows 阻塞点（B1–B10）尚未改，typecheck + 全量测试在 Windows 原生环境是否全绿未验证。

## 阶段 5 试点收口的文档与 opt-in 骨架

- `SUPPORT.md`：首发支持 / 真机回归后才标注 / 不列首发 三档支持矩阵（对应方案文档 471 行）。
- `LIMITATIONS.md`：全项目 NOT_RUN 项的正式用户清单，逐项标注「未验证，不开放正式承诺」。
- `worker/probe.ts` + `tests/probe.test.ts` + `tests/fixtures/fake-cli.mjs`：能力探测与 realCLI opt-in 门。探测只跑安全参数（版本/帮助），**绝不花 model 费用**；真实 CLI 跑一次花 model 费用，只有用户明确带 `--run` 才算授权。真实 agent 跑一律 NOT_RUN。

## 本次交付的边界

- 这是**独立的技术尖峰**，不是产品入口；没有接 TS Hub、没有账本、没有对账，`launchID` 只是 `DateTime.UtcNow.Ticks`。
- 进程树枚举用父 PID 粗证，Job 内精确枚举（`QueryInformationJobObject`）留到 Phase 2。
- 没有引入任何 Windows 服务、注册表、Task Scheduler、通知注册，也不改动上游 TS 源码或账号配置。
