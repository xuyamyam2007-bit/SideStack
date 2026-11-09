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

        /// <summary>
        /// 桌面占用状态。两个标志含义完全不同，处理方式也不同：
        ///
        ///   Covered    —— 前台有个窗口把「面板所在的那块屏」的工作区盖住了（典型：任何最大化窗口）。
        ///                 它只用来决定「收起态要不要在屏幕边缘露出那条细条」，以及自动收起面板。
        ///                 **绝不能用它隐藏面板窗口** —— 那会让侧边栏在最大化窗口出现时整体消失，
        ///                 而用户往往正是想在最大化窗口上唤出侧边栏。
        ///
        ///   Fullscreen —— 前台是真·全屏应用（独占全屏 / 无边框窗口化全屏 / 演示模式）。
        ///                 这时面板必须整体隐身（窗口 Hide），否则会盖在游戏上、
        ///                 还会让独占全屏游戏反复被这个置顶分层窗口打断。是否挂起只看这一个标志。
        /// </summary>
        public struct DesktopState
        {
            /// <summary>面板所在那块屏被大窗口占据（含最大化窗口）。只影响"细条"与自动收起。</summary>
            public bool Covered;
            /// <summary>前台是真·全屏应用。面板必须整体隐身。</summary>
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
        /// 「独占全屏」信号连续多少次拿到"不吻合"的几何证据就认为它滞留了。
        /// 游戏切换分辨率时显示器矩形与窗口矩形会短暂不一致（缓存也可能慢一拍），
        /// 所以阈值给得宽松：心跳在挂起态 200ms 一次，50 次 ≈ 10 秒。
        /// 这段窗口是"宁可多等一会儿也不能在游戏里弹出面板"的取舍；
        /// 若确实是误判，托盘菜单「重新检测并唤出面板」可以立刻恢复。
        /// </summary>
        private const int D3dStaleLimit = 50;

        private static int d3dNoGeometryStreak;
        private static bool d3dStaleReported;
        private static bool qunsShellLogged;

        /// <summary>
        /// 一次查询同时给出「要不要收细条」和「是不是真全屏」。
        ///
        /// 关键原则：**只有真·全屏才允许把面板整体隐藏**；普通大窗口/最大化窗口只算"占据"。
        /// 旧实现把这两件事混成一个 Covered 标志，于是任何最大化窗口（浏览器 / 资源管理器 / 编辑器）
        /// 都会让面板 Hide()，而全屏游戏退出后系统往往把焦点还给"上一个最大化窗口" ——
        /// 面板因此持续挂起、鼠标贴边毫无反应，表现就是「全屏游戏退出后唤不出侧边栏」。
        ///
        /// 判定顺序：
        /// ① 先取前台窗口并做有效性筛查（可见、非最小化、非 DWM 隐藏、非本进程、非桌面/任务栏类）。
        ///    这一步决定了"前台到底有没有一个应用窗口" —— 也是系统状态滞留时的兜底。
        /// ② QUNS = 独占全屏 D3D：最强信号，直接判全屏，但要求连续 D3dStaleLimit 次都拿不到
        ///    几何支持才降级（防滞留）。
        /// ③ 可见外框盖住面板所在显示器、且窗口**不是最大化** → 无边框窗口化全屏。
        /// ④ 可见外框盖住工作区 → 只判"占据"（最大化窗口走这一条）。
        ///
        /// 另外所有比较都只针对**主显示器**（面板只存在于这块屏）：副屏上的全屏游戏不该让主屏的侧边栏消失。
        /// </summary>
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
                            // 前台窗口可能是根窗口的子窗口（启动器 / 覆盖层常见），换成顶层窗口再判断
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

            // ② QUNS：它只描述"前台是不是全屏应用"，必须和"前台确实有一个应用窗口"交叉校验。
            //    游戏退出/崩溃后 Shell 状态可能滞留，此时前台已经是桌面或别的程序，
            //    若继续相信 QUNS，侧边栏就永远唤不出来。
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
                        // 独占全屏：切分辨率瞬间窗口矩形会短暂不可信，所以"拿不到几何"本身不算反证；
                        // 只有**确实拿到了矩形、而它不盖住显示器**才累计滞留次数。
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

            // 面板只存在于主显示器：窗口和主屏没有交集就与本程序无关（副屏全屏游戏不该影响主屏侧边栏）
            if (!Intersects(frame, pmon))
            {
                return st;
            }

            // ③ 盖住整块主显示器、且"形状像全屏" → 无边框窗口化全屏
            if (FullscreenShaped(frame, pmon, zoomed, normalFrame))
            {
                st.Covered = true;
                st.Fullscreen = true;
                st.Reason = "前台窗口盖住整块显示器（" + who + "）";
                return st;
            }

            // ④ 盖住工作区 → 只算占据（最大化窗口 / 铺满的普通窗口），面板保留、只收细条
            if (CoversRect(frame, pwork, 3))
            {
                st.Covered = true;
                st.Reason = "前台窗口盖住工作区（" + who + (zoomed ? "，已最大化" : "") + "）";
                return st;
            }

            return st;
        }

        /// <summary>两个矩形是否有交集（零面积不算）。</summary>
        private static bool Intersects(RECT a, RECT b)
        {
            return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
        }

        /// <summary>
        /// 窗口是否带"标准外框"（标题栏 + 可调整边框）。
        /// 最大化普通窗口 = true；无边框全屏游戏（WS_POPUP、去掉标题栏）= false。
        /// 这是「盖住整块显示器的窗口到底是全屏游戏还是最大化窗口」的最后一道判别依据 ——
        /// 光看矩形分不出来（无边框全屏游戏有的也会带 WS_MAXIMIZE），只能看外框样式。
        /// </summary>
        private static bool HasNormalFrame(IntPtr h)
        {
            try
            {
                long style = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_STYLE).ToInt64();
                if (style == 0) { return true; }   // 取不到样式时按"普通窗口"处理，宁可让面板可用
                bool caption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
                bool thick = (style & NativeMethods.WS_THICKFRAME) != 0;
                return caption && thick;
            }
            catch { return true; }
        }

        /// <summary>
        /// 「形状像全屏」：可见外框盖住整块显示器，并且不是"带标准外框的最大化窗口"。
        /// </summary>
        private static bool FullscreenShaped(RECT frame, RECT monitor, bool zoomed, bool normalFrame)
        {
            if (!CoversRect(frame, monitor, ToleranceOf(monitor))) { return false; }
            return !zoomed || !normalFrame;
        }

        /// <summary>
        /// 取窗口"看得见的外框"。优先 DWM 扩展帧边界（最大化窗口不含那条 8px 隐形边框），
        /// 失败时退回 GetWindowRect；分屏/全屏切换的瞬间可能拿到空矩形，此时返回 false。
        /// </summary>
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
