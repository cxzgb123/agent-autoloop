# Windows installer / release —— 交付机制骨架（文档 15.3 / 16）

本目录是最小的「每用户签名安装包」交付结构，**不是**已可用的安装器。真实
install/update/rollback/uninstall 必须在干净非管理员 VM 跑通（文档 18.1「Windows 交付」层）。

## 文件

| 文件 | 内容 | 状态 |
|---|---|---|
| `manifest.json` | 签名 manifest 的最小字段：版本、launcher、平台、Node/.NET 运行时、hash、签名标记 | 静态自检 |
| `selfcheck.cmd` | 兼容检查（Node ≥ 24、.NET 10 WindowsDesktop、x64）+ manifest 字段自检 | **本机真跑** |
| `rollback.cmd` | 回滚/卸载骨架：只记录文档 15.3 合同，不删任何东西 | NOT_RUN |

## 本机真跑过的

```
cmd /c windows\installer\selfcheck.cmd
```

只验证「兼容检查与 manifest 字段」这一层；签名（Authenticode）、固定版本目录、
自带 Node Runtime、health probe、回滚切换、卸载清理都在干净 VM 层，**NOT_RUN**。

## 明确 NOT_RUN

1. 干净 VM 非 admin install / update / rollback / uninstall（文档 462 行退出条件）。
2. Authenticode / 发布签名校验（本机无证书，`signed` 恒 false，`sha256` 留空）。
3. 文件占用更新（绝不覆盖仍运行的 exe）、health probe、一致备份、版本切换。
