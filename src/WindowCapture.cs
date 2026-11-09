using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace SideStack
{
    // 抓屏可见性策略：默认允许截图/录屏看到侧边栏。
    internal static class WindowCapture
    {
        public static bool CaptureAllowed = true;

        public static bool LensSelfExcludeDisabled
        {
            get { return Environment.GetEnvironmentVariable("SIDESTACK_CAPTURABLE") == "1"; }
        }

        public static int Reapply()
        {
            return 0;
        }

        public static void Guard(FrameworkElement popup)
        {
        }
    }
}
