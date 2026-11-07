@echo off
rem ============================================================================
rem  Task Scheduler logon task - NOT_RUN skeleton. Default prints the contract;
rem  --check runs a static self-check of the XML against the doc-15.3 contract.
rem  Real deploy + stop testing must happen on a clean Windows 11 VM.
rem  NOTE: this file is ASCII-only on purpose: cmd parses .cmd in the OEM code
rem  page, and UTF-8 Chinese comments break the parser (garbled to stray chars).
rem ============================================================================

setlocal enabledelayedexpansion
cd /d "%~dp0"

set "XML=%~dp0agent-autoloop-logon.xml"

if /i "%~1"=="--check" goto check

echo.
echo NOT_RUN: Task Scheduler logon-task skeleton. No real registration, no stop test.
echo Proposed schtasks (must be verified on a real machine):
echo.
echo   schtasks /Create /TN "agent-autoloop-^<installUUID^>" /XML "%XML%" /F
echo.
echo Before real deploy, replace __CURRENT_USER__ / __LAUNCHER_PATH__ / __DATA_HOME__
echo in the XML and verify on a clean VM: InteractiveToken, logon trigger,
echo ExecutionTimeLimit=PT0S, MultipleInstancesPolicy=IgnoreNew, WakeToRun=false, no password.
echo Exit code 0 means "skeleton exists", not "verified".
endlocal
exit /b 0

:check
set "FAIL=0"
if not exist "%XML%" (
  echo [check] FAIL: task XML not found: %XML%
  set "FAIL=1"
  goto done
)
rem Static self-check: each doc-15.3 field must be present.
for %%K in (InteractiveToken LeastPrivilege LogonTrigger MultipleInstancesPolicy IgnoreNew WakeToRun ExecutionTimeLimit PT0S RestartOnFailure Count Interval PT1M) do (
  findstr /c:"%%K" "%XML%" >nul || (
    echo [check] FAIL: XML missing %%K
    set "FAIL=1"
  )
)
rem Must NOT appear: password, SYSTEM, S4U.
for %%K in (Password SYSTEM S4U) do (
  findstr /c:"%%K" "%XML%" >nul && (
    echo [check] FAIL: XML contains forbidden %%K
    set "FAIL=1"
  )
)
rem WakeToRun must be false, never true.
findstr /c:"<WakeToRun>false</WakeToRun>" "%XML%" >nul || (
  echo [check] FAIL: WakeToRun is not false
  set "FAIL=1"
)
if "!FAIL!"=="1" goto done
echo [check] XML static self-check PASS (logon deploy + stop test still NOT_RUN, needs clean VM)
:done
endlocal
if "%FAIL%"=="1" exit /b 1
exit /b 0
