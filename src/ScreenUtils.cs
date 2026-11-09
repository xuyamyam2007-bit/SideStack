using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SideStack
{
    // 桌面遮挡与全屏判定、显示器矩形缓存、屏幕抓取。
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

        // Covered 只影响收起细条与自动收起；只有 Fullscreen 才允许隐藏面板窗口。
        public struct DesktopState
        {
            public bool Covered;
            public bool Fullscreen;
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

        private const int D3dStaleLimit = 50;

        private static int d3dNoGeometryStreak;
        private static bool d3dStaleReported;
        private static bool qunsShellLogged;

        // 判定顺序：前台有效性筛查 → QUNS（独占全屏直判）→ 几何 → 工作区，不要调换。
        public static DesktopState QueryDesktop()
        {
            DesktopState st = new DesktopState();
            st.Reason = "";

            IntPtr h = NativeMethods.GetForegroundWindow();
            string cls = "";
            RECT frame = new RECT();
            bool haveFrame = false;
            bool zoomed = false;
            bool normalFrame = false;
            bool foregroundIsApp = false;

            try
            {
                if (h != IntPtr.Zero && (h == NativeMethods.GetDesktopWindow() || h == NativeMethods.GetShellWindow()))
                {
                    h = IntPtr.Zero;
                }
            }
            catch { h = IntPtr.Zero; }

            if (h != IntPtr.Zero)
            {
                try
                {
                    if (NativeMethods.IsWindowVisible(h) && !NativeMethods.IsIconic(h))
                    {
                        uint pid;
                        NativeMethods.GetWindowThreadProcessId(h, out pid);
                        if (pid != OwnPid)
                        {
                            IntPtr root = NativeMethods.GetAncestor(h, NativeMethods.GA_ROOT);
                            if (root != IntPtr.Zero) { h = root; }

                            cls = ClassNameOf(h);
                            int cloaked = 0;
                            bool isCloaked =
                                NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_CLOAKED, out cloaked, 4) == 0 &&
                                cloaked != 0;

                            bool okay = !IsExcludedClass(cls) && !isCloaked
                                        && NativeMethods.IsWindowVisible(h)
                                        && !NativeMethods.IsIconic(h);
                            if (okay)
                            {
                                foregroundIsApp = true;
                                zoomed = NativeMethods.IsZoomed(h);
                                normalFrame = HasNormalFrame(h);
                                haveFrame = TryGetVisibleFrame(h, out frame);
                            }
                        }
                    }
                }
                catch (Exception ex) { Logger.Write("QueryDesktop.foreground", ex); }
            }

            RECT pmon = GetPrimaryMonitorPx();
            RECT pwork = GetPrimaryWorkPx();
            if (pwork.Width <= 100 || pwork.Height <= 100) { pwork = pmon; }

            try
            {
                int qs;
                if (NativeMethods.SHQueryUserNotificationState(out qs) == 0)
                {
                    bool qunsFull = qs == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN ||
                                    qs == NativeMethods.QUNS_BUSY ||
                                    qs == NativeMethods.QUNS_PRESENTATION_MODE;

                    if (qunsFull && !foregroundIsApp)
                    {
                        d3dNoGeometryStreak = 0;
                        if (!qunsShellLogged)
                        {
                            qunsShellLogged = true;
                            Logger.Write("全屏判定：系统通知状态=" + QunsText(qs) +
                                         "，但前台没有有效应用窗口（桌面/任务栏/最小化/已隐藏），"
                                         + "按状态滞留处理，不挂起面板");
                        }
                    }
                    else if (qs == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN)
                    {
                        qunsShellLogged = false;

                        if (!haveFrame) { d3dNoGeometryStreak = 0; d3dStaleReported = false; }
                        else if (CoversRect(frame, pmon, ToleranceOf(pmon))) { d3dNoGeometryStreak = 0; d3dStaleReported = false; }
                        else { d3dNoGeometryStreak++; }

                        if (d3dNoGeometryStreak <= D3dStaleLimit)
                        {
                            st.Covered = true;
                            st.Fullscreen = true;
                            st.Reason = "系统通知状态=独占全屏 D3D 应用（" + cls + "）";
                            return st;
                        }

                        if (!d3dStaleReported)
                        {
                            d3dStaleReported = true;
                            Logger.Write("全屏判定：独占全屏信号已持续 " + d3dNoGeometryStreak +
                                         " 次与前台窗口实际位置不符（游戏可能已异常退出 / 驱动复位），"
                                         + "按状态滞留下调为普通占据，面板恢复待命");
                        }
                        st.Covered = true;
                        st.Reason = "独占全屏信号滞留（" + cls + "）";
                        return st;
                    }
                    else if (qunsFull && haveFrame && FullscreenShaped(frame, pmon, zoomed, normalFrame))
                    {
                        d3dNoGeometryStreak = 0;
                        d3dStaleReported = false;
                        qunsShellLogged = false;
                        st.Covered = true;
                        st.Fullscreen = true;
                        st.Reason = "系统通知状态=" + QunsText(qs) + "，前台窗口盖住整块显示器（" + cls + "）";
                        return st;
                    }
                    else if (!qunsFull)
                    {
                        d3dNoGeometryStreak = 0;
                        d3dStaleReported = false;
                        qunsShellLogged = false;
                    }
                }
            }
            catch (Exception ex) { Logger.Write("QueryDesktop.QUNS", ex); }

            if (!foregroundIsApp || !haveFrame) { return st; }

            string who = cls + " " + frame.Width + "×" + frame.Height;

            if (!Intersects(frame, pmon))
            {
                return st;
            }

            if (FullscreenShaped(frame, pmon, zoomed, normalFrame))
            {
                st.Covered = true;
                st.Fullscreen = true;
                st.Reason = "前台窗口盖住整块显示器（" + who + "）";
                return st;
            }

            if (CoversRect(frame, pwork, 3))
            {
                st.Covered = true;
                st.Reason = "前台窗口盖住工作区（" + who + (zoomed ? "，已最大化" : "") + "）";
                return st;
            }

            return st;
        }

        private static bool Intersects(RECT a, RECT b)
        {
            return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
        }

        private static bool HasNormalFrame(IntPtr h)
        {
            try
            {
                long style = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_STYLE).ToInt64();
                if (style == 0) { return true; }
                bool caption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
                bool thick = (style & NativeMethods.WS_THICKFRAME) != 0;
                return caption && thick;
            }
            catch { return true; }
        }

        private static bool FullscreenShaped(RECT frame, RECT monitor, bool zoomed, bool normalFrame)
        {
            if (!CoversRect(frame, monitor, ToleranceOf(monitor))) { return false; }
            return !zoomed || !normalFrame;
        }

        private static bool TryGetVisibleFrame(IntPtr h, out RECT r)
        {
            r = new RECT();
            try
            {
                RECT d;
                if (NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                        out d, Marshal.SizeOf(typeof(RECT))) == 0 &&
                    d.Width > 0 && d.Height > 0)
                {
                    r = d;
                    return true;
                }
            }
            catch { }

            RECT wr;
            if (NativeMethods.GetWindowRect(h, out wr) && wr.Width > 0 && wr.Height > 0)
            {
                r = wr;
                return true;
            }
            return false;
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

    // BitBlt 抓屏，复用 DIB 缓冲。
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
