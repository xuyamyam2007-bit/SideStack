@echo off
rem ============================================================
rem  SideStack MSI packaging  (just double-click this file)
rem
rem  Optional arguments are passed through to installer\build-msi.ps1:
rem      build-msi.cmd -Version 1.0.1    set product version
rem      build-msi.cmd -Rebuild          rebuild SideStack.exe first
rem      build-msi.cmd -NoIce            skip ICE validation
rem
rem  Output: dist\SideStack-<version>-x64.msi  (per-user install, no admin needed)
rem  Chinese usage notes: installer\build-msi.ps1 header
rem ============================================================
setlocal
cd /d "%~dp0"

set "PS=powershell"
where pwsh >nul 2>&1 && set "PS=pwsh"

%PS% -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\build-msi.ps1" %*
set RC=%errorlevel%

echo.
if "%RC%"=="0" (
    echo [OK] MSI build finished.
) else (
    echo [FAILED] exit code %RC% - see messages above.
)
echo.
pause
exit /b %RC%
