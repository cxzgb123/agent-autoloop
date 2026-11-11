@echo off
rem ============================================================================
rem  Rollback / uninstall skeleton - NOT_RUN. No real rollback, no real uninstall.
rem  The doc-15.3 contract is recorded here so a later release step can honor it;
rem  real uninstall on a clean VM must keep business data + keys, and only remove
rem  the entries/tasks/registrations this product created.
rem  ASCII-only on purpose (see install-task-scheduler.cmd).
rem ============================================================================

echo.
echo NOT_RUN: rollback/uninstall skeleton. Nothing is removed, nothing is rolled back.
echo Doc-15.3 contract to honor at real release:
echo   - uninstall keeps business data and keys by default.
echo   - only remove entries/tasks/registrations THIS product created.
echo   - stop your own supervised jobs after identity verification, never by a
echo     stale pid file.
echo   - deleting data is a separate, irreversible action the user confirms alone.
echo   - rollback restores the previous compatible version after a health-probe
echo     failure; never overwrite a still-running exe.
echo.
echo Exit code 0 means "skeleton exists", not "verified".
exit /b 0
