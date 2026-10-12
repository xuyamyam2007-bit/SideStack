@echo off
rem ============================================================
rem  SideStack publish to GitHub  (just double-click this file)
rem
rem  This is a thin wrapper around upload-github.ps1. It can be
rem  started from ANY working directory (the script resolves its
rem  own location via $PSScriptRoot).
rem
rem  Optional arguments are passed straight through:
rem      upload.cmd -Version 1.0.2      build + tag v1.0.2 (CI publishes the Release)
rem      upload.cmd -DryRun             build everything, but do not commit/push
rem      upload.cmd -BuildOnly          only compile exe + MSI, leave the repo alone
rem      upload.cmd -Yes                skip the confirmation prompt
rem      upload.cmd -Message "fix: xx"  custom commit message
rem      upload.cmd -IncludeExtra       also upload MSI / installer / tools / this script
rem
rem  Exit codes: 0 ok / 1 env error / 2 cancelled / 3 build failed / 4 push failed
rem  Chinese usage notes: see the header of upload-github.ps1
rem ============================================================
setlocal
cd /d "%~dp0"

set "PS=powershell"
where pwsh >nul 2>&1 && set "PS=pwsh"

%PS% -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0upload-github.ps1" %*
set RC=%errorlevel%

echo.
if "%RC%"=="0" (
    echo [OK] publish finished.
) else (
    echo [FAILED] exit code %RC% - see messages above.
)
echo.
pause
exit /b %RC%
