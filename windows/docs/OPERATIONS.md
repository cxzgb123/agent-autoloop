# agent-autoloop Windows 原生版 操作手册

> 给真机操作者的上手步骤。**本目录当前是「技术尖峰 + 交付骨架」，不是产品入口**——它监督假 Agent、跑自检，但还没有接真实 TS Hub 的账本与对账，`launchID` 只是时间戳。真实安装/常驻/通知仍未验证。

## 1. 准备

- 系统：Windows 11 x64（本机为 11 Pro 26100）。
- 运行时：二选一
  - **开发机**：.NET 10 SDK + node ≥ 24。
  - **目标机**：用发布好的 self-contained `winhost.exe`（无需装 .NET），但 spawn 假 Agent 仍需 node ≥ 24 在 PATH。

## 2. 出包（开发机做一次）

```bash
cd windows
dotnet publish WinHost/WinHost.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
copy mock-agent.mjs publish\
copy toast-standin.mjs publish\
```

产物：`publish\winhost.exe`（单文件，约 111M），可拷到任何 Windows 11 x64 机器直接运行。

## 3. 运行自检（确认能跑）

```bash
winhost selftest      # 应打印 ".NET 10 + Job Object P/Invoke 已加载"
winhost rounds        # 两轮受监督闭环
winhost hang          # 进程树清理
winhost tree          # Job 内精确枚举三代树
winhost ledger        # 身份与账本三态
winhost secrets       # DPAPI 秘密隔离
winhost pipe          # Named Pipe 控制面
winhost tray          # 管理页/托盘骨架
winhost notify        # 通知骨架
```

全部 PASS 即证明本目录的 WinHost 侧可用。一键脚本 `windows\verify-phase0.cmd` 覆盖前八项（需 .NET SDK）。

## 4. 动作一览

| 动作 | 作用 | 真机边界 |
|---|---|---|
| `rounds` | 两轮受监督闭环 | 已验证 |
| `hang` | TerminateJobObject 清进程树 | 已验证 |
| `tree` | Job 内精确枚举三代树 | 已验证 |
| `ledger` | 身份 + 账本三态 | 已验证 |
| `secrets` | DPAPI 封装/解封/坏密文 | 跨账号 NOT_RUN |
| `pipe` | Named Pipe 白名单/DACL | 跨账号 NOT_RUN |
| `tray` | 管理页/托盘骨架 | 真实显示 NOT_RUN |
| `notify` | 通知骨架 | 真实 Toast NOT_RUN |
| `selftest` | 最小加载自检 | 已验证 |

## 5. TS 侧操作

```bash
cd <仓库根>
npm run typecheck
npm run format:check
npm test            # 全量（慢）
npm run test:quick  # 快速冒烟（跳过模拟/穷举）
```

TS 内核是「只收、只分、只派、只记」的中台，无界面；唯一页面是卡片链接（不跑脚本的表单）。WinHost 是 Windows 侧的外壳，两者经协议对接，本阶段尚未接真实 Hub。

## 6. 交付机制（骨架，真机 NOT_RUN）

- **Task Scheduler 登录任务**：`windows\scripts\install-task-scheduler.cmd`，XML 见 `windows\scripts\agent-autoloop-logon.xml`。真实注册未做。
- **安装包**：`windows\installer\manifest.json`（版本/平台/运行时/hash/签名）+ `selfcheck.cmd`（兼容检查，可真跑）+ `rollback.cmd`（骨架）。真实 install/update/rollback/uninstall 未做。
- **签名**：`manifest.json` 的 `signed` 恒 false、`sha256` 留空，发布时再填并 Authenticode 签名。

## 7. 安全边界（操作时必须知道）

1. **进程监督不误杀**：WinHost 只用自己持有的句柄 `TerminateJobObject`，绝不对任意 PID `taskkill`。
2. **控制面最小**：Named Pipe 方法白名单只有 `ping`/`status`，多一分控制面多一分可被滥用的入口。
3. **秘密不落明文**：`SecretStore` 只给「封装/解封」两个口子，TS 侧不落明文密钥。
4. **worker 不能自批**：effect 审批只由 owner 回答卡片，参数只存 hash，参数变旧批准作废，副作用不明不重做。
5. **文字是资料，规则才是指令**：事件里的字不进提示词，也影响不了交给谁、在哪、用什么权限。

## 8. 已知未验证项（真机前必读）

完整清单见 [LIMITATIONS.md](../LIMITATIONS.md)。一句话速查：

- 需**第二个账号**：DACL 跨账号拒绝、DPAPI 跨账号解封拒绝。
- 需**干净 VM**：非 admin install/update/rollback/uninstall、self-contained/MSIX 打包、真实 Task Scheduler 部署、签名。
- 需**真实桌面会话**：真实 Toast 激活、管理页可达性（键盘/Narrator/高对比/高 DPI）。
- 需**真实运行**：7 天长跑、真实 agent 跑（花 model 费用）、真实交付（创建私有测试仓库）。

以上未验证项**不开放正式承诺**，真机测试请按 [TESTING.md](TESTING.md) 第 7 节逐项做并保留真实输出。
