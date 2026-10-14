using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace SideStack
{
    /// <summary>
    /// 抓屏可见性策略。
    ///
    /// 以前这里会给本进程**所有**窗口反复设置 WDA_EXCLUDEFROMCAPTURE（每 150ms 枚举一遍），
    /// 那带来两个问题：①「UU 远程」这类抓屏型远程工具看不到侧边栏；
    /// ② 频繁重设显示亲和度会影响窗口合成，曾出现整片白窗口且无法交互。
    ///
    /// 现在默认**一律允许被抓屏**：截图 / 录屏 / 远程控制都能正常看到侧边栏。
    /// 唯一的例外是《光感》风格：它的背景来自抓取屏幕，若抓到自己会自反馈，
    /// 因此只对**它自己那一个面板窗口**做一次性的排除（见 DockWindow.SetLensAffinity）。
    /// 设置环境变量 SIDESTACK_CAPTURABLE=1 可连这个例外也关掉。
    /// </summary>
    internal static class WindowCapture
    {
        public static bool CaptureAllowed = true;

        public static bool LensSelfExcludeDisabled
        {
            get { return Environment.GetEnvironmentVariable("SIDESTACK_CAPTURABLE") == "1"; }
        }

        /// <summary>永远允许被抓屏（保留此方法只为兼容旧调用点）</summary>
        public static int Reapply()
        {
            return 0;
        }

        public static void Guard(FrameworkElement popup)
        {
            // 不再需要对弹层做任何抓屏处理
        }
    }
}
