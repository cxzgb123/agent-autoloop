@echo off
rem ============================================================================
rem  installer self-check (runs for real): the part of the per-user installer that
rem  CAN be verified on this machine. Real install/update/rollback/uninstall on a
rem  clean non-admin VM stays NOT_RUN.
rem  ASCII-only on purpose: cmd parses .cmd in the OEM code page; UTF-8 Chinese
rem  comments break the parser into stray commands.
rem ============================================================================

setlocal enabledelayedexpansion
cd /d "%~dp0"

set "FAIL=0"
set "MANIFEST=%~dp0manifest.json"

if not exist "%MANIFEST%" (
  echo [check] FAIL: manifest not found: %MANIFEST%
  set "FAIL=1"
  goto tool
)

rem Signed-manifest contract (doc 15.3 / 16): version, launcher, hash field, compat reqs.
for %%K in (product version launcher platform nodeRuntime dotnetRuntime sha256 signed) do (
  findstr /c:"\"%%K\"" "%MANIFEST%" >nul || (
    echo [check] FAIL: manifest missing field %%K
    set "FAIL=1"
  )
)

:tool
rem Compatibility check (doc 15.3): Node >= 24, .NET 10, Windows x64. These run for real.
node --version >nul 2>&1 || (
  echo [check] FAIL: node not on PATH
  set "FAIL=1"
  goto arch
)
for /f "delims=v." %%a in ('node --version') do set "NODE_MAJOR=%%a"
if not defined NODE_MAJOR set "NODE_MAJOR=0"
if !NODE_MAJOR! LSS 24 (
  echo [check] FAIL: node !NODE_MAJOR! ^< 24
  set "FAIL=1"
)

:arch
if /i not "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
  echo [check] FAIL: not x64 ^(%PROCESSOR_ARCHITECTURE%^)
  set "FAIL=1"
)

rem .NET 10 desktop runtime must be present (winhost targets net10.0-windows).
dotnet --list-runtimes 2>nul | findstr /c:"Microsoft.WindowsDesktop.App 10." >nul || (
  echo [check] FAIL: .NET 10 WindowsDesktop runtime not found
  set "FAIL=1"
)

if "!FAIL!"=="1" goto done
echo [check] installer self-check PASS (real VM install/update/rollback/uninstall still NOT_RUN)
:done
endlocal
if "%FAIL%"=="1" exit /b 1
exit /b 0
