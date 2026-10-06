# SideStack

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

把桌面快捷方式收纳到屏幕左右两侧的隐藏式 Dock 面板 —— 桌面保持干净，需要时鼠标移到屏幕边缘就滑出来。

程序常驻**系统托盘**、不占任务栏、**不需要管理员权限**；面板支持经典玻璃 / 透亮 / 光感 / 光感·强 四种风格，
其中「光感」系列是纯 CPU 实现的液态玻璃（抓取面板背后的真实屏幕画面，做折射 + 色散 + 内阴影）。

## 运行要求

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10 / 11（部分效果依赖 Win10 2004+ 的 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`，更老的系统上《光感》背景会发糊） |
| 运行时 | .NET Framework 4.0 以上（Win10 自带 4.6+，Win11 自带 4.8.1，通常**无需额外安装**） |
| 隐私 | 全部功能在本机完成，不联网、不上传任何数据 |
| 权限 | 按用户运行（`asInvoker`），只读写 `%APPDATA%\SideStack\` 与 `HKCU` |

> MSI 安装包在安装前会检查 .NET Framework 版本，低于 **4.8** 时给出中文提示并中止（附下载链接），
> 不会出现"装完双击起不来"。程序本身只要 4.0，这个门槛只为给出友好提示。

## 使用

1. **直接运行**：双击 `SideStack.exe`。启动后不显示窗口，只在托盘常驻。
2. **呼出面板**：鼠标移到屏幕**最左 / 最右边缘**，对应侧面板滑入；鼠标移开后延迟自动收回。
3. **打开设置**：单击或双击托盘图标。
4. **退出**：托盘右键菜单 / 面板右键菜单 / 设置窗口里的「退出 SideStack」。
5. **随系统启动**：设置窗口里打开「开机自动启动」（写 `HKCU\...\Run`）。

设置项的完整说明、每一种面板风格的做法与实测开销、以及大量排障记录，见 **[使用说明.md](使用说明.md)**。

## 安装包（MSI）

`dist\SideStack-1.0.0-x64.msi` 是现成的安装包（按用户安装到 `%LocalAppData%\Programs\SideStack`，无需管理员）：

* 开始菜单快捷方式必装，桌面快捷方式与开机自启可选；
* 卸载走「设置 → 应用 → 已安装的应用」。

## 从源码构建

**前提**：Windows 自带的 .NET Framework 编译器 `csc.exe`（无需安装 Visual Studio / .NET SDK），
以及 WPF 程序集（系统自带）。工程**不依赖任何第三方库或原生 DLL**。

```powershell
:: 编译 exe（双击或命令行执行）
build.cmd                 :: 等价于 powershell -ExecutionPolicy Bypass -File build.ps1
build.cmd -Restart        :: 编译后结束旧实例并启动新版本
build.cmd -NoBackup       :: 不生成 SideStack.exe.bak

:: 打 MSI 安装包 → dist\SideStack-<版本>-x64.msi
build-msi.cmd                          :: 用现有 SideStack.exe 打包
build-msi.cmd -Rebuild                 :: 先重新编译 exe 再打包
build-msi.cmd -Version 1.0.1           :: 指定版本号
```

打包 MSI 需要 [WiX Toolset v3](https://github.com/wixtoolset/wix3)；本机没有时
`installer\build-msi.ps1` 会自动下载到 `%LOCALAPPDATA%\SideStackBuildTools\wix314`。

**源码改动即时生效**：程序启动时会比对 `src\*.cs` 与 `SideStack.exe` 的时间戳，源码更新则自动重编译并
平滑替换正在运行的实例。所以改完源码直接双击 `SideStack.exe` 即可，不必手动编译。
（把 `src\` 删掉程序照样运行，只是失去这个能力。）

## 仓库结构

```
├─ SideStack.exe          已编译的主程序（单文件，无外部依赖）
├─ app.ico / app.manifest 图标与清单（DPI 感知、无需管理员）
├─ build.cmd / build.ps1  编译脚本
├─ build-msi.cmd          打 MSI 的入口
├─ src\                   全部源码（22 个 .cs）
│   ├─ App.cs             入口、心跳、显隐状态机、菜单与配置落地
│   ├─ DockWindow.cs      面板窗口：图标槽位、滑动动画、悬停、拖动换位
│   ├─ LensGlass.cs       「光感」液态玻璃的 CPU 实现（SDF 折射 + 色散 + 内阴影）
│   ├─ Models.cs / ConfigStore.cs   数据模型与 config.txt 读写
│   ├─ SettingsWindow.cs / Theme.cs / UiRefraction.cs   设置界面与视觉样式
│   ├─ ScreenUtils.cs / WindowCapture.cs / NativeMethods.cs   抓屏与 Win32 封装
│   ├─ IconLoader.cs / ShellUtils.cs / TrayIcon.cs   图标提取、资源管理器操作、托盘
│   └─ AutoRebuild.cs     源码改动时自动重编译
├─ installer\             WiX 源文件、许可协议、打包脚本与说明（见 installer\README.md）
├─ tools\make_icon.py     生成 app.ico
├─ dist\                  安装包输出目录
└─ 使用说明.md             完整文档：设计要点、踩坑记录、性能实测数据
```

## 调试开关（环境变量）

| 变量 | 作用 |
|---|---|
| `SIDESTACK_ANIM_TRACE=1` | 每 ~40ms 输出面板位移动画追踪 |
| `SIDESTACK_LENS_PERF=1` | 输出《光感》的帧率与抓屏/渲染/上传分段耗时 |
| `SIDESTACK_LENS_DUMP=<目录>` | 每 20 帧导出玻璃帧 PNG（调参用） |
| `SIDESTACK_NOFX=1` | 关掉投影等效果（排查渲染问题） |
| `SIDESTACK_NOFADE=1` | 关闭窗口级淡入淡出 |
| `SIDESTACK_CAPTURABLE=1` | 允许侧边栏出现在截图/录屏里（《光感》背景会自反馈） |
| `SIDESTACK_SHOW_SETTINGS=1` | 启动后自动打开设置窗口 |

日志与配置位于 `%APPDATA%\SideStack\`（`sidestack.log`、`config.txt`，历史配置备份在 `backups\`）。

## 许可

本项目以 **MIT 许可证**开源，全文见 [LICENSE](LICENSE)：你可以自由使用、修改、再分发（包括商用），
只需保留版权与许可声明。

安装向导里展示的那份中文说明（[installer\license-zh.txt](installer/license-zh.txt)）是**安装与卸载告知**
（装到哪、写哪些注册表项、怎么彻底清除），不是许可证正文 —— 它与 MSI 一起打包，改动后需重新打包方生效。
