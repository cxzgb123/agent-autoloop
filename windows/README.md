# Windows 原生版：Phase 0 监督尖峰 + Phase 4 UX/交付骨架

本目录是方案文档（`docs/claude/agent-autoloop-windows-design-2026-11-04.md`）第 17 节
「阶段 0」与「阶段 4」的最小可运行切片。

## Phase 0 做了什么（NOT_RUN 之外的部分）

证明方案的核心技术假设成立：**用 Win32 Job Object 监督 agent 进程树**（文档第 8 节）。

| 交付 | 内容 | 对应文档 | 状态 |
|---|---|---|---|
| `mock-agent.mjs` | 零依赖假 Agent，normal/hang 两种模式，子进程继承 Job | 文档 16 节 `tests/fixtures/mock-agent.mjs` | 已运行验证 |
| `WinHost/ProcessSupervisor.cs` | `CreateProcessW(CREATE_SUSPENDED)` → `AssignProcessToJobObject(KILL_ON_JOB_CLOSE)` → `ResumeThread`，任一步失败 fail closed 且回收自己持有的句柄，绝不 taskkill 任意 PID | 文档 8.2 / 8.3 | 已运行验证 |
| `WinHost/ControlPipe.cs` | Named Pipe 入口：长度前缀协议、4KB 上限、单连接、方法白名单 `ping`/`status`、管道名带 installUUID、SDDL 拒绝网络/匿名 | 文档 10.1 | 白名单/长度/连接已运行验证 |

## Phase 4 做了什么（UX 与交付，NOT_RUN 之外的部分）

| 交付 | 内容 | 对应文档 | 状态 |
|---|---|---|---|
| `WinHost/TrayContext.cs` | 最小管理页 + 托盘：四态（运行中/已暂停/需要决定/错误）、启动/停止/刷新按钮，只消费状态，不解释模型内容 | 文档 15.1 | `tray` selftest 已运行 |
| `WinHost/Notifications.cs` | Toast 注册/activation 骨架：cardID 幂等身份 + revision 更新身份，只携带公开摘要，不带 bearer token；假命令 `toast-standin.mjs` 逐字传参自检 | 文档 15.2 | `notify` selftest 已运行 |
| `scripts/agent-autoloop-logon.xml` + `scripts/install-task-scheduler.cmd` | Task Scheduler 每用户登录任务 XML：InteractiveToken、LeastPrivilege、`ExecutionTimeLimit=PT0S`、`MultipleInstancesPolicy=IgnoreNew`、`WakeToRun=false`、不存密码、失败重启 1 分钟×3 | 文档 15.3 | `--check` 静态自检已运行 |
| `installer/manifest.json` + `installer/selfcheck.cmd` + `installer/rollback.cmd` | 每用户签名安装包最小结构：manifest 字段、兼容检查（Node ≥ 24 / .NET 10 WindowsDesktop / x64）、回滚卸载骨架 | 文档 15.3 / 16 | `selfcheck.cmd` 已运行 |

## 验证结果

复现：`windows\verify-phase0.cmd`（需本机已装 .NET 10 SDK 与 node ≥ 24）。覆盖
rounds / hang / pipe / tray / notify 五个 selftest。

```
=== 管理页/托盘骨架 ===
[tray] 四态文本 = 运行中 / 已暂停 / 需要决定 / 错误
[tray] 托盘标题 = 运行中（期望 运行中）
[tray] 管理页控件数 = 4（期望 4）
Phase 4 管理页/托盘 PASS（真实显示与可达性：NOT_RUN，需桌面会话）

=== 通知骨架 ===
[notify] AUMID = agent-autoloop.<uuid>
[notify] 含 cardID=True revision=True 公开摘要=True（期望全 True）
[notify] 参数逐字传递 = True（期望 True）
Phase 4 通知骨架 PASS（真实 Toast 注册/激活/勿扰/Narrator：NOT_RUN）

=== Task Scheduler 静态自检 ===
cmd /c windows\scripts\install-task-scheduler.cmd --check   → PASS

=== installer 兼容检查 ===
cmd /c windows\installer\selfcheck.cmd   → PASS
```

TS 侧：`npm run typecheck` 全绿、`npm run format:check` 全绿、`npm test` 349 tests /
338 pass / 11 skip / 0 fail（Windows 原生）。

## 明确 NOT_RUN（文档要求、本机无法伪造的项）

1. **runtime 打包的干净 VM 证据**：文档 462 行退出条件要求「干净 VM 非 admin 全闭环；
   数据保留/恢复/升级身份可核验」。本机只验证了 framework-dependent（`dotnet winhost.dll`
   运行）。**self-contained 打包与干净 VM 安装/更新/回滚/卸载未做**（`installer/` 是骨架）。
2. **Named Pipe 的 DACL 实机施加与「非预期身份被拒」**：`ControlPipe.SecuritySddl()` 给出
   只允许当前用户、拒绝网络/匿名的 SDDL 字符串，但真正把 DACL 施加到管道需要
   `CreateNamedPipeW` 的 P/Invoke 携带 `SECURITY_ATTRIBUTES`，且「非预期身份被拒」必须用
   第二个 Windows 账号来连；本机只有一个账号，**都记 NOT_RUN**（Phase 2 分支有真实施加，
   未合并进本分支）。
3. **Task Scheduler 真实部署与停机测试**（文档 15.3）：本目录只有 XML + `--check` 静态
   自检；真实 `schtasks` 注册、登录触发、注销后不运行、睡眠/关机不执行，都要干净 VM 核验。
4. **真实 Toast 注册/激活**（文档 15.2）：Windows App SDK 注册、激活与 runtime 部署、
   「安装→首次通知→关闭窗口→通知点击恢复→卸载」、后台激活、勿扰/Focus Assist、Narrator
   都未做；CI 没有桌面，不能假装跑过 Toast。`toast-standin.mjs` 只逐字打印参数，不弹真通知。
5. **管理页真实显示与可达性**（文档 15.1 / 18.1 UI/通知层）：高 DPI、键盘导航、Narrator、
   系统高对比、中文用户名、长文本与 RTL 不溢出都未做；`tray` selftest 只构造页面与托盘
   标题，不 Show。

## 本次交付的边界

- 这是**独立的技术尖峰 + UX/交付骨架**，不是产品入口；没有接 TS Hub、没有账本、没有对账，
  `launchID` 只是 `DateTime.UtcNow.Ticks`。
- WinHost 只消费状态、不决定业务：管理页四态由外部 status provider 给，通知只带
  cardID/revision/公开摘要，高风险批准绝不经通知参数携带 bearer token。
- 进程树枚举用父 PID 粗证，Job 内精确枚举（`QueryInformationJobObject`）在 Phase 2 分支。
- 没有引入任何 Windows 服务、注册表、Task Scheduler 真实注册、通知真实注册，也不改动上游
  TS 源码或账号配置。
