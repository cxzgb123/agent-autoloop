# agent-autoloop Windows 原生版 能力说明

> 面向真机测试者的能力清单。**「已验证」= 本机真实运行过并有输出作证；「未验证 NOT_RUN」= 需要干净 VM / 第二个账号 / 真实桌面会话才能测，当前没有证据，不开放正式承诺。**

## 1. 这是什么

agent-autoloop 是一个「收事件 → 分派 → 派活 → 记账」的中台。本目录（`windows/`）是它的 **Windows 11 原生版**，用一个小型 `.NET 10` 宿主（WinHost）监督每一次 agent 运行，复用上游 TypeScript 内核与协议。

- **内核（`kernel/`、`protocol/`）**：只收、只分、只派、只记。不主动连接任何东西，不知道产品名。
- **接单程序（`worker/`）**：把每轮活交给真实 CLI（Claude / Codex），只通过能力探测，绝不直接 shell。
- **Windows 宿主（`windows/WinHost/`）**：用 Win32 **Job Object** 监督 agent 进程树，用 **Named Pipe** 做本地控制面，用 **DPAPI** 存秘密。

## 2. 核心能力

### 2.1 进程树监督（Win32 Job Object）—— 已验证

- `CreateProcessW(CREATE_SUSPENDED)` → 挂进 Job（`KILL_ON_JOB_CLOSE`）→ 恢复运行；任一步失败即 fail closed，只回收自己拿到的句柄，**绝不对任意 PID 执行 taskkill**。
- 进程身份 = `launchID + 启动时间 + exe 路径指纹`，PID 仅诊断。
- 停止用 `TerminateJobObject` 清整棵进程树；Hub 崩溃不会误杀 agent，WinHost 退出才会。
- **Job 内精确枚举**（`QueryInformationJobObject`）：三代进程树（root→child→grandchild）不漏孙进程。

### 2.2 执行账本（内存）—— 已验证

- 按 `launchID` 记启动 / 退出 / 失败，区分「还在跑 / 已退 / 启动失败」三态；同 `launchID` 只记一棵树。
- **注意**：当前是**内存**切片，持久化与 Hub 重启对账属后续阶段，`launchID` 仍是 `DateTime.UtcNow.Ticks`。

### 2.3 本地控制面（Named Pipe）—— 部分已验证

- 长度前缀协议、4KB 上限、单连接、方法白名单 `ping`/`status`、管道名带 `installUUID`。
- **DACL 施加已验证**：`CreateNamedPipeW` + `SECURITY_ATTRIBUTES` 读回确认 `(D;;...;;;NU)`、`(D;;...;;;AN)` 两个 Deny ACE 真在对象上。
- **「非预期身份被拒」未验证 NOT_RUN**：需第二个 Windows 账号来连，本机只有一个账号。

### 2.4 秘密隔离（DPAPI）—— 部分已验证

- `SecretStore` 用 `CryptProtectData`/`CryptUnprotectData`（CurrentUser），坏密文抛异常不静默。
- 密文不含明文、同用户可解回、坏密文被拒 —— **已验证**。
- **跨账号解封拒绝未验证 NOT_RUN**：需第二个 Windows 账号。

### 2.5 权限与效果（effect 审批卡）—— 已验证

- worker 产出 effect 请求**只进审批卡**，批准只由 owner 回答卡片；参数只存 hash；参数变了旧批准作废；副作用不明不重做。
- worker **不能自批**自己的动作。
- 真正执行副作用的是外部 executor（交付插件/通道），本阶段不接。

### 2.6 管理页 / 托盘 / 通知（骨架）—— 部分已验证

- `TrayContext`：四态（运行中/已暂停/需要决定/错误）+ 启动/停止/刷新，只消费状态，不解释模型内容。
- `Notifications`：Toast 骨架，cardID 幂等身份 + revision 更新身份，只带公开摘要，不带 bearer token。
- 构造与参数自检 **已验证**；**真实显示 / 可达性 / Toast 激活未验证 NOT_RUN**（需真实桌面会话 + App SDK runtime）。

### 2.7 交付骨架（Task Scheduler + installer）—— 部分已验证

- 每用户登录任务 XML（`InteractiveToken`、`LeastPrivilege`、`ExecutionTimeLimit=PT0S`、不存密码）。
- 每用户安装包最小结构（manifest + 兼容检查 + 回滚骨架）。
- XML 静态自检、兼容检查 **已验证**；**干净 VM 非 admin 全闭环、真实 schtasks 注册、签名未验证 NOT_RUN**。

## 3. 首发支持范围（目标，非已全部验证）

| 能力 | 状态 |
|---|---|
| Windows 11 x64 | 首发支持（本机 11 Pro 26100 已跑 Phase 0–5 骨架） |
| 本地单用户可信模式（标准模式） | 首发支持（不隔离同用户恶意，文档 10.2） |
| Claude / Codex 能力探测闭环 | 目标（假 CLI 已验证；真实闭环 NOT_RUN） |
| 明确审批的受控动作 | 目标（审批卡已验证；executor 未接） |
| 登录常驻 / CLI / 托盘 / 备份更新 | 目标（骨架已验证；真机 NOT_RUN） |

**不列首发**：Windows 10、服务器 Session0 无人值守、任意 ACP/command 严格隔离、手机公网面板、Windows ARM64（真机回归后再标）。

## 4. 平台边界

- 内核不读时钟、不取随机数、不读环境或文件；时间/规则/密钥从外壳传入。
- WinHost 只消费状态、不决定业务：不解释模型内容、不决定业务是否获批。
- 通道用本地假 HTTP 服务，通知用 PATH 前面的假命令，不真发。

## 5. 未验证项速查

完整清单见 [LIMITATIONS.md](../LIMITATIONS.md)。一句话：**「需要第二个账号 / 干净 VM / 真实桌面会话 / 真实 agent 跑」的项全部 NOT_RUN，不做伪证。**
