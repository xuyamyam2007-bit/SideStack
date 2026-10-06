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

        public static bool IsDesktopCovered()
        {
            IntPtr h = NativeMethods.GetForegroundWindow();
            if (h == IntPtr.Zero) { return false; }
            if (h == NativeMethods.GetDesktopWindow()) { return false; }
            if (h == NativeMethods.GetShellWindow()) { return false; }

            try
            {
                if (!NativeMethods.IsWindowVisible(h)) { return false; }
                if (NativeMethods.IsIconic(h)) { return false; }

                uint pid;
                NativeMethods.GetWindowThreadProcessId(h, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) { return false; }

                StringBuilder sb = new StringBuilder(256);
                NativeMethods.GetClassName(h, sb, sb.Capacity);
                string cls = sb.ToString();
                for (int i = 0; i < ExcludeClasses.Length; i++)
                {
                    if (string.Equals(cls, ExcludeClasses[i], StringComparison.Ordinal)) { return false; }
                }

                RECT wr;
                if (!NativeMethods.GetWindowRect(h, out wr)) { return false; }
                if (wr.Width <= 0 || wr.Height <= 0) { return false; }

                RECT mon = GetPrimaryMonitorPx();

                if (wr.Left <= mon.Left + 1 && wr.Top <= mon.Top + 1 &&
                    wr.Right >= mon.Right - 1 && wr.Bottom >= mon.Bottom - 1)
                {
                    return true;
                }

                RECT work = GetPrimaryWorkPx();
                if (wr.Left <= work.Left + 2 && wr.Top <= work.Top + 2 &&
                    wr.Right >= work.Right - 2 && wr.Bottom >= work.Bottom - 2)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Write("IsDesktopCovered", ex);
            }
            return false;
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
