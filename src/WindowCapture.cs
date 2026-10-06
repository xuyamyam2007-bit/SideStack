using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace SideStack
{
    internal static class WindowCapture
    {
        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public static bool CaptureAllowed;

        public static bool Hold;

        private static int holdCount;

        private static uint Pid
        {
            get { try { return (uint)System.Diagnostics.Process.GetCurrentProcess().Id; } catch { return 0; } }
        }

        public static int Apply(uint pid, bool excludeFromCapture)
        {
            if (pid == 0) { return 0; }
            int changed = 0;
            uint affinity = excludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
            try
            {
                EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    uint p;
                    GetWindowThreadProcessId(h, out p);
                    if (p != pid || !IsWindow(h)) { return true; }
                    try
                    {
                        if (SetWindowDisplayAffinity(h, affinity)) { changed++; }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return changed;
        }

        public static int Reapply()
        {
            return Apply(Pid, !CaptureAllowed);
        }

        public static void Guard(FrameworkElement popup)
        {
            if (popup == null) { return; }
            try
            {
                ContextMenu cm = popup as ContextMenu;
                if (cm != null)
                {
                    cm.Opened += delegate { EnterHold("菜单"); };
                    cm.Closed += delegate { ExitHold(); };
                    return;
                }
                ToolTip tt = popup as ToolTip;
                if (tt != null)
                {
                    tt.Opened += delegate { Reapply(); };
                    return;
                }
                Window w = popup as Window;
                if (w != null)
                {
                    w.Loaded += delegate { Reapply(); };
                }
            }
            catch { }
        }

        private static void EnterHold(string what)
        {
            try
            {
                holdCount++;
                Hold = holdCount > 0;
                int n = Reapply();
                Logger.Write("弹层即时排除: " + what + "（本进程窗口 " + n + " 个）暂停抓屏=" + Hold);
            }
            catch { }
        }

        private static void ExitHold()
        {
            try
            {
                holdCount--;
                if (holdCount < 0) { holdCount = 0; }
                Hold = holdCount > 0;
                Reapply();
            }
            catch { }
        }
    }
}
