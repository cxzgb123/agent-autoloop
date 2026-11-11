# agent-autoloop Windows 原生版 测试手册

> 给真机测试者的可执行测试清单。每条先写**怎么跑**、**期望什么**、**怎么判 PASS/FAIL**。
> 前置：本机已装 **.NET 10 SDK**（framework-dependent 跑法）或直接用发布好的 self-contained `winhost.exe`；**node ≥ 24**（WinHost 要 spawn mock agent）。

## 0. 两种跑法

**A. framework-dependent（开发机，需 .NET 10 SDK）**
```bash
cd windows
dotnet build WinHost/WinHost.csproj -c Release
dotnet WinHost/bin/Release/net10.0-windows/winhost.dll <动作>
```

**B. self-contained 单文件（无需 .NET runtime，适合干净 VM）**
```bash
cd windows
# 先出包（开发机做一次）：
dotnet publish WinHost/WinHost.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
# 把假 agent 放进 publish 目录：
copy mock-agent.mjs publish\
copy toast-standin.mjs publish\
# 之后任意机器直接跑：
publish\winhost.exe <动作>
```

## 1. 进程监督层（Phase 0 + Phase 2）

### 1.1 两轮闭环（rounds）
```bash
winhost rounds
```
**期望**：`[round 1] 闭环成功`、`[round 2] 闭环成功`、`Phase 0 两轮闭环 PASS`。
**判 FAIL**：任一 round 退出码非 0。

### 1.2 进程树清理（hang）
```bash
winhost hang
```
**期望**：`进程树内子进程数 = 1`、`TerminateJobObject 后：父存活=False，子存活=False`、`PASS`。
**判 FAIL**：没观察到子进程，或 Stop 后父/子仍存活。

### 1.3 Job 内精确枚举三代树（tree）
```bash
winhost tree
```
**期望**：`Job 内精确枚举找到 3 个进程`、`Stop 全清`、`PASS`。
**判 FAIL**：枚举到的进程数 < 3（漏孙进程），或 Stop 后仍有存活。

### 1.4 身份与账本三态（ledger）
```bash
winhost ledger
```
**期望**：`运行中状态 = Running`、`退出后状态 = Exited`、`启动失败状态 = LaunchFailed`、`去重 launchID = 记录数`、`PASS`。
**判 FAIL**：任一状态不对，或同 launchID 出现多棵树。

## 2. 本地控制面（Phase 0 Named Pipe）

```bash
winhost pipe
```
**期望**：
- `SDDL 含 Network/Anonymous Deny = True`
- `合法方法 ping -> {"ok":true,...}`
- `白名单外方法 -> {"ok":false,...}`
- `超长消息 -> （EOF，已断开）`
- `Phase 0 Named Pipe 控制面 PASS`
**判 FAIL**：白名单外方法被放行，或超长消息被放行。

> **NOT_RUN（需第二个 Windows 账号）**：「非预期身份被拒」——用另一个账号去连管道，应被 DACL 拒绝。本机只有一个账号，无法自动测；若你手上有第二个账号，可手动验证连不上。

## 3. 秘密隔离（Phase 3 DPAPI）

```bash
winhost secrets
```
**期望**：`密文不含明文 = True`、`解封还原原文 = True`、`坏密文被拒 = True`、`PASS`。
**判 FAIL**：明文泄露进密文，或坏密文被静默放行。

> **NOT_RUN（需第二个账号）**：另一个 Windows 账号应解不开当前用户的密文。

## 4. UX 骨架（Phase 4）

```bash
winhost tray
winhost notify
```
**tray 期望**：`四态文本 = 运行中 / 已暂停 / 需要决定 / 错误`、`管理页控件数 = 4`、`PASS`。
**notify 期望**：`含 cardID=True revision=True 公开摘要=True`、`参数逐字传递 = True`、`PASS`。

> **NOT_RUN（需真实桌面会话 + App SDK runtime）**：真实托盘显示、管理页可达性（键盘/Narrator/高对比/高 DPI）、真实 Toast 注册/激活/勿扰。`tray`/`notify` 只构造对象不 Show。

## 5. 交付骨架（Phase 4）

```bash
# Task Scheduler 登录任务 XML 静态自检
cmd /c windows\scripts\install-task-scheduler.cmd --check

# installer 兼容检查（Node≥24 / .NET 10 WindowsDesktop / x64 / manifest 字段）
cmd /c windows\installer\selfcheck.cmd
```
**期望**：两者都 `PASS`。

> **NOT_RUN（需干净 VM）**：真实 `schtasks` 注册与登录触发/注销不运行/睡眠不执行；非 admin install/update/rollback/uninstall；Authenticode 签名（`manifest.json` 的 `signed` 恒 false、`sha256` 留空）。

## 6. TS 内核与协议（Phase 3 + Phase 5）

```bash
cd <仓库根>
npm run typecheck
npm run format:check
npm test          # 含 20 万步模拟 + 有界穷举，较慢
```
**期望**：typecheck 0 错、format 全过、`npm test` 0 fail（11 个 skip 是既有 win32 守卫，不算失败）。
**判 FAIL**：任何 fail。

> `npm test` 里 20 万步模拟 + 有界穷举耗时较长，真机测试建议用 `npm run test:quick`（跳过模拟和穷举）先快速冒烟，全量留到发布前。

## 7. 真机项（本手册不代签，交给你在真机/VM 上做）

以下没有自动脚本能「假装跑过」，必须你在真实环境验证，并**保留真实输出**：

| 项 | 怎么测 | 期望 |
|---|---|---|
| 干净 VM 非 admin 全闭环 | 干净 Windows 11 VM，非管理员账号，用 self-contained exe install→update→rollback→uninstall | 全流程成功，失败保留可解释错误 |
| 第二个账号 DACL 拒绝 | 账号 A 跑 `winhost pipe`，账号 B 去连管道 | B 被拒 |
| 第二个账号 DPAPI 拒绝 | 账号 A 封装密文，账号 B 解封 | B 抛异常 |
| 真实 Task Scheduler 登录任务 | 注册 XML，登录触发 / 注销 / 睡眠 / 关机 | 登录启动、注销不跑、睡眠不执行 |
| 真实 Toast | 注册 App，走「安装→通知→点击恢复→卸载」 | 通知点开恢复对应卡片 |
| 管理页可达性 | 键盘导航、Narrator、高对比、高 DPI、中文用户名、长文本 | 不溢出、可操作 |
| 真实 agent 跑 | 用户明确授权后跑 Claude/Codex 一轮 | 能力探测、session 续接、费用增量正常 |

> 真机跑真实 agent / 真实交付会花 model 费用或创建私有测试仓库，**必须你明确授权**才做，本手册不默认触发。
