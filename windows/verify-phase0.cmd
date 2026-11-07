@echo off
rem Phase 0 + Phase 4 one-shot verification: build WinHost, run the five selftests.
rem ASCII-only on purpose: cmd parses .cmd in the OEM code page; UTF-8 Chinese
rem comments break the parser into stray commands.
setlocal
cd /d "%~dp0"

echo === build WinHost ===
dotnet build WinHost\WinHost.csproj -c Release || exit /b 1

set OUT=WinHost\bin\Release\net10.0-windows
copy /y mock-agent.mjs "%OUT%\mock-agent.mjs" >nul
copy /y toast-standin.mjs "%OUT%\toast-standin.mjs" >nul

echo.
echo === two-round loop ===
dotnet "%OUT%\winhost.dll" rounds || exit /b 1

echo.
echo === process-tree cleanup ===
dotnet "%OUT%\winhost.dll" hang || exit /b 1

echo.
echo === named pipe control surface ===
dotnet "%OUT%\winhost.dll" pipe || exit /b 1

echo.
echo === tray / admin-page skeleton ===
dotnet "%OUT%\winhost.dll" tray || exit /b 1

echo.
echo === notification skeleton ===
dotnet "%OUT%\winhost.dll" notify || exit /b 1

echo.
echo Phase 0 + Phase 4 all PASS
endlocal
