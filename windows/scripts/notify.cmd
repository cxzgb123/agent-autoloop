@echo off
rem ============================================================================
rem  NOT_RUN 骨架 —— 未在真机验证，只描述要做的事，绝不声称已通知。
rem
rem  通知（文档 15.2）的最小命令骨架。真实通知必须经 Windows App SDK 注册、激活与
rem  runtime 部署，且在干净非管理员 VM 跑通「安装→首次通知→关闭窗口→通知点击恢复→卸载」。
rem  本脚本不弹任何真通知。
rem ============================================================================

setlocal
echo.
echo NOT_RUN: 这是通知骨架，未注册 Windows App SDK，也未弹任何真通知。
echo 文档 15.2 合同（实现时逐条兑现）：
echo   - 卡片先事务写入，再经 outbox 通知；cardID 是首次发送幂等身份。
echo   - Windows 不显示通知不得 ACK 为用户已看到；回报区分 sent/suppressed/unavailable/failed。
echo   - Toast 只携带可公开摘要与 cardID，高风险审批不带 bearer token 进通知参数。
echo   - 勿扰/Focus Assist/远程桌面影响可见性，pending 决策在 UI 与 CLI 永远可查。
echo.
echo 退出码 0 表示「骨架存在」，不代表「已验证」。请勿据此宣称通知已成熟。
endlocal
