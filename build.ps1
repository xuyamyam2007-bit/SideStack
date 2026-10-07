# SideStack 一键编译脚本
# 用法（在本目录下打开 PowerShell 执行）：
#   .\build.ps1                 编译（自动备份 SideStack.exe.bak，不启动）
#   .\build.ps1 -Restart        编译后结束旧实例并启动新版本
#   .\build.ps1 -NoBackup       不生成/覆盖备份
#   .\build.ps1 -Clean          只删除临时产物（SideStack.exe.new 等）后退出
#
# 说明：
#   * 本机没有 .NET SDK，脚本自动定位 Windows 自带的 .NET Framework 编译器 csc.exe；
#   * 先编译到 SideStack.exe.new，**编译成功才替换** SideStack.exe，编译失败时现有程序不受影响；
#   * 默认会在替换前把现有 exe 备份成 SideStack.exe.bak；
#   * 若 SideStack 正在运行：替换会被文件锁挡住，脚本会提示（或按 -Restart 自动结束旧实例）。

[CmdletBinding()]
param(
    [switch]$Restart,     # 编译后结束旧实例并启动新版本
    [switch]$NoBackup,    # 不生成备份
    [switch]$Clean        # 只清理临时产物
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $dir 'src'
$exe = Join-Path $dir 'SideStack.exe'
$out = Join-Path $dir 'SideStack.exe.new'
$bak = Join-Path $dir 'SideStack.exe.bak'
$log = Join-Path $env:TEMP 'sidestack_build.log'

# ---------------------------------------------------------------- 清理模式
if ($Clean) {
    foreach ($f in @($out, (Join-Path $dir 'SideStack.pdb'))) {
        if (Test-Path $f) { Remove-Item $f -Force; Write-Host "已删除 $([IO.Path]::GetFileName($f))" -ForegroundColor Yellow }
    }
    Write-Host '清理完成。' -ForegroundColor Green
    exit 0
}

Write-Host '=== SideStack 编译 ===' -ForegroundColor Cyan
Write-Host "程序目录: $dir"

# ---------------------------------------------------------------- 定位编译器
function Find-Csc {
    $cands = @()
    $win = $env:WINDIR; if (-not $win) { $win = 'C:\Windows' }
    foreach ($fw in @('Framework64', 'Framework')) {
        $base = Join-Path $win "Microsoft.NET\$fw"
        if (Test-Path $base) {
            $cands += (Get-ChildItem -Path $base -Directory -ErrorAction SilentlyContinue |
                       Where-Object { $_.Name -like 'v4*' } |
                       Sort-Object Name -Descending |
                       ForEach-Object { Join-Path $_.FullName 'csc.exe' })
        }
    }
    foreach ($c in $cands) { if (Test-Path $c) { return $c } }
    return $null
}

$csc = Find-Csc
if (-not $csc) {
    Write-Host '找不到 C# 编译器（csc.exe），需要 .NET Framework 4.x。' -ForegroundColor Red
    exit 1
}
Write-Host "编译器  : $csc"

$fwDir = Split-Path -Parent $csc
$wpfDir = Join-Path $fwDir 'WPF'

# ---------------------------------------------------------------- 检查输入
if (-not (Test-Path $srcDir)) {
    Write-Host "找不到源码目录: $srcDir" -ForegroundColor Red
    exit 1
}
$sources = @(Get-ChildItem -Path $srcDir -Filter '*.cs' -File | Sort-Object Name)
if ($sources.Count -eq 0) {
    Write-Host "src 目录下没有 .cs 源码: $srcDir" -ForegroundColor Red
    exit 1
}

$manifest = Join-Path $dir 'app.manifest'
$icon = Join-Path $dir 'app.ico'

# ---------------------------------------------------------------- 组装编译参数
$cscArgs = @()
$cscArgs += '/nologo'
$cscArgs += '/target:winexe'
$cscArgs += '/platform:anycpu'
$cscArgs += '/optimize+'
$cscArgs += ('/out:"' + $out + '"')
$cscArgs += '/win32icon:"' + $icon + '"'
if (Test-Path $manifest) { $cscArgs += '/win32manifest:"' + $manifest + '"' }

# .NET Framework 自带程序集
$refs = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Xaml.dll')
foreach ($r in $refs) {
    $p = Join-Path $fwDir $r
    if (Test-Path $p) { $cscArgs += '/r:"' + $p + '"' }
}
# WPF 程序集
foreach ($r in @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll')) {
    $p = Join-Path $wpfDir $r
    if (Test-Path $p) { $cscArgs += '/r:"' + $p + '"' }
}

$cscArgs += ($sources | ForEach-Object { '"' + $_.FullName + '"' })

# ---------------------------------------------------------------- 编译
if (Test-Path $out) { Remove-Item $out -Force -ErrorAction SilentlyContinue }
Write-Host "编译 $($sources.Count) 个源文件 …" -ForegroundColor Cyan

$output = & $csc @cscArgs 2>&1
$code = $LASTEXITCODE
$output | Out-File -FilePath $log -Encoding UTF8

if ($code -ne 0 -or -not (Test-Path $out)) {
    Write-Host ''
    Write-Host '❌ 编译失败，现有 SideStack.exe 未被修改。' -ForegroundColor Red
    Write-Host '--------------- 编译器输出 ---------------' -ForegroundColor DarkYellow
    $output | ForEach-Object { Write-Host $_ }
    Write-Host '-----------------------------------------' -ForegroundColor DarkYellow
    Write-Host "完整输出: $log"
    exit 1
}
Write-Host '编译成功。' -ForegroundColor Green

# ---------------------------------------------------------------- 结束旧实例
$running = Get-Process -Name 'SideStack' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "检测到 SideStack 正在运行（PID $($running.Id -join ', ')）。" -ForegroundColor Yellow
    if ($Restart) {
        Write-Host '按 -Restart 结束旧实例 …'
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 800
        $running = Get-Process -Name 'SideStack' -ErrorAction SilentlyContinue
    }
    else {
        Write-Host '替换 exe 需要先关闭它：请右键托盘图标退出，或改用 .\build.ps1 -Restart' -ForegroundColor Yellow
        Write-Host "新版本已编译到: $out（可直接改名替换）" -ForegroundColor Yellow
        exit 2
    }
}

# ---------------------------------------------------------------- 备份 + 替换
if (-not $NoBackup -and (Test-Path $exe)) {
    try {
        Copy-Item $exe $bak -Force
        Write-Host "已备份: $([IO.Path]::GetFileName($bak))"
    }
    catch { Write-Host "备份失败（继续替换）: $($_.Exception.Message)" -ForegroundColor Yellow }
}

try {
    Move-Item $out $exe -Force
}
catch {
    Write-Host "替换 exe 失败: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "新版本保留在: $out" -ForegroundColor Yellow
    exit 1
}

$fi = Get-Item $exe
Write-Host ("✅ 已生成 {0}（{1:N0} 字节，{2}）" -f $fi.Name, $fi.Length, $fi.LastWriteTime) -ForegroundColor Green

# ---------------------------------------------------------------- 启动
if ($Restart) {
    Start-Process -FilePath $exe
    Start-Sleep -Milliseconds 1200
    $p = Get-Process -Name 'SideStack' -ErrorAction SilentlyContinue
    if ($p) { Write-Host "已启动新版本（PID $($p.Id -join ', ')）" -ForegroundColor Green }
    else { Write-Host '启动似乎没有成功，请手动双击 SideStack.exe 并查看日志。' -ForegroundColor Yellow }
}
else {
    Write-Host '未启动（需要的话双击 SideStack.exe，或加 -Restart 重新跑一次）。'
}
exit 0

