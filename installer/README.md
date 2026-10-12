# SideStack 安装包（MSI）打包说明

用 [WiX Toolset v3](https://github.com/wixtoolset/wix3) 把 `SideStack.exe` 打成标准 MSI 安装包。
产物：`..\dist\SideStack-<版本>-x64.msi`。

## 一键打包

双击项目根目录的 `build-msi.cmd`，或在 PowerShell 里执行：

```powershell
.\build-msi.ps1                  # 打包现有 SideStack.exe → ..\dist\SideStack-1.0.0-x64.msi
.\build-msi.ps1 -Version 1.0.1   # 指定版本号（必须是 x.y.z）
.\build-msi.ps1 -Rebuild         # 先调用 ..\build.ps1 重新编译 exe，再打包
.\build-msi.ps1 -WixDir D:\wix314  # 手动指定 WiX 目录（含 candle.exe / light.exe）
.\build-msi.ps1 -NoIce           # 跳过 ICE 校验（只用于快速排错）
```

本机没有 WiX 时脚本会自动下载 `wix314-binaries.zip` 到 `%LOCALAPPDATA%\SideStackBuildTools\wix314`
（查找顺序：`-WixDir` → 环境变量 `WIX` → 上面的缓存目录 → `Program Files\WiX Toolset v3.14\bin` → `PATH`）。
脚本不修改 `SideStack.exe`，只是把它复制进安装包；源码比 exe 新时会给出提示（用 `-Rebuild` 可先重编译）。

## 文件说明

| 文件 | 作用 |
|---|---|
| `SideStack.wxs` | WiX 源文件：产品信息、目录结构、组件、功能（可选安装项）、中文安装界面 |
| `license-zh.txt` | 许可协议正文（纯文本，改完重新打包即生效；脚本自动转成 RTF 塞进安装界面） |
| `build-msi.ps1` | 打包脚本（生成 RTF → candle 编译 → light 链接 + ICE 校验） |
| `obj\` | 中间产物：`SideStack.wixobj`、`SideStack.wixpdb`、生成的 `license.rtf` |
| `..\dist\` | 最终产物目录 |

## 安装形态：按用户安装（per-user，不需要管理员权限）

| 项目 | 位置 |
|---|---|
| 程序目录 | `%LocalAppData%\Programs\SideStack\SideStack.exe` |
| 开始菜单 | `%AppData%\Microsoft\Windows\Start Menu\Programs\SideStack\SideStack.lnk` |
| 桌面快捷方式 | `%UserProfile%\Desktop\SideStack.lnk`（可选，默认勾选） |
| 开机自启动 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `SideStack` 值（可选，默认勾选） |
| 卸载入口 | 设置 → 应用 → 已安装的应用（卸载不需要提权） |
| 程序配置/日志 | `%APPDATA%\SideStack\`（安装包不碰它，卸载时也保留） |

安装界面为简体中文（`-cultures:zh-CN`），用 `WixUI_FeatureTree`：
许可协议 → 自定义安装（可勾选「桌面快捷方式」「开机自动启动」）→ 确认 → 安装 → 完成（可勾选「立即运行 SideStack」）。

**为什么按用户装**：SideStack 是托盘小工具，配置写在 `%APPDATA%`、自启写在 `HKCU\Run`，本来就是一个用户一份；
按用户安装可以免 UAC、免管理员，装/卸都干净。若确实需要装到 `Program Files`（全机器可用），
把 `SideStack.wxs` 里的 `InstallScope="perUser"` 改成 `perMachine`，目录树改成
`ProgramFiles64Folder\SideStack`，并把各组件的 `RegistryValue Root="HKCU"` 改成 `HKLM` 即可。

## 设计要点（改安装包时请注意）

* **升级**：`UpgradeCode` 固定为 `BC430B88-D468-4440-8A52-DC6C2C92FD8C`，只要不改它，新版本 MSI 会先卸载旧版再安装；
  版本号只允许三段（`x.y.z`，major/minor ≤ 255）。已装更高版本时会被 `MajorUpgrade` 挡下并给出中文提示。
* **组件 GUID 固定**：`E6C0EAC5…`（主程序）、`9E9CAFC1…`（开始菜单）、`4707A164…`（桌面）、`3F63D2B7…`（自启）。
  新增/删除文件后要同步改 `SideStack.wxs`。
* **每个组件都用 HKCU 注册表值作 KeyPath**：安装目录在用户配置文件夹下，这是 ICE38/ICE43 的要求；
  已创建的用户目录都在卸载时删除（ICE64）。
* **目录 Id 不能用标准文件夹保留字**：自定义的开始菜单目录用 `SideStackMenuFolder`。
  早期写成 `StartMenuFolder`（Windows Installer 的“开始菜单根目录”保留属性）时，快捷方式会跑到开始菜单根目录去。
* **ICE 抑制**：`ICE91`（per-user 包的“文件不随 ALLUSERS 变化”是预期行为）、
  `ICE61`（用了 `AllowSameVersionUpgrades`，同版本重装也先卸载旧版）。其余 ICE 全部开启。
* 安装包**不含** `src\` 目录，所以装好后不会触发「源码改动即时生效」的自动重编译，是一个冻结版本。
* **前置条件（.NET Framework）**：程序本身只要求 .NET Framework 4.0（整个工程可以对 4.0 的运行时程序集编译通过），
  但 MSI 里用注册表检索做了**装前检测**：读 `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full` 的
  `Version`（如 `4.8.09221`）与 `Release`（如 `533509`），要求 **4.8（`Release >= 528040`）**，
  并额外用 `Version >= 4.5` 兜一层。不满足时**在向导之前**弹出中文提示（附下载链接）并中止，
  不会出现"装完 exe 双击起不来"（那种情况 Windows 只会给一句英文报错）。
  之所以不用 4.0 作门槛：Windows 10 各版本自带 1507→4.6、1607→4.6.2、1709→4.7.1、1803+→4.7.2、Win11→4.8.1，
  所以 4.8 不会挡住任何受支持的 Windows，却能挡住被精简过运行库的系统。
  要放宽（例如兼容 Windows 7/8.1）就改 `SideStack.wxs` 里那条 `Condition` 的 `#528040`，
  取值对照：`378389`=4.5、`393295`=4.6、`460798`=4.7、`461808`=4.7.2、`533320`=4.8.1。
  **两个坑（都踩过）**：① WiX 3.14 的 schema 里**没有** `<LaunchCondition>` 元素，candle 会报
  `CNDL0005`，要用 `Product` 下的 `<Condition Message="…">表达式</Condition>`（表达式为假则中止）；
  ② 别用 `WixNetFxExtension` 的 `NETFRAMEWORK45` 属性 —— 实测它不会被注入，条件恒为假，
  连装了 4.8.1 的机器都会被拦住（实测安装返回 1603）。所以本包只用注册表检索，不依赖该扩展。

## 图标（app.ico）

`app.ico` 由脚本生成，别手工替换：

```powershell
python tools\make_icon.py                 # 生成 app.ico（覆盖前备份 app.ico.bak）+ 预览
python tools\make_icon.py --preview-only  # 只出预览，不动 app.ico
```

设计：鲜艳蓝→紫渐变圆角方块（`#4C8DF6 → #2B6CE8 → #7C3AED`），左侧一块玻璃面板
（屏幕侧边的 Dock），三张颜色鲜艳的卡片（珊瑚红 `#FF5A5F` / 琥珀黄 `#FFC93C` /
翡翠绿 `#2BD97C`）从右上斜向流进面板 —— 前排最大的珊瑚卡是视觉主体，一眼能看出
「桌面图标被收进侧边面板」。所有尺寸都按画布比例定义，任意缩放都保持同一构图；
按尺寸分三档：

| 尺寸 | 档位 | 处理 |
|---|---|---|
| ≥ 40px | detail | 玻璃面板带描边，卡片带顶部高光 + 柔和投影 |
| 24 ~ 32px | bold | 面板更实、去掉高光投影（平涂更锐利），卡片整体放大 1.05 |
| ≤ 20px | mini | 同上但放大 1.08 —— 16px 下三张卡各自只剩几像素，靠放大保住"鲜艳显眼" |

配色刻意拉开：底蓝紫、面板白、卡片红/黄/绿，共 5 个色相，深浅底上都不糊。
预览图输出到 `.icon-preview\`（`sheet.png` 尺寸一览、`context.png` 深浅底观感、
`alternatives.png` 其他备选方案）。ICO 内含 10 个尺寸（16~256）：≥96px 用 PNG 压缩、
其余 32bpp BMP，兼顾体积与兼容性。改完图标要重跑 `build.ps1`（图标编进 exe：托盘 /
任务栏 / 资源管理器）和 `build-msi.ps1`（图标进快捷方式与「应用和功能」）。

## 已验证（2026-09-30，Windows 11 26200 / Windows Installer 5.0）

* 静默安装 `msiexec /i SideStack-1.0.0-x64.msi /qn` → 退出码 0，全程无需管理员权限；
  装的 `SideStack.exe` 与源码编译产物 SHA256 完全一致。
* 快捷方式（开始菜单 / 桌面）目标、工作目录、图标正确；`HKCU\...\Run\SideStack` 写入已安装路径。
* 「应用和功能」里显示 `SideStack 1.0.0`（发布者、图标、大小、卸载/修改入口齐全）。
* 修改安装（`REMOVE=DesktopShortcut,AutoStart` / `ADDLOCAL=…`）与修复（`/fomus`）均正常。
* 升级 1.0.0 → 1.0.1 正常（旧版被移除，只剩一条 ARP 记录）；用 1.0.0 覆盖 1.0.1 被正确拦下（退出码 1603，中文提示，现有安装不受影响）。
* 卸载后：程序目录、快捷方式、`HKCU\Run` 自启项、`HKCU\Software\SideStack` 标记、图标缓存、ARP 记录全部清除，无残留。
* 许可协议 RTF 经 RichTextBox 渲染校验，中文显示正常（脚本生成的是 `\uNNNN` 转义，不依赖代码页）。
* ICE 校验仅剩上面两条已说明的抑制项，其余全部通过。
