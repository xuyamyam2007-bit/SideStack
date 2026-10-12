<#
  SideStack → MSI 安装包打包脚本

  用法（在 installer 目录下打开 PowerShell 执行，或直接双击 ..\build-msi.cmd）：
    .\build-msi.ps1                     打包 1.0.0（自动定位 WiX，缺失时自动下载到本机缓存）
    .\build-msi.ps1 -Version 1.0.1      指定版本号（必须是 x.y.z）
    .\build-msi.ps1 -Rebuild            先调用 ..\build.ps1 重新编译 SideStack.exe 再打包
    .\build-msi.ps1 -WixDir D:\wix314   指定 WiX Toolset 3.x 目录（含 candle.exe / light.exe）
    .\build-msi.ps1 -NoIce              跳过 ICE 校验（仅用于快速出包排错）

  产物：<项目根>\dist\SideStack-<版本>-x64.msi
  安装形态：按用户安装（%LocalAppData%\Programs\SideStack），不需要管理员权限。
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$SourceExe,
    [string]$OutputDir,
    [string]$WixDir,
    [string]$WixCacheDir,
    [string]$WixUrl = 'https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314-binaries.zip',
    [switch]$Rebuild,
    [switch]$NoIce
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$installerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $installerDir
$objDir = Join-Path $installerDir 'obj'
if (-not $OutputDir) { $OutputDir = Join-Path $projectDir 'dist' }
if (-not $SourceExe) { $SourceExe = Join-Path $projectDir 'SideStack.exe' }
if (-not $WixCacheDir) { $WixCacheDir = Join-Path $env:LOCALAPPDATA 'SideStackBuildTools' }

Write-Host '=== SideStack → MSI ===' -ForegroundColor Cyan
Write-Host "项目目录: $projectDir"

$wixUiExt = 'WixUIExtension.dll'
$wixUtilExt = 'WixUtilExtension.dll'

# ---------------------------------------------------------------- 版本号
if ($Version -notmatch '^(\d{1,3})\.(\d{1,3})\.(\d{1,5})$') {
    throw "版本号 '$Version' 不是 x.y.z 形式（MSI 只支持三段版本，且 major/minor ≤ 255、build ≤ 65535）。"
}
$msiVersion = $Version
Write-Host "版本号  : $msiVersion"

# ---------------------------------------------------------------- 源 exe
if ($Rebuild) {
    Write-Host '按 -Rebuild 先重新编译 SideStack.exe …' -ForegroundColor Cyan
    & (Join-Path $projectDir 'build.ps1') -NoBackup
    if ($LASTEXITCODE -ne 0) {
        throw "重新编译失败（build.ps1 退出码 $LASTEXITCODE）。若提示 SideStack 正在运行，请先退出托盘里的 SideStack 再重试。"
    }
}
if (-not (Test-Path $SourceExe)) {
    throw "找不到要打包的程序: $SourceExe（可先用 ..\build.ps1 编译，或加 -Rebuild）"
}
$exeItem = Get-Item $SourceExe

# 源码比 exe 新时提醒（安装包里的 exe 应当是当前源码的编译结果）
$newer = @()
foreach ($f in @(Get-ChildItem (Join-Path $projectDir 'src') -Filter '*.cs' -File -ErrorAction SilentlyContinue) +
               @(Get-Item (Join-Path $projectDir 'app.manifest') -ErrorAction SilentlyContinue) +
               @(Get-Item (Join-Path $projectDir 'app.ico') -ErrorAction SilentlyContinue)) {
    if ($f.LastWriteTime -gt $exeItem.LastWriteTime) { $newer += $f.Name }
}
if ($newer.Count -gt 0) {
    Write-Host ("提示: 以下文件比 SideStack.exe 新 -> " + ($newer -join ', ')) -ForegroundColor Yellow
    Write-Host '      打包进去的仍是现有 exe；要打包最新源码请加 -Rebuild。' -ForegroundColor Yellow
}
Write-Host ("源程序  : {0}（{1:N0} 字节，{2}）" -f $exeItem.Name, $exeItem.Length, $exeItem.LastWriteTime)

# ---------------------------------------------------------------- 定位 WiX
function Test-WixDir([string]$dir) {
    if (-not $dir) { return $false }
    return (Test-Path (Join-Path $dir 'candle.exe')) -and (Test-Path (Join-Path $dir 'light.exe'))
}

function Find-Wix {
    param([string]$Hint)
    if ($Hint) {
        if (Test-WixDir $Hint) { return $Hint }
        throw "指定的 -WixDir 里没有 candle.exe / light.exe: $Hint"
    }
    $cands = @()
    if ($env:WIX) { $cands += $env:WIX }
    $cands += (Join-Path $WixCacheDir 'wix314')
    $cands += (Join-Path $env:ProgramFiles 'WiX Toolset v3.14\bin')
    $cands += (Join-Path ${env:ProgramFiles(x86)} 'WiX Toolset v3.14\bin')
    if (${env:ProgramFiles(x86)}) {
        $cands += (Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'WiX Toolset*') -Directory -ErrorAction SilentlyContinue |
                   ForEach-Object { Join-Path $_.FullName 'bin' })
    }
    foreach ($c in $cands) { if (Test-WixDir $c) { return $c } }
    $onPath = Get-Command candle.exe -ErrorAction SilentlyContinue
    if ($onPath) { return (Split-Path -Parent $onPath.Source) }
    return $null
}

$wix = Find-Wix -Hint $WixDir
if (-not $wix) {
    $dest = Join-Path $WixCacheDir 'wix314'
    $zip = Join-Path $WixCacheDir 'wix314-binaries.zip'
    Write-Host "本机没有 WiX Toolset 3.x，下载到 $dest …" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $WixCacheDir | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    if (-not (Test-Path $zip)) {
        Invoke-WebRequest -Uri $WixUrl -OutFile $zip -UseBasicParsing
    }
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $dest -Force
    $wix = Find-Wix -Hint $dest
}
$candle = Join-Path $wix 'candle.exe'
$light = Join-Path $wix 'light.exe'
Write-Host "WiX     : $wix"

# ---------------------------------------------------------------- 许可协议 RTF
function ConvertTo-RtfEscaped {
    param([string]$Text)
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int][char]$ch
        if ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($ch -eq '{') { [void]$sb.Append('\{') }
        elseif ($ch -eq '}') { [void]$sb.Append('\}') }
        elseif ($code -lt 128) { [void]$sb.Append($ch) }
        else { [void]$sb.Append('\u' + $code + '?') }
    }
    $sb.ToString()
}

New-Item -ItemType Directory -Force -Path $objDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$licenseTxt = Join-Path $installerDir 'license-zh.txt'
$licenseRtf = Join-Path $objDir 'license.rtf'
if (-not (Test-Path $licenseTxt)) { throw "找不到许可协议文本: $licenseTxt" }
$lines = @(Get-Content -Path $licenseTxt -Encoding UTF8)
$title = ($lines | Where-Object { $_.Trim() -ne '' } | Select-Object -First 1)
$body = $lines | Select-Object -Skip 1
$rtfLines = New-Object System.Collections.Generic.List[string]
$rtfLines.Add('{\rtf1\ansi\ansicpg936\deff0\nouicompat{\fonttbl{\f0\fnil\fcharset134 Microsoft YaHei;}}')
$rtfLines.Add('\viewkind4\uc1\pard\f0\fs20\b ' + (ConvertTo-RtfEscaped $title) + '\b0\par')
foreach ($l in $body) { $rtfLines.Add('\pard\f0\fs18 ' + (ConvertTo-RtfEscaped $l) + '\par') }
$rtfLines.Add('}')
[IO.File]::WriteAllText($licenseRtf, ($rtfLines -join "`r`n"), [System.Text.Encoding]::ASCII)
Write-Host "许可协议: $licenseRtf"

# ---------------------------------------------------------------- 编译 wxs
$wixobj = Join-Path $objDir 'SideStack.wixobj'
$wixpdb = Join-Path $objDir 'SideStack.wixpdb'
$msiName = "SideStack-$msiVersion-x64.msi"
$msiPath = Join-Path $OutputDir $msiName

$candleArgs = @(
    '-nologo'
    '-arch'; 'x64'
    '-ext'; (Join-Path $wix $wixUiExt)
    '-ext'; (Join-Path $wix $wixUtilExt)
    "-dVersion=$msiVersion"
    "-dSourceExe=$SourceExe"
    "-dProjectDir=$projectDir\"
    "-dLicenseRtf=$licenseRtf"
    "-dBuildDir=$objDir\"
    '-out'; $wixobj
    (Join-Path $installerDir 'SideStack.wxs')
)
Write-Host '编译 WiX 源 …' -ForegroundColor Cyan
& $candle @candleArgs
if ($LASTEXITCODE -ne 0) { throw "candle.exe 失败（退出码 $LASTEXITCODE）" }

# ---------------------------------------------------------------- 链接 MSI
$lightArgs = @(
    '-nologo'
    '-ext'; (Join-Path $wix $wixUiExt)
    '-ext'; (Join-Path $wix $wixUtilExt)
    '-cultures:zh-CN'
)
if ($NoIce) {
    $lightArgs += '-sval'
} else {
    # ICE91: 本包声明为 perUser（InstallScope="perUser"），"文件不随 ALLUSERS 变化" 属预期行为
    $lightArgs += '-sice:ICE91'
    # ICE61: 用了 AllowSameVersionUpgrades（同版本重装也先卸载旧版），"MaxVersion = 当前版本" 是预期行为
    $lightArgs += '-sice:ICE61'
    # ICE38: 主程序组件故意用 exe 文件作 KeyPath（安装目录允许用户自定义，文件 KeyPath 才会
    #        把绝对路径记进组件注册表，卸载时才能删掉自定义目录里的 exe）；本包只做按用户安装
    $lightArgs += '-sice:ICE38'
}
# .wixpdb（调试符号）放到 obj，保持 dist 里只有安装包
$lightArgs += @('-out'; $msiPath; '-pdbout'; (Join-Path $objDir 'SideStack.wixpdb'); $wixobj)
Write-Host '链接 MSI …' -ForegroundColor Cyan
& $light @lightArgs
if ($LASTEXITCODE -ne 0) { throw "light.exe 失败（退出码 $LASTEXITCODE）" }

# ---------------------------------------------------------------- 结果
$msiItem = Get-Item $msiPath
$hash = (Get-FileHash -Path $msiPath -Algorithm SHA256).Hash
Write-Host ''
Write-Host ("OK 已生成 {0}（{1:N0} 字节）" -f $msiItem.FullName, $msiItem.Length) -ForegroundColor Green
Write-Host ("SHA256: {0}" -f $hash)
try {
    $inst = New-Object -ComObject WindowsInstaller.Installer
    $db = $inst.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $inst, @($msiPath, 0))
    $sql = 'SELECT `Property`,`Value` FROM `Property` WHERE `Property` = ''ProductCode'' OR `Property` = ''ProductName'' OR `Property` = ''ProductVersion'''
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @($sql))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    while ($true) {
        $rec = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $rec) { break }
        $k = $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(1))
        $v = $rec.GetType().InvokeMember('StringData', 'GetProperty', $null, $rec, @(2))
        Write-Host ("{0} = {1}" -f $k, $v)
    }
} catch { }
exit 0
