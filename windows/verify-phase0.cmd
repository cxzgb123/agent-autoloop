@echo off
rem Phase 0 一键验证：构建 WinHost，跑两轮闭环 + 进程树清理。
setlocal
cd /d "%~dp0"

echo === 构建 WinHost ===
dotnet build WinHost\WinHost.csproj -c Release || exit /b 1

set OUT=WinHost\bin\Release\net10.0-windows
copy /y mock-agent.mjs "%OUT%\mock-agent.mjs" >nul
copy /y toast-standin.mjs "%OUT%\toast-standin.mjs" >nul

echo.
echo === 两轮闭环 ===
dotnet "%OUT%\winhost.dll" rounds || exit /b 1

echo.
echo === 进程树清理 ===
dotnet "%OUT%\winhost.dll" hang || exit /b 1

echo.
echo === Named Pipe 控制面 ===
dotnet "%OUT%\winhost.dll" pipe || exit /b 1

echo.
echo === 管理页/托盘骨架 ===
dotnet "%OUT%\winhost.dll" tray || exit /b 1

echo.
echo === 通知骨架 ===
dotnet "%OUT%\winhost.dll" notify || exit /b 1

echo.
echo Phase 4 全部 PASS
endlocal
