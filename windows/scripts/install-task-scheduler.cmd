@echo off
rem ============================================================================
rem  NOT_RUN 骨架 —— 未在真机验证，只描述要做的事，绝不声称已常驻。
rem
rem  Task Scheduler 部署（文档 15.3）的最小命令骨架。真实安装与停机测试必须在干净
rem  Windows 11 VM 上跑，并核验文档 15.3 列出的每项（InteractiveToken、登录触发、
rem  ExecutionTimeLimit=PT0S、MultipleInstancesPolicy=IgnoreNew、WakeToRun=false 等）。
rem  本脚本不执行任何调度，只打印契约，避免把「没验证的常驻」伪装成「已启用」。
rem ============================================================================

setlocal
echo.
echo NOT_RUN: 这是 Task Scheduler 骨架，未执行真实注册，也未做停机测试。
echo 真实部署所需的 schtasks 参数（拟议，需真机验证）：
echo.
echo   schtasks /Create /TN "agent-autoloop-<installUUID>" ^
echo     /TR "\"C:\path\to\winhost.exe\" --dataHome \"C:\path\to\home\"" ^
echo     /SC ONLOGON /RL LIMITED /F
echo.
echo 需另行设置的 XML 级字段（schtasks 不直接暴露）：
echo   ExecutionTimeLimit=PT0S（避免默认 72 小时把常驻宿主结束）
echo   MultipleInstancesPolicy=IgnoreNew（配应用自身单实例锁）
echo   WakeToRun=false（默认不叫醒电脑）
echo.
echo 退出码 0 表示「骨架存在」，不代表「已验证」。请勿据此宣称常驻已启用。
endlocal
