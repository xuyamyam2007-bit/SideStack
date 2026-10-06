using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SideStack
{
    public sealed class UiRefraction
    {
        private sealed class Region
        {
            public Border Target;
            public double Radius;
            public Brush Original;
            public ImageBrush Brush;
            public WriteableBitmap Wb;
            public byte[] Dst;
            public double Tint;
            public bool IsShell;
        }

        private const double RenderScale = 0.65;

        private const int PeriodMs = 1200;

        private readonly Window win;
        private readonly List<Region> regions = new List<Region>();
        private readonly object gate = new object();
        private ScreenBgraCapturer capturer;
        private Thread worker;
        private AutoResetEvent wake;
        private volatile bool running;
        private volatile bool enabled;
        private volatile bool affinitySet;
        private readonly string dumpDir;
        private bool dumped;

        public UiRefraction(Window win)
        {
            this.win = win;
            dumpDir = Environment.GetEnvironmentVariable("SIDESTACK_UIREFRACT_DUMP");
        }

        public void AddRegion(Border target, double radius, double tint, bool isShell)
        {
            if (target == null) { return; }
            Region r = new Region();
            r.Target = target;
            r.Radius = radius;
            r.IsShell = isShell;
            r.Tint = isShell ? tint : Math.Min(tint, 0.62);
            r.Original = target.Background;
            r.Brush = new ImageBrush();
            r.Brush.Stretch = Stretch.Fill;
            regions.Add(r);
        }

        public void SetOpacity(double opacity)
        {
            lock (gate)
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    regions[i].Tint = regions[i].IsShell ? opacity : Math.Min(opacity, 0.62);
                }
            }
            Request();
        }

        public void SetEnabled(bool on)
        {
            enabled = on;
            if (on)
            {
                if (worker == null)
                {
                    wake = new AutoResetEvent(false);
                    running = true;
                    worker = new Thread(WorkerLoop);
                    worker.IsBackground = true;
                    worker.Name = "UiRefraction";
                    worker.Start();
                }
                win.LocationChanged += OnWindowChanged;
                win.SizeChanged += OnWindowChanged;
                Request();
            }
            else
            {
                win.LocationChanged -= OnWindowChanged;
                win.SizeChanged -= OnWindowChanged;
                Restore();
            }
        }

        private void OnWindowChanged(object sender, EventArgs e) { Request(); }

        public void Request()
        {
            try { if (wake != null && enabled) { wake.Set(); } }
            catch { }
        }

        private void Restore()
        {
            for (int i = 0; i < regions.Count; i++)
            {
                try { if (regions[i].Target != null) { regions[i].Target.Background = regions[i].Original; } }
                catch { }
            }
        }

        private void WorkerLoop()
        {
            while (running)
            {
                try { wake.WaitOne(PeriodMs); }
                catch { }
                if (!running) { break; }
                if (!enabled) { continue; }
                try { CaptureAndRender(); }
                catch (Exception ex) { Logger.Write("UiRefraction", ex); }
            }
            running = false;
        }

        private void CaptureAndRender()
        {
            int wx = 0, wy = 0, ww = 0, wh = 0;
            double scale = 1.0;
            List<double[]> boxes = new List<double[]>();
            List<double> tints = new List<double>();
            List<double> radii = new List<double>();
            bool ok = (bool)win.Dispatcher.Invoke(new Func<bool>(delegate
            {
                if (!enabled || !win.IsVisible || win.ActualWidth < 8 || win.ActualHeight < 8) { return false; }
                Point origin = win.PointToScreen(new Point(0, 0));
                scale = DpiScale();
                wx = (int)Math.Round(origin.X);
                wy = (int)Math.Round(origin.Y);
                ww = (int)Math.Round(win.ActualWidth * scale);
                wh = (int)Math.Round(win.ActualHeight * scale);
                boxes.Clear(); tints.Clear(); radii.Clear();
                for (int i = 0; i < regions.Count; i++)
                {
                    Region r = regions[i];
                    if (r.Target == null || !r.Target.IsVisible || r.Target.ActualWidth < 8 || r.Target.ActualHeight < 8)
                    {
                        boxes.Add(null); tints.Add(0); radii.Add(0); continue;
                    }
                    Point p = r.Target.TranslatePoint(new Point(0, 0), win);
                    boxes.Add(new double[] { p.X * scale, p.Y * scale,
                                             r.Target.ActualWidth * scale, r.Target.ActualHeight * scale });
                    tints.Add(r.Tint);
                    radii.Add(r.Radius * scale);
                }
                return ww > 0 && wh > 0;
            }));
            if (!ok) { return; }

            if (capturer == null) { capturer = new ScreenBgraCapturer(); }

            SetAffinity(true);
            byte[] src;
            try { src = capturer.Capture(wx, wy, ww, wh); }
            finally { SetAffinity(false); }
            if (src == null) { return; }

            int dop = Math.Max(2, Environment.ProcessorCount / 4);
            List<byte[]> results = new List<byte[]>();
            List<int[]> sizes = new List<int[]>();
            for (int i = 0; i < regions.Count; i++)
            {
                double[] bx = boxes[i];
                if (bx == null) { results.Add(null); sizes.Add(null); continue; }
                int rw = (int)bx[2], rh = (int)bx[3];
                int ow = Math.Max(8, (int)Math.Round(rw * RenderScale));
                int oh = Math.Max(8, (int)Math.Round(rh * RenderScale));
                LensGlassOptions o = CardPreset(ow, oh, radii[i] * RenderScale, tints[i]);
                o.SrcOffsetX = (int)Math.Round(bx[0]);
                o.SrcOffsetY = (int)Math.Round(bx[1]);
                o.SrcPanelW = rw;
                o.SrcPanelH = rh;
                int need = ow * oh * 4;
                if (regions[i].Dst == null || regions[i].Dst.Length != need) { regions[i].Dst = new byte[need]; }
                byte[] glass = LensGlassRenderer.Render(src, ww, wh, o, dop, regions[i].Dst);
                results.Add(glass);
                sizes.Add(new int[] { ow, oh });
            }
            DumpOnce(src, ww, wh, results, sizes);
            PushToUi(results, sizes);
        }

        private void PushToUi(List<byte[]> results, List<int[]> sizes)
        {
            try
            {
                win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!enabled) { return; }
                    for (int i = 0; i < regions.Count; i++)
                    {
                        Region r = regions[i];
                        if (results[i] == null || sizes[i] == null || r.Target == null) { continue; }
                        int ow = sizes[i][0], oh = sizes[i][1];
                        if (r.Wb == null || r.Wb.PixelWidth != ow || r.Wb.PixelHeight != oh)
                        {
                            r.Wb = new WriteableBitmap(ow, oh, 96, 96, PixelFormats.Pbgra32, null);
                            r.Brush.ImageSource = r.Wb;
                        }
                        r.Wb.WritePixels(new Int32Rect(0, 0, ow, oh), results[i], ow * 4, 0);
                        if (r.Target.Background != r.Brush) { r.Target.Background = r.Brush; }
                    }
                }), DispatcherPriority.Background);
            }
            catch { }
        }

        private static LensGlassOptions CardPreset(int w, int h, double radius, double tint)
        {
            double shortSide = Math.Min(w, h);
            LensGlassOptions o = new LensGlassOptions();
            o.Width = w;
            o.Height = h;
            o.CornerRadius = Math.Max(3.0, radius);
            o.Magnification = 1.0;
            o.RefractionHeight = Math.Max(5.0, shortSide / 12.0);
            o.RefractionAmount = Math.Max(5.0, shortSide / 4.0);
            o.DepthEffect = true;
            o.ChromaticAberration = true;
            o.InnerShadowRadius = 0.0;
            o.InnerShadowAlpha = 0.0;
            o.HighlightWidth = 0.7;
            o.HighlightAlpha = 0.16;
            o.TintAlpha = Math.Max(0.0, Math.Min(0.95, tint));
            return o;
        }

        private void SetAffinity(bool on)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd == IntPtr.Zero) { return; }
                if (on) { NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE); affinitySet = true; }
                else if (affinitySet) { NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_NONE); affinitySet = false; }
            }
            catch { }
        }

        private double DpiScale()
        {
            try
            {
                HwndSource src = PresentationSource.FromVisual(win) as HwndSource;
                if (src != null && src.CompositionTarget != null) { return src.CompositionTarget.TransformToDevice.M11; }
            }
            catch { }
            return 1.0;
        }

        private void DumpOnce(byte[] src, int srcW, int srcH, List<byte[]> results, List<int[]> sizes)
        {
            if (dumped || string.IsNullOrEmpty(dumpDir)) { return; }
            dumped = true;
            try
            {
                if (!System.IO.Directory.Exists(dumpDir)) { System.IO.Directory.CreateDirectory(dumpDir); }
                SaveBgra(src, srcW, srcH, System.IO.Path.Combine(dumpDir, "ui-source.png"));
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i] != null && sizes[i] != null)
                    {
                        SaveBgra(results[i], sizes[i][0], sizes[i][1],
                                 System.IO.Path.Combine(dumpDir, "ui-region" + i + ".png"));
                    }
                }
                Logger.Write("UiRefraction 已导出诊断图到 " + dumpDir);
            }
            catch (Exception ex) { Logger.Write("UiRefraction.Dump", ex); }
        }

        private static void SaveBgra(byte[] bgra, int w, int h, string path)
        {
            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(bgra, y * w * 4,
                        IntPtr.Add(bd.Scan0, y * bd.Stride), w * 4);
                }
            }
            finally { bmp.UnlockBits(bd); }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
        }
    }
}
