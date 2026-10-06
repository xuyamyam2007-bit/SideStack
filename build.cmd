@echo off
rem ============================================================
rem  SideStack one-click build  (just double-click this file)
rem
rem  Optional arguments are passed through to build.ps1:
rem      build.cmd -Restart    kill old instance, then start new build
rem      build.cmd -NoBackup   do not create SideStack.exe.bak
rem      build.cmd -Clean      only remove temporary build artifacts
rem
rem  Chinese usage notes: see build.ps1 header / ????.md
rem ============================================================
setlocal
cd /d "%~dp0"

set "PS=powershell"
where pwsh >nul 2>&1 && set "PS=pwsh"

%PS% -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set RC=%errorlevel%

echo.
if "%RC%"=="0" (
    echo [OK] build finished.
) else (
    echo [FAILED] exit code %RC% - see messages above.
)
echo.
pause
exit /b %RC%
