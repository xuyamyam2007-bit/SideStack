using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SideStack
{
    public static class ScreenUtils
    {
        private static readonly string[] ExcludeClasses = new string[]
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "SysListView32", "TrayNotifyWnd", "Button", "tooltips_class32",
            "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
            "MultitaskingViewFrame", "ForegroundStaging"
        };

        public static bool TryGetCursorPx(out POINT p)
        {
            return NativeMethods.GetCursorPos(out p);
        }

        private static RECT cachedMonitor;
        private static RECT cachedWork;
        private static bool monitorCached;

        private static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;

        public static void InvalidateMonitorCache()
        {
            monitorCached = false;
        }

        private static void EnsureMonitorCached()
        {
            if (monitorCached) { return; }
            POINT origin = new POINT();
            origin.X = 0;
            origin.Y = 0;
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            IntPtr mon = NativeMethods.MonitorFromPoint(origin, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (mon != IntPtr.Zero && NativeMethods.GetMonitorInfo(mon, ref mi))
            {
                cachedMonitor = mi.rcMonitor;
                cachedWork = mi.rcWork;
            }
            else
            {
                cachedMonitor.Left = 0;
                cachedMonitor.Top = 0;
                cachedMonitor.Right = NativeMethods.GetSystemMetrics(0);
                cachedMonitor.Bottom = NativeMethods.GetSystemMetrics(1);
                cachedWork = cachedMonitor;
                if (!NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref cachedWork, 0))
                {
                    cachedWork = cachedMonitor;
                }
            }
            monitorCached = true;
        }

        public static RECT GetPrimaryMonitorPx()
        {
            EnsureMonitorCached();
            return cachedMonitor;
        }

        public static RECT GetPrimaryWorkPx()
        {
            EnsureMonitorCached();
            return cachedWork;
        }

        /// <summary>桌面占用状态：被大窗口盖住、或前台是全屏应用（含独占全屏游戏）。</summary>
        public struct DesktopState
        {
            /// <summary>面板必须彻底消失（桌面被大窗口/全屏应用占据）。</summary>
            public bool Covered;
            /// <summary>前台是全屏应用：窗口盖住整块显示器，或系统报告有全屏/独占全屏应用。</summary>
            public bool Fullscreen;
            /// <summary>判定依据，写日志用。</summary>
            public string Reason;
        }

        private static string QunsText(int qs)
        {
            switch (qs)
            {
                case NativeMethods.QUNS_NOT_PRESENT: return "锁屏/屏保";
                case NativeMethods.QUNS_BUSY: return "全屏应用在前台";
                case NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN: return "独占全屏 D3D 应用";
                case NativeMethods.QUNS_PRESENTATION_MODE: return "演示模式";
                default: return "状态 " + qs;
            }
        }

        /// <summary>
        /// 一次查询同时给出「要不要藏」和「是不是全屏应用」。
        ///
        /// 判定顺序：
        /// ① 系统通知状态（SHQueryUserNotificationState）——独占全屏游戏只有这个信号可靠；
        /// ② 前台窗口（先换成它的顶层根窗口，有些程序的"前台窗口"其实是根窗口的子窗口）
        ///    是否盖住它自己所在的整块显示器 —— 无边框窗口化全屏游戏走这一条；
        /// ③ 前台窗口是否盖住工作区（大窗口，保持原来的行为）；
        /// ④ 有多个显示器时，除了"它自己那块"，再和主显示器比一次（面板只在主显示器上）。
        ///
        /// 之前只拿"前台窗口矩形"和"主显示器 / 主工作区"比，且只在 250ms 采样一次，
        /// 全屏游戏上很容易漏判；漏判时收起态的面板仍留在屏幕边缘（见 DockWindow.ApplyPosition），
        /// 就会在全屏游戏上露出一条边、并让独占全屏游戏反复被这个置顶窗口打断。
        /// </summary>
        public static DesktopState QueryDesktop()
        {
            DesktopState st = new DesktopState();
            st.Reason = "";

            // ① 系统级信号
            try
            {
                int qs;
                if (NativeMethods.SHQueryUserNotificationState(out qs) == 0 &&
                    (qs == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN ||
                     qs == NativeMethods.QUNS_BUSY ||
                     qs == NativeMethods.QUNS_PRESENTATION_MODE))
                {
                    st.Covered = true;
                    st.Fullscreen = true;
                    st.Reason = "系统通知状态=" + QunsText(qs);
                    return st;
                }
            }
            catch (Exception ex) { Logger.Write("QueryDesktop.QUNS", ex); }

            // ②③④ 前台窗口
            IntPtr h = NativeMethods.GetForegroundWindow();
            if (h == IntPtr.Zero) { return st; }
            if (h == NativeMethods.GetDesktopWindow()) { return st; }
            if (h == NativeMethods.GetShellWindow()) { return st; }

            try
            {
                if (!NativeMethods.IsWindowVisible(h)) { return st; }
                if (NativeMethods.IsIconic(h)) { return st; }

                uint pid;
                NativeMethods.GetWindowThreadProcessId(h, out pid);
                if (pid == OwnPid) { return st; }

                string cls = ClassNameOf(h);
                if (IsExcludedClass(cls)) { return st; }

                // 前台窗口可能是根窗口的子窗口（启动器 / 覆盖层常见），换成顶层窗口再比矩形
                IntPtr root = NativeMethods.GetAncestor(h, NativeMethods.GA_ROOT);
                if (root != IntPtr.Zero && root != h)
                {
                    if (!NativeMethods.IsWindowVisible(root) || NativeMethods.IsIconic(root)) { return st; }
                    h = root;
                    cls = ClassNameOf(h);
                    if (IsExcludedClass(cls)) { return st; }
                }

                int cloaked;
                if (NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_CLOAKED, out cloaked, 4) == 0 &&
                    cloaked != 0)
                {
                    return st;  // DWM 隐藏的"幽灵窗口"（UWP 遗留），不算遮挡
                }

                RECT wr;
                if (!NativeMethods.GetWindowRect(h, out wr)) { return st; }
                if (wr.Width <= 0 || wr.Height <= 0) { return st; }

                string who = cls + " " + wr.Width + "×" + wr.Height;

                // 它自己所在的那块显示器
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                IntPtr mon = NativeMethods.MonitorFromWindow(h, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (mon != IntPtr.Zero && NativeMethods.GetMonitorInfo(mon, ref mi))
                {
                    if (CoversRect(wr, mi.rcMonitor, ToleranceOf(mi.rcMonitor)))
                    {
                        st.Covered = true; st.Fullscreen = true;
                        st.Reason = "前台窗口盖住整块显示器（" + who + "）";
                        return st;
                    }
                }

                // 主显示器 / 主工作区（面板所在的那块）
                RECT pmon = GetPrimaryMonitorPx();
                if (CoversRect(wr, pmon, ToleranceOf(pmon)))
                {
                    st.Covered = true; st.Fullscreen = true;
                    st.Reason = "前台窗口盖住主显示器（" + who + "）";
                    return st;
                }

                if (mon != IntPtr.Zero && CoversRect(wr, mi.rcWork, 2))
                {
                    st.Covered = true;
                    st.Reason = "前台窗口盖住工作区（" + who + "）";
                    return st;
                }

                RECT pwork = GetPrimaryWorkPx();
                if (CoversRect(wr, pwork, 2))
                {
                    st.Covered = true;
                    st.Reason = "前台窗口盖住主工作区（" + who + "）";
                    return st;
                }
            }
            catch (Exception ex)
            {
                Logger.Write("QueryDesktop", ex);
            }
            return st;
        }

        private static string ClassNameOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            NativeMethods.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static bool IsExcludedClass(string cls)
        {
            for (int i = 0; i < ExcludeClasses.Length; i++)
            {
                if (string.Equals(cls, ExcludeClasses[i], StringComparison.Ordinal)) { return true; }
            }
            return false;
        }

        /// <summary>宽高各留一点余量：无边框窗口化全屏的游戏有时比显示器小 1~2 像素。</summary>
        private static int ToleranceOf(RECT r)
        {
            int t = (int)Math.Round(Math.Max(r.Width, r.Height) * 0.002);
            return t < 2 ? 2 : (t > 8 ? 8 : t);
        }

        private static bool CoversRect(RECT wr, RECT target, int tol)
        {
            if (target.Width <= 0 || target.Height <= 0) { return false; }
            return wr.Left <= target.Left + tol && wr.Top <= target.Top + tol &&
                   wr.Right >= target.Right - tol && wr.Bottom >= target.Bottom - tol;
        }

        public static byte[] CaptureBgraClamped(int x, int y, int w, int h)
        {
            return SharedCapturer.Capture(x, y, w, h);
        }

        private static readonly ScreenBgraCapturer SharedCapturer = new ScreenBgraCapturer();

        public static bool IsCursorInTriggerBand(bool isLeft, double triggerDip, double dpiScale, out POINT cursorPx)
        {
            cursorPx = new POINT();
            if (!NativeMethods.GetCursorPos(out cursorPx)) { return false; }
            RECT mon = GetPrimaryMonitorPx();
            double band = triggerDip * dpiScale;
            if (band < 1.0) { band = 1.0; }
            if (isLeft)
            {
                return cursorPx.X <= mon.Left + band;
            }
            return cursorPx.X >= mon.Right - band;
        }

        public static byte[] CaptureBgra(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) { return null; }
            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(
                           w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h),
                                         System.Drawing.CopyPixelOperation.SourceCopy);
                    }

                    byte[] px = new byte[w * h * 4];
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(
                        new System.Drawing.Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int row = 0; row < h; row++)
                        {
                            Marshal.Copy(IntPtr.Add(bd.Scan0, row * bd.Stride), px, row * w * 4, w * 4);
                        }
                    }
                    finally { bmp.UnlockBits(bd); }
                    return px;
                }
            }
            catch (Exception ex)
            {
                Logger.Write("CaptureBgra", ex);
                return null;
            }
        }
    }

    public sealed class ScreenBgraCapturer
    {
        private int w, h;
        private IntPtr memDc = IntPtr.Zero;
        private IntPtr dib = IntPtr.Zero;
        private IntPtr oldBmp = IntPtr.Zero;
        private IntPtr bits = IntPtr.Zero;
        private byte[] buf;
        private readonly object gate = new object();

        public byte[] Capture(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) { return null; }
            try
            {
                RECT mon = ScreenUtils.GetPrimaryMonitorPx();
                int cx0 = Math.Max(x, mon.Left), cy0 = Math.Max(y, mon.Top);
                int cx1 = Math.Min(x + w, mon.Right), cy1 = Math.Min(y + h, mon.Bottom);
                if (cx1 <= cx0 || cy1 <= cy0) { return null; }

                IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
                try
                {
                    lock (gate)
                    {
                        if (!Ensure(w, h)) { return null; }
                        NativeMethods.BitBlt(memDc, cx0 - x, cy0 - y, cx1 - cx0, cy1 - cy0,
                                             screen, cx0, cy0, NativeMethods.SRCCOPY);
                        Marshal.Copy(bits, buf, 0, buf.Length);

                        int subW = cx1 - cx0, subH = cy1 - cy0;
                        int offX = cx0 - x, offY = cy0 - y;
                        if (offX > 0 || offY > 0 || offX + subW < w || offY + subH < h)
                        {
                            FillEdges(buf, w, h, offX, offY, subW, subH);
                        }
                        return buf;
                    }
                }
                finally { NativeMethods.ReleaseDC(IntPtr.Zero, screen); }
            }
            catch (Exception ex)
            {
                Logger.Write("ScreenBgraCapturer.Capture", ex);
                return null;
            }
        }

        private static void FillEdges(byte[] px, int w, int h, int offX, int offY, int subW, int subH)
        {
            int firstCol = offX, lastCol = offX + subW - 1;
            for (int r = offY; r < offY + subH; r++)
            {
                int row = r * w * 4;
                for (int c = 0; c < firstCol; c++) { Buffer.BlockCopy(px, row + firstCol * 4, px, row + c * 4, 4); }
                for (int c = lastCol + 1; c < w; c++) { Buffer.BlockCopy(px, row + lastCol * 4, px, row + c * 4, 4); }
            }
            for (int r = 0; r < offY; r++) { Buffer.BlockCopy(px, offY * w * 4, px, r * w * 4, w * 4); }
            for (int r = offY + subH; r < h; r++) { Buffer.BlockCopy(px, (offY + subH - 1) * w * 4, px, r * w * 4, w * 4); }
        }

        private bool Ensure(int width, int height)
        {
            if (memDc != IntPtr.Zero && w == width && h == height) { return true; }
            Release();

            memDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            if (memDc == IntPtr.Zero) { return false; }

            NativeMethods.BITMAPINFO bmi = new NativeMethods.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            dib = NativeMethods.CreateDIBSection(memDc, ref bmi, NativeMethods.DIB_RGB_COLORS,
                                                 out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) { Release(); return false; }
            oldBmp = NativeMethods.SelectObject(memDc, dib);

            w = width; h = height;
            buf = new byte[w * h * 4];
            return true;
        }

        private void Release()
        {
            try
            {
                if (memDc != IntPtr.Zero && oldBmp != IntPtr.Zero) { NativeMethods.SelectObject(memDc, oldBmp); }
                if (dib != IntPtr.Zero) { NativeMethods.DeleteObject(dib); }
                if (memDc != IntPtr.Zero) { NativeMethods.DeleteDC(memDc); }
            }
            catch { }
            memDc = IntPtr.Zero; dib = IntPtr.Zero; oldBmp = IntPtr.Zero; bits = IntPtr.Zero;
        }
    }
}
