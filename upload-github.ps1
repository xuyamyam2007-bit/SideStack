# ============================================================================
#  SideStack 一键发布到 GitHub
#
#  做四件事：① 编译 exe  ② 打 MSI  ③ 同步到上传仓库并规范化行尾  ④ 提交推送
#
#  用法（可在任意目录执行）：
#     .\upload-github.ps1                        # 编译+打包+同步+提交+推送（提交前确认）
#     .\upload-github.ps1 -Version 1.0.2         # 同时创建并推送 tag v1.0.2
#     .\upload-github.ps1 -Message "fix: xxx"    # 自定义提交信息
#     .\upload-github.ps1 -DryRun                # 全套演练但不提交不推送
#     .\upload-github.ps1 -BuildOnly             # 只编译+打包，不动仓库
#     .\upload-github.ps1 -Yes                   # 跳过确认（无人值守）
#     .\upload-github.ps1 -SkipBuild             # 不重编译，只同步+提交+推送
#     .\upload-github.ps1 -NoPush                # 只本地提交
#
#  本脚本内建了本工程踩过的坑：
#    · .ps1 必须带 UTF-8 BOM（Windows PowerShell 5.1 会把无 BOM 的中文按 ANSI 读，直接语法错）
#    · git 可能不在 PATH（装完 Git 需新开终端）；脚本会去常见安装目录找
#    · 系统级 core.autocrlf=true 会让 LF 文件在检出时变 CRLF，造成满屏"假改动"
#      → 仓库根放 .gitattributes（* text=auto eol=lf）一劳永逸；脚本也会自动补上
#    · 仓库若残留失效代理，推送连不上 → 自动检测端口并清除
#    · 远端被网页「Add files via upload」改过会分叉 → 自动 fetch 并合并
#    · 上传仓库是独立目录，绝不在项目目录里 commit（避免把 .bak / obj 一起传上去）
#
#  退出码：0 成功 / 1 环境或参数错 / 2 用户取消 / 3 编译或打包失败 / 4 推送失败
# ============================================================================

[CmdletBinding()]
param(
    [string]$Version,
    [string]$Message,
    [string]$PublishDir,
    [string]$Remote = 'origin',
    [string]$Branch = 'main',
    [switch]$DryRun,
    [switch]$Yes,
    [switch]$SkipBuild,
    [switch]$BuildOnly,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Say($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c }
function Step($n, $t) { Write-Host ''; Write-Host ("[" + $n + "] " + $t) -ForegroundColor Cyan }
function Ok($t) { Write-Host ("    OK   " + $t) -ForegroundColor Green }
function Warn($t) { Write-Host ("    注意 " + $t) -ForegroundColor Yellow }
function Die($t, $code) { Write-Host ("    失败 " + $t) -ForegroundColor Red; exit $code }

# ---------------------------------------------------------------- git 包装
function Run-Git {
    param([string[]]$GitArgs, [switch]$Capture, [switch]$AllowFail)
    # 经 cmd /c 调用：git 的进度/警告走 stderr，直接 & git 2>&1 在 Stop 模式下会抛异常
    $line = '"' + $script:Git + '" -C "' + $script:RepoDir + '"'
    foreach ($a in $GitArgs) { $line += ' "' + ([string]$a).Replace('"', '\"') + '"' }
    $out = cmd /c ($line + ' 2>&1')
    $code = $LASTEXITCODE
    if ($null -eq $out) { $out = @() } elseif ($out -isnot [array]) { $out = @($out) }
    $out = @($out | ForEach-Object { [string]$_ } | Where-Object { $_ -ne '' })
    if ($code -ne 0 -and -not $AllowFail) {
        Write-Host ("    git " + ($GitArgs -join ' ') + " 失败（退出码 " + $code + "）") -ForegroundColor Red
        $out | ForEach-Object { Write-Host ("      " + $_) -ForegroundColor DarkRed }
        exit 4
    }
    if ($Capture) { return ($out -join "`n") }
    $out | ForEach-Object { Say ("    " + $_) }
    return $null
}

# ---------------------------------------------------------------- 0. 环境
Step 0 '环境检查'

$ProjDir = $PSScriptRoot
if (-not $ProjDir) { $ProjDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not (Test-Path (Join-Path $ProjDir 'src'))) { Die ("不是 SideStack 项目根目录（找不到 src）: " + $ProjDir) 1 }
Say ("    项目目录 : " + $ProjDir)

$script:Git = $null
$g = Get-Command git.exe -ErrorAction SilentlyContinue
if ($g) { $script:Git = $g.Source }
if (-not $script:Git) {
    foreach ($c in @(
            (Join-Path $env:ProgramFiles 'Git\cmd\git.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Git\cmd\git.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe'),
            (Join-Path $env:ProgramFiles 'Git\bin\git.exe'))) {
        if ($c -and (Test-Path -LiteralPath $c)) { $script:Git = $c; break }
    }
}
if (-not $script:Git) { Die '找不到 git.exe，请先安装 Git for Windows' 1 }
Say ("    git      : " + $script:Git)

if (-not $PublishDir) { $PublishDir = $ProjDir }   # 默认：项目目录本身就是 git 仓库（2026-10 已合并）
if (-not (Test-Path -LiteralPath $PublishDir)) { Die ("上传目录不存在: " + $PublishDir + "（用 -PublishDir 指定）") 1 }
$script:RepoDir = (Resolve-Path -LiteralPath $PublishDir).Path
$script:SameDir = ($script:RepoDir.TrimEnd('\') -ieq $ProjDir.TrimEnd('\'))
Say ("    上传目录 : " + $script:RepoDir + $(if ($script:SameDir) { '（= 项目目录，跳过同步步骤）' } else { '' }))
if (-not (Test-Path (Join-Path $script:RepoDir '.git'))) { Die ("上传目录不是 git 仓库: " + $script:RepoDir) 1 }
if ($Version -and $Version -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,5}$') { Die ("版本号必须是 x.y.z: " + $Version) 1 }

# 找 Python（用于二进制安全的行尾规范化；找不到就跳过这步）
$script:Python = $null
foreach ($c in @(
        (Join-Path $env:USERPROFILE '.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'),
        'python.exe', 'python3.exe')) {
    if ($c -like '*\*') { if (Test-Path -LiteralPath $c) { $script:Python = $c; break } }
    else { $cmd = Get-Command $c -ErrorAction SilentlyContinue; if ($cmd) { $script:Python = $cmd.Source; break } }
}
if ($script:Python) { Say ("    python   : " + $script:Python) } else { Warn '没找到 python，跳过行尾规范化' }

# ---------------------------------------------------------------- 1. 编译
Step 1 '编译 SideStack.exe'
if ($SkipBuild) {
    Warn '按 -SkipBuild 跳过编译'
} else {
    foreach ($p in @(Get-Process -Name SideStack -ErrorAction SilentlyContinue)) {
        Say ("    结束运行中的实例 PID " + $p.Id)
        try { Stop-Process -Id $p.Id -Force } catch { }
    }
    Start-Sleep -Milliseconds 800

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjDir 'build.ps1') 2>&1 |
        ForEach-Object { Say ("    " + $_) }
    $rc = $LASTEXITCODE

    $exe = Join-Path $ProjDir 'SideStack.exe'
    $newExe = Join-Path $ProjDir 'SideStack.exe.new'
    if (Test-Path $newExe) {
        if ((Get-FileHash $exe).Hash -ne (Get-FileHash $newExe).Hash) {
            Copy-Item $newExe $exe -Force
            Say '    用 SideStack.exe.new 覆盖 SideStack.exe'
        }
        Remove-Item $newExe -Force
    }
    if ($rc -ne 0 -and $rc -ne 2) { Die ('编译失败（build.ps1 退出码 ' + $rc + '）') 3 }
    Ok ('exe ' + (Get-Item $exe).Length + ' 字节')
}

# ---------------------------------------------------------------- 2. 打 MSI
Step 2 '打 MSI 安装包'
$msiPath = $null
if ($SkipBuild) {
    Warn '按 -SkipBuild 跳过打包（上传目录里已有的 MSI 不会被改动）'
} else {
    $ver = if ($Version) { $Version } else { '1.0.0' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjDir 'installer\build-msi.ps1') `
        -Version $ver -OutputDir (Join-Path $script:RepoDir 'dist') 2>&1 | ForEach-Object { Say ("    " + $_) }
    if ($LASTEXITCODE -ne 0) { Die ('打 MSI 失败（退出码 ' + $LASTEXITCODE + '）') 3 }
    $msiPath = Join-Path $script:RepoDir ('dist\SideStack-' + $ver + '-x64.msi')
    if (-not (Test-Path -LiteralPath $msiPath)) { Die ('没有生成预期的 MSI: ' + $msiPath) 3 }
    Ok ((Split-Path -Leaf $msiPath) + '  ' + (Get-Item $msiPath).Length + ' 字节')
}

if ($BuildOnly) {
    Write-Host ''
    Ok '只构建（-BuildOnly）：仓库未改动'
    exit 0
}

# ---------------------------------------------------------------- 3. 同步到上传仓库
Step 3 '同步文件到上传目录'
$copied = 0
$removed = 0
if ($script:SameDir) {
    Warn '上传目录就是项目目录：无需同步（工作区里是什么就提交什么）'
} else {
function Sync-Item($rel) {
    $s = Join-Path $ProjDir $rel
    if (-not (Test-Path -LiteralPath $s)) { Warn ('缺少 ' + $rel); return }
    $d = Join-Path $script:RepoDir $rel
    $dd = Split-Path -Parent $d
    if (-not (Test-Path -LiteralPath $dd)) { New-Item -ItemType Directory -Path $dd -Force | Out-Null }
    if ((Test-Path -LiteralPath $d) -and ((Get-FileHash $s).Hash -eq (Get-FileHash $d).Hash)) { return }
    Copy-Item -LiteralPath $s -Destination $d -Force
    Say ("    " + $rel)
    $script:copied++
}

# 上传内容 = 项目目录里的**所有**项目文件（源码、脚本、工具、文档、安装包都包含）。
# 哪些不该上传由仓库根的 .gitignore 决定（只忽略可再生的产物与噪声），
# 因此这里不再维护白名单：目录里有什么就同步什么，不再有"附加内容"之分。
$allTop = @('SideStack.exe', 'build.ps1', 'build.cmd', 'build-msi.cmd', 'app.ico', 'app.manifest',
            'README.md', 'LICENSE', '.gitignore', '.gitattributes',
            'upload-github.ps1', 'upload.cmd', 'installer\README.md', 'tools\make_icon.py',
            ([string]([char]0x4F7F + [char]0x7528 + [char]0x8BF4 + [char]0x660E) + '.md'))
foreach ($f in $allTop) { if (Test-Path -LiteralPath (Join-Path $ProjDir $f)) { Sync-Item $f } }
foreach ($f in (Get-ChildItem (Join-Path $ProjDir 'src') -Filter '*.cs' -File | Sort-Object Name)) { Sync-Item ('src\' + $f.Name) }
foreach ($f in (Get-ChildItem (Join-Path $ProjDir 'installer') -File | Sort-Object Name)) { Sync-Item ('installer\' + $f.Name) }
foreach ($f in (Get-ChildItem (Join-Path $ProjDir 'tools') -File | Sort-Object Name)) { Sync-Item ('tools\' + $f.Name) }
Ok ('同步 ' + $copied + ' 个文件（其余无变化）')
}

# 仓库级行尾策略：一劳永逸地压住系统 core.autocrlf=true 造成的"假改动"
$gaPath = Join-Path $script:RepoDir '.gitattributes'
if (-not (Test-Path -LiteralPath $gaPath)) {
    $gaText = "* text=auto eol=lf`n`n*.exe binary`n*.ico binary`n*.msi binary`n*.dll binary`n*.png binary`n"
    [IO.File]::WriteAllText($gaPath, $gaText, (New-Object System.Text.UTF8Encoding($false)))
    Run-Git @('add', '.gitattributes') | Out-Null
    Ok '已补上 .gitattributes（统一 LF）'
}

# 行尾规范化（二进制安全：交给 python，避免 PowerShell 5.1 的 Set-Content 加 BOM）
if ($script:Python) {
    $pyScript = @'
import os, sys
repo = sys.argv[1]
exts = ('.cs', '.ps1', '.cmd', '.md', '.wxs', '.txt', '.py', '.yml', '.yaml', '.manifest')
fixed = 0
for root, dirs, files in os.walk(repo):
    if '.git' in root.split(os.sep): continue
    for f in files:
        if not f.endswith(exts): continue
        p = os.path.join(root, f)
        b = open(p, 'rb').read()
        if b'\r\n' not in b: continue
        bom = b.startswith(b'\xef\xbb\xbf')
        body = b[3:] if bom else b
        body = body.replace(b'\r\n', b'\n')
        open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + body)
        fixed += 1
print(fixed)
'@
    $pyFile = Join-Path $env:TEMP 'sidestack-eol.py'
    [IO.File]::WriteAllText($pyFile, $pyScript, (New-Object System.Text.UTF8Encoding($false)))
    $fixedCount = (& $script:Python $pyFile $script:RepoDir) | Select-Object -Last 1
    Remove-Item $pyFile -Force -ErrorAction SilentlyContinue
    if ($fixedCount -match '^\d+$' -and [int]$fixedCount -gt 0) { Ok ('行尾规范化：' + $fixedCount + ' 个文件改回 LF') }
}

# 只保留当前版本的 MSI，且仅在本次真的产出了 MSI 时清理
# 只保留当前版本的 MSI，且仅在**显式指定 -Version** 时清理旧版本
# （不指定版本会默认打 1.0.0，此时若删掉 1.0.1 就成了版本倒退）
if ($msiPath -and $Version) {
    $keep = Split-Path -Leaf $msiPath
    Get-ChildItem (Join-Path $script:RepoDir 'dist') -Filter '*.msi' -File |
        Where-Object { $_.Name -ne $keep } |
        ForEach-Object { Say ('    移除历史 MSI（Release 里已有）: ' + $_.Name); Remove-Item $_.FullName -Force }
} elseif ($msiPath) {
    Warn ('未指定 -Version（本次打进包的是 ' + (Split-Path -Leaf $msiPath) + '），保留其他版本 MSI 不删')
}

# ---------------------------------------------------------------- 4. 远端与分叉
Step 4 '检查远端状态'
Run-Git @('fetch', $Remote, '--prune', '--tags') -AllowFail | Out-Null
$branchRef = "$Remote/$Branch"
Run-Git @('rev-parse', '--verify', '--quiet', $branchRef) -AllowFail | Out-Null
$hasRemote = ($LASTEXITCODE -eq 0)

if ($hasRemote) {
    $ahead = [int](Run-Git @('rev-list', '--count', "$branchRef..HEAD") -Capture)
    $behind = [int](Run-Git @('rev-list', '--count', "HEAD..$branchRef") -Capture)
    Say ('    本地领先 ' + $ahead + '，落后 ' + $behind)
    if ($behind -gt 0) {
        Warn '远端有新提交（可能是网页上传）'
        Say ('    ' + (Run-Git @('log', '--oneline', '-3', $branchRef) -Capture))
        if ($DryRun) { Warn '-DryRun：跳过合并' }
        else {
            Run-Git @('merge', $branchRef, '--no-edit', '-m', ('Merge ' + $branchRef + '（内容以本地为准）'))
            Ok '已合并远端提交'
        }
    }
} else {
    Warn ('远端还没有分支 ' + $Branch + '，将创建')
}

# 失效代理自检
$proxy = Run-Git @('config', '--get', 'http.proxy') -Capture -AllowFail
if ($proxy) {
    $port = 0; if ($proxy -match ':(\d+)') { $port = [int]$Matches[1] }
    $alive = $false
    if ($port -gt 0) { $alive = [bool](Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -eq $port }) }
    if (-not $alive) {
        Warn ('仓库代理 ' + $proxy + ' 无人监听，推送会失败 —— 已临时清除')
        Run-Git @('config', '--local', '--unset', 'http.proxy') -AllowFail | Out-Null
        Run-Git @('config', '--local', '--unset', 'https.proxy') -AllowFail | Out-Null
    } else { Say ('    代理 ' + $proxy + ' 在监听，保留') }
}

# ---------------------------------------------------------------- 5. 提交
Step 5 '提交改动'
Run-Git @('add', '-A') | Out-Null
$staged = Run-Git @('diff', '--cached', '--stat') -Capture
if (-not $staged) {
    Say '    没有需要提交的改动'
} else {
    Say $staged
    if (-not $Message) {
        $Message = if ($Version) { 'chore: 发布 ' + $Version } else { 'chore: 同步发布' }
    }
    if ($DryRun) {
        Warn ('-DryRun：本应提交「' + $Message + '」')
    } else {
        if (-not $Yes) {
            Write-Host ''
            Write-Host ('    即将提交并推送到 ' + $Remote + '/' + $Branch) -ForegroundColor Yellow
            $ans = Read-Host '    继续？(y/N)'
            if ($ans -notmatch '^(y|Y)') { Write-Host '    已取消' -ForegroundColor Yellow; exit 2 }
        }
        Run-Git @('-c', 'core.autocrlf=false', 'commit', '-m', $Message) | Out-Null
        Ok ('已提交 ' + (Run-Git @('log', '-1', '--oneline') -Capture))
    }
}

# ---------------------------------------------------------------- 6. 推送
Step 6 '推送到 GitHub'
if ($NoPush) { Warn '-NoPush：只提交，不推送' }
elseif ($DryRun) { Warn '-DryRun：跳过推送' }
else {
    $pushArgs = @('push', $Remote, $Branch)
    if ($Version) {
        $tagName = 'v' + $Version
        if (Run-Git @('tag', '-l', $tagName) -Capture) {
            Warn ('tag ' + $tagName + ' 已存在，未新建')
        } else {
            Run-Git @('tag', '-a', $tagName, '-m', ('SideStack ' + $Version)) | Out-Null
            Ok ('已建 tag ' + $tagName + '（推送后触发 CI 自动发 Release）')
        }
        $pushArgs += $tagName
    }
    Run-Git $pushArgs
    Ok '推送完成'
}

# ---------------------------------------------------------------- 7. 汇总
Step 7 '结果'
Say ('    提交   : ' + (Run-Git @('log', '-1', '--pretty=format:%h %s') -Capture))
if ($Version) { Say ('    版本   : ' + $Version + '（tag v' + $Version + '）') }
if ($msiPath -and (Test-Path -LiteralPath $msiPath)) {
    $f = Get-Item -LiteralPath $msiPath
    Say ('    MSI    : ' + $f.Name + '  ' + $f.Length + ' 字节')
    Say ('    SHA256 : ' + (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash)
}
$url = Run-Git @('remote', 'get-url', $Remote) -Capture
Say ('    远端   : ' + $url)
if ($Version -and -not $DryRun -and -not $NoPush) {
    Say ('    Release: ' + ($url -replace '\.git$', '') + '/releases/tag/v' + $Version) -ForegroundColor Green
}
Write-Host ''
Write-Host '完成。' -ForegroundColor Green
exit 0
