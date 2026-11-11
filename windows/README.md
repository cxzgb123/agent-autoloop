# Windows 原生版：Phase 0/1 技术尖峰 + Phase 2 监督 + Phase 3 权限与效果

本目录是方案文档（`docs/claude/agent-autoloop-windows-design-2026-11-04.md`）第 17 节分阶段表的最小可运行切片。
Phase 0（Job Object 监督闭环）、Phase 1（内核与协议跨平台）已在此前的分支落地；本分支补上 Phase 2（Windows 监督）与 Phase 3（权限与效果）的 TS 内核与 WinHost 两侧。

## 本次做了什么（NOT_RUN 之外的部分）

证明方案的核心技术假设成立：**用 Win32 Job Object 监督 agent 进程树**，这是文档第 8 节整个 Windows 监督层的根基。

| 交付 | 内容 | 对应文档 | 状态 |
|---|---|---|---|
| `mock-agent.mjs` | 零依赖假 Agent，normal/hang/tree 三种模式，子进程继承 Job（tree 是 root→child→grandchild 三代树） | 文档 16 节 `tests/fixtures/mock-agent.mjs` | 已运行验证 |
| `WinHost/ProcessSupervisor.cs` | `CreateProcessW(CREATE_SUSPENDED)` → `AssignProcessToJobObject(KILL_ON_JOB_CLOSE)` → `ResumeThread`，任一步失败 fail closed 且回收自己持有的句柄，绝不 taskkill 任意 PID；Phase 2 起进程身份 = launchID + 启动时间 + exe 路径指纹，Job 内精确枚举 | 文档 8.2 / 8.3 | 已运行验证 |
| `WinHost/ExecutionLedger.cs` | 内存执行账本：按 launchID 记启动/退出/失败，区分「还在跑 / 已退 / 启动失败」，同 launchID 只记一棵树 | 文档 9.1 / 12 | 已运行验证 |
| `WinHost/Program.cs` | `rounds`（两轮闭环）/ `hang`（进程树清理）/ `tree`（Job 内精确枚举三代树）/ `ledger`（身份与账本三态）/ `pipe`（Named Pipe 控制面）/ `secrets`（DPAPI 自检）/ `selftest` 七个动作 | 文档 462 行退出条件 | 已运行验证 |
| `WinHost/ControlPipe.cs` | Named Pipe 入口：长度前缀协议、4KB 上限、单连接、方法白名单 `ping`/`status`、管道名带 installUUID；DACL 经 `CreateNamedPipeW` + `SECURITY_ATTRIBUTES` 真实施加到管道对象（读回验证 deny ACE 在对象上），拒绝网络/匿名 | 文档 10.1 | 白名单/长度/连接/DACL 施加已运行验证 |
| `WinHost/SecretStore.cs` | DPAPI CurrentUser 封装（`CryptProtectData`/`CryptUnprotectData`），只用 `CRYPTPROTECT_UI_FORBIDDEN`，不选 LocalMachine，坏密文抛异常不静默 | 文档 11.1 | 已运行验证 |
| TS 内核 `effects` 表 + 回执 `effect` 字段 | worker 产出 effect 请求只进审批卡；批准只由 owner 回答卡片；参数只存 hash；参数变了旧批准作废；副作用不明不重做 | 文档 10.3 | `tests/effects.test.ts` 8 用例已运行验证 |

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

复现：`windows\verify-phase0.cmd`（需本机已装 .NET 10 SDK 与 node ≥ 24）。秘密隔离 selftest：`dotnet windows\WinHost\bin\Release\net10.0\winhost.dll secrets`。

## 明确 NOT_RUN（文档要求、本机无法伪造的项）

1. **runtime 打包的干净 VM 证据**：文档 462 行退出条件要求「runtime 打包方案有干净 Windows 11 VM 证据」，且第 7 节第 4 点强调 `.NET` 部署（framework-dependent / self-contained / MSIX）与 Windows App SDK runtime 必须分别在干净 VM 验证。本机只验证了 framework-dependent（`dotnet winhost.dll` 运行）。**self-contained 打包与干净 VM 安装未做。**
2. **「非预期身份被拒」实机**：DACL 施加（`CreateNamedPipeW` + `SECURITY_ATTRIBUTES`）已运行验证——读回管道对象 SDDL 确认 `(D;;...;;;NU)` 与 `(D;;...;;;AN)` 两个 Deny ACE 真在对象上，当前用户 `ping` 通过。但「非预期身份被拒」必须用第二个 Windows 账号来连；本机只有一个账号，**这一项记 NOT_RUN，不做伪证**。
3. **Task Scheduler 部署**（文档 15.3）与**通知**（文档 15.2）：本次未实现，只留脚本骨架（`scripts/` 目录，标注 NOT_RUN）。
4. **DPAPI 跨账号解封拒绝**：`SecretStore` 已证明坏密文被拒与同一用户可解回；但「另一个 Windows 账号解不开当前用户封装的密文」需要第二个账号，本机只有一个，记 NOT_RUN，不做伪证。
5. **标准模式审批端与执行端身份隔离**（文档 10.3 严格模式）：标准模式不做抗恶意同用户保证，一个同用户进程仍可能绕过逻辑审批；这是文档明说的边界，不是本阶段要消除的缺口。

## 本次交付的边界

- 这是**独立的技术尖峰**，不是产品入口；没有接 TS Hub，账本是有状态可区分的**内存**切片（持久化与 Hub 重启对账是后续阶段），`launchID` 仍是 `DateTime.UtcNow.Ticks`。
- 进程树枚举在 Phase 2 已从父 PID 粗证换成 Job 内精确枚举（`QueryInformationJobObject` / `JobObjectBasicProcessIdList`），三代进程树不漏孙进程（`winhost tree` 验证）。
- 没有引入任何 Windows 服务、注册表、Task Scheduler、通知注册，也不改动上游 TS 源码或账号配置。
- Phase 3 的 effect 只在 TS 内核落「请求/批准/拒绝/不明」四种状态与参数 hash；真正执行副作用的是外部的 executor（交付插件/通道），本阶段不接，也不在 TS 内核做「重做」。`SecretStore` 只给「封装/解封」两个口子，TS 侧不落明文密钥。
