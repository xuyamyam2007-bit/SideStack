using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SideStack
{
    public class DockWindow : Window
    {
        public bool IsLeft;

        public event Action<DockWindow, DockItem> ItemActivated;
        public event Action<DockWindow, DockItem> RevealRequested;
        public event Action<DockWindow, DockItem> TransferRequested;
        public event Action<DockWindow, DockItem, bool> NoRoundToggled;
        public event Action<DockWindow, DockItem> RemoveRequested;
        public event Action<DockWindow> SettingsRequested;
        public event Action<DockWindow> ReloadRequested;
        public event Action<DockWindow> OpenConfigFolderRequested;
        public event Action<DockWindow> ExitRequested;
        public event Action<DockWindow> LayoutChanged;

        public event Action<DockWindow, System.Collections.Generic.List<string>, int> FilesDropped;

        public event Action<DockWindow, DockItem> CustomIconRequested;

        public event Action<DockWindow, DockItem> CustomIconCleared;

        public event Action<DockWindow, DockItem, double> IconScaleRequested;

        private DockConfig cfg;
        private Border panel;
        private Border fringe;
        private Grid grid;

        private const double EdgeMargin = 8;
        private const double NameH = 18;
        private const double NameFont = 10.5;

        private double dpiScale = 1.0;
        private double dockWidth = 72;
        private double dockHeight = 320;

        private bool wantShow;
        private int lastInsideMs;
        private double animT;
        private int lastTickMs;
        private bool forcedHidden;

        private const double ShadowPad = 16;

        private const double EdgePadScale = 0.45;

        private const double EdgePadMin = 1.0;

        private const double PeekW = 4;
        private bool coveredHide;

        private IntPtr hwnd = IntPtr.Zero;

        private int lastPlacedWinX = int.MinValue;
        private int lastPlacedWinY = int.MinValue;
        private int stableFrames;
        private bool offsetMeasured;

        private int shadowPadPx = 16;

        private ContextMenu panelMenu;

        private Border rim;

        private ImageBrush lensBrush;
        private string lensKey = "";
        private bool lensBusy;
        private bool lensPending;

        private WriteableBitmap lensWb;
        private System.Threading.Thread lensThread;
        private volatile bool lensThreadRun;
        private ulong lensSig;
        private bool lensAffinity;

        private readonly ScreenBgraCapturer syncLensCapturer = new ScreenBgraCapturer();
        private readonly byte[][] syncLensDst = new byte[2][];
        private int syncLensDstSlot;
        private bool syncLensEntered;

        private volatile int LensFrameMs = 16;
        private volatile bool lensPushPending;
        private static readonly bool animTrace = Environment.GetEnvironmentVariable("SIDESTACK_ANIM_TRACE") != null;
        private static readonly bool noFade = Environment.GetEnvironmentVariable("SIDESTACK_NOFADE") == "1";
        private int lastTraceMs;
        private bool animSettled = true;
        private int lensFail;

        private static readonly Brush TransparentBrush = FrozenBrush(Color.FromArgb(0, 0, 0, 0));

        private static Brush FrozenBrush(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private volatile int lensScalePct = 60;
        // TransparentBrush 是 Freeze() 过的共享画刷：只能用在"永远不再改"的属性上。
        // 图标槽位的 Background 不能用它 —— 悬停底色靠对该画刷做动画实现（见 CreateItemBox 的
        // AnimateBrush），冻结画刷不可动画，异常被吞掉后表现为"悬停底色整个没了"。
        private readonly ScreenBgraCapturer lensCapturer = new ScreenBgraCapturer();
        private readonly byte[][] lensDst = new byte[2][];
        private int lensDstSlot;

        private Canvas hoverLayer;

        private Border dropHintSlot;
        private Brush dropHintBrush;
        private Thickness dropHintThickness;
        private Border hoverBox;

        private const double HoverFrameInset = 1.0;

        private readonly Dictionary<Border, int> boxRow = new Dictionary<Border, int>();
        private readonly Dictionary<Border, int> boxCol = new Dictionary<Border, int>();

        private Border dragBox;
        private DockItem dragItem;
        private int dragFrom = -1;
        private int dragTo = -1;
        private Point dragStart;
        private bool dragging;
        private int lastDropMs;

        private bool panelPinned;

        public double DpiScale { get { return dpiScale; } }
        public double DockWidthDip { get { return dockWidth; } }
        public double DockHeightDip { get { return dockHeight; } }

        public int ItemCount
        {
            get
            {
                List<DockItem> list = IsLeft ? cfg.Left : cfg.Right;
                return list.Count - DockConfig.CountEmpty(list);
            }
        }

        public DockWindow(bool isLeft, DockConfig config)
        {
            IsLeft = isLeft;
            cfg = config;
            ApplyLensKnobs(config);
            if (isLeft) { Logger.Write("动画参数: 缓动=" + config.Easing + "，时长=" + config.AnimMs + "ms，刷新率=" + config.AnimFps + "Hz"); }

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;

            AllowDrop = true;
            DragOver += delegate (object s, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Drop += delegate (object s, DragEventArgs e)
            {
                try
                {
                    if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { return; }
                    string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files == null || files.Length == 0) { return; }
                    int idx = IndexAtPoint(e.GetPosition(this), Math.Max(1, ItemCount));
                    Logger.Write("面板收到拖入: " + files.Length + " 个路径，落点槽 " + idx);
                    if (FilesDropped != null)
                    {
                        FilesDropped(this, new System.Collections.Generic.List<string>(files), idx);
                    }
                    e.Handled = true;
                }
                catch (Exception ex) { Logger.Write("DockWindow.Drop", ex); }
            };

            ShowInTaskbar = false;

            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = false;
            SnapsToDevicePixels = true;

            Left = -10000;
            Top = -10000;
            Opacity = 0;

            BuildPanel();
            RebuildItems();
            Relayout();

            Loaded += delegate
            {
                UpdateDpi();

                RebuildItems();
                Relayout();
            };
        }

        public void ReassertToolWindowStyle()
        {
            try
            {
                if (hwnd == IntPtr.Zero) { return; }
                IntPtr ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
                long v = ex.ToInt64();
                v |= NativeMethods.WS_EX_TOOLWINDOW;
                v |= NativeMethods.WS_EX_NOACTIVATE;
                v &= ~(long)NativeMethods.WS_EX_APPWINDOW;
                if (v != ex.ToInt64())
                {
                    NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(v));
                    Logger.Write("面板外壳样式已重申（工具窗口 / 非任务栏）");
                }
            }
            catch (Exception ex2) { Logger.Write("ReassertToolWindowStyle", ex2); }
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                hwnd = h;
                IntPtr ex = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE);
                long v = ex.ToInt64();
                v |= NativeMethods.WS_EX_TOOLWINDOW;
                v |= NativeMethods.WS_EX_NOACTIVATE;
                v &= ~(long)NativeMethods.WS_EX_APPWINDOW;
                NativeMethods.SetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE, new IntPtr(v));
                UpdateDpi();
            }
            catch (Exception ex2)
            {
                Logger.Write("DockWindow.OnSourceInitialized", ex2);
            }

            UpdateDpi();
            RebuildItems();
            Relayout();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            UpdateDpi();
            InvalidateWindowOffset();
            RebuildItems();
            Relayout();
        }

        private void BuildPanel()
        {
            fringe = new Border();
            fringe.BorderThickness = new Thickness(1);
            fringe.SnapsToDevicePixels = true;
            fringe.Visibility = Visibility.Collapsed;
            fringe.IsHitTestVisible = false;
            fringe.Margin = new Thickness(ShadowPad - 2);

            panel = new Border();
            panel.BorderThickness = new Thickness(0);
            panel.SnapsToDevicePixels = true;
            panel.ClipToBounds = false;
            panel.Background = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
            panel.Margin = new Thickness(ShadowPad);
            rim = new Border();
            rim.BorderThickness = new Thickness(1);
            rim.SnapsToDevicePixels = true;
            rim.Margin = new Thickness(ShadowPad - 1);
            rim.IsHitTestVisible = false;
            rim.BorderBrush = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));

            grid = new Grid();
            grid.ClipToBounds = false;
            grid.Margin = new Thickness(0);

            hoverLayer = new Canvas();
            hoverLayer.ClipToBounds = false;
            hoverLayer.IsHitTestVisible = false;
            hoverLayer.Margin = new Thickness(DockConfig.PanelPadding);
            hoverBox = new Border();
            hoverBox.Background = null;
            hoverBox.BorderThickness = new Thickness(1);
            hoverBox.BorderBrush = TransparentBrush;
            hoverBox.IsHitTestVisible = false;
            hoverBox.SnapsToDevicePixels = true;
            hoverBox.RenderTransformOrigin = new Point(0.5, 0.5);
            hoverBox.RenderTransform = new ScaleTransform(1.0, 1.0);
            hoverBox.Opacity = 0;
            hoverLayer.Children.Add(hoverBox);

            Grid panelContent = new Grid();
            panelContent.ClipToBounds = false;
            panelContent.Children.Add(grid);
            panelContent.Children.Add(hoverLayer);
            panel.Child = panelContent;

            Grid host = new Grid();
            host.ClipToBounds = false;
            host.Children.Add(fringe);
            host.Children.Add(panel);
            host.Children.Add(rim);
            Content = host;

            panelMenu = BuildPanelMenu();
            WindowCapture.Guard(panelMenu);
            Theme.StyleMenu(panelMenu);
            panel.ContextMenu = panelMenu;

            ApplyEdgeMargins();
        }

        private void ApplyEdgeMargins()
        {
            ApplyMargins(ShadowPad, EdgePadDip());
        }

        private void ApplyMargins(double shadow, double edge)
        {
            if (IsLeft)
            {
                panel.Margin = new Thickness(shadow, shadow, shadow + edge, shadow);
                rim.Margin = new Thickness(shadow - 1, shadow - 1, shadow + edge - 1, shadow - 1);
                fringe.Margin = new Thickness(shadow - 2, shadow - 2, shadow + edge - 2, shadow - 2);
            }
            else
            {
                panel.Margin = new Thickness(shadow + edge, shadow, shadow, shadow);
                rim.Margin = new Thickness(shadow + edge - 1, shadow - 1, shadow - 1, shadow - 1);
                fringe.Margin = new Thickness(shadow + edge - 2, shadow - 2, shadow - 2, shadow - 2);
            }
        }

        private void RebuildItems()
        {
            UpdateDpi();

            DetachAnimations(grid);

            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            boxRow.Clear();
            boxCol.Clear();
            HideHoverBox();
            List<DockItem> items = IsLeft ? cfg.Left : cfg.Right;

            bool noFx = Environment.GetEnvironmentVariable("SIDESTACK_NOFX") == "1";

            if (cfg.PanelStyle == "clear") { ApplyClearShell(noFx); }
            else if (IsLensStyle(cfg.PanelStyle)) { ApplyLensShell(noFx); }
            else { ApplyGlassShell(noFx); }

            Color hoverBg = Ui.ParseColor(cfg.HoverColor, Colors.White);
            double hoverAlpha = cfg.HoverOpacity;

            double slotW, slotH, spacing, iconSize, panelH, panelW;
            int cols, rows;
            ComputeMetrics(items.Count, out slotW, out slotH, out spacing, out iconSize,
                           out panelH, out panelW, out cols, out rows);

            for (int c = 0; c < cols; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            for (int r = 0; r < rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < items.Count; i++)
            {
                DockItem item = items[i];
                Border box = CreateItemBox(item, i, items, hoverBg, hoverAlpha);
                int logicalCol = i / rows;
                Grid.SetRow(box, i % rows);
                Grid.SetColumn(box, IsLeft ? logicalCol : (cols - 1 - logicalCol));
                grid.Children.Add(box);
                boxRow[box] = Grid.GetRow(box);
                boxCol[box] = Grid.GetColumn(box);
            }

            Theme.StyleMenu(panelMenu);
        }

        private void ShowHoverBox(Border box)
        {
            if (box == null || hoverBox == null || hoverLayer == null) { return; }
            try
            {
                double w = box.ActualWidth > 0 ? box.ActualWidth : box.Width;
                double h = box.ActualHeight > 0 ? box.ActualHeight : box.Height;
                if (double.IsNaN(w) || w <= 0 || double.IsNaN(h) || h <= 0) { return; }

                double bw = Math.Max(1, w - HoverFrameInset * 2);
                double bh = Math.Max(1, h - HoverFrameInset * 2);

                Point slotTl = box.TranslatePoint(new Point(0, 0), hoverLayer);
                double left = slotTl.X + HoverFrameInset;
                double top = slotTl.Y + HoverFrameInset;

                double grow = (DockConfig.HoverScale - 1.0) / 2.0;
                double overW = bw * grow;
                double overH = bh * grow;

                double panelInnerLeft = -panelPadDip;
                double panelInnerRight = hoverLayer.ActualWidth + panelPadDip;
                double panelInnerTop = -panelPadDip;

                if (IsLeft) { left = Math.Max(left, panelInnerLeft + overW); }
                else { left = Math.Min(left, panelInnerRight - bw - overW); }

                top = Math.Max(top, panelInnerTop + overH);

                hoverBox.Width = bw;
                hoverBox.Height = bh;
                hoverBox.CornerRadius = new CornerRadius(Math.Max(0, box.CornerRadius.TopLeft - HoverFrameInset));
                hoverBox.BorderThickness = new Thickness(cfg.HoverBorderWidth);
                hoverBox.BorderBrush = new SolidColorBrush(Ui.ParseColor(cfg.HoverBorderColor, Colors.White));
                hoverBox.Background = null;
                Canvas.SetLeft(hoverBox, left);
                Canvas.SetTop(hoverBox, top);
                Theme.AnimateBounce(hoverBox, DockConfig.HoverScale, 200);
                AnimateOpacity(hoverBox, 1.0, 110);
            }
            catch (Exception ex) { Logger.Write("ShowHoverBox", ex); }
        }

        private void HideHoverBox()
        {
            if (hoverBox == null) { return; }
            try
            {
                Theme.AnimateScale(hoverBox, 1.0, 150);
                AnimateOpacity(hoverBox, 0.0, 150);
            }
            catch { }
        }

        private static void AnimateOpacity(UIElement e, double to, int ms)
        {
            try
            {
                DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
                a.FillBehavior = FillBehavior.HoldEnd;
                e.BeginAnimation(UIElement.OpacityProperty, a);
            }
            catch { }
        }

        private void ApplyGlassShell(bool noFx)
        {
            SetLensAffinity(false);
            StopLensLive();
            panel.Background = Ui.MakeBrush(Theme.GlassFill, DockConfig.PanelOpacity);
            panel.CornerRadius = new CornerRadius(DockConfig.PanelRadius);
            panel.Effect = noFx ? null : Theme.Shadow(20, 0.26, 3);

            rim.BorderBrush = new SolidColorBrush(Theme.GlassEdge);
            rim.BorderThickness = new Thickness(1);
            rim.CornerRadius = new CornerRadius(DockConfig.PanelRadius);
            rim.Background = null;

            fringe.Visibility = Visibility.Collapsed;
        }

        private void ApplyClearShell(bool noFx)
        {
            SetLensAffinity(false);
            StopLensLive();
            panel.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
            panel.CornerRadius = new CornerRadius(DockConfig.PanelRadius);
            panel.Effect = noFx ? null : Theme.Shadow(26, 0.17, 4);

            LinearGradientBrush rimBrush = new LinearGradientBrush();
            rimBrush.StartPoint = new Point(0.0, 0.0);
            rimBrush.EndPoint = new Point(0.0, 1.0);
            rimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0));
            rimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.10));
            rimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(86, 255, 255, 255), 0.5));
            rimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.90));
            rimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
            rim.BorderBrush = rimBrush;
            rim.BorderThickness = new Thickness(1.2);
            rim.CornerRadius = new CornerRadius(DockConfig.PanelRadius);
            rim.Background = null;

            fringe.CornerRadius = new CornerRadius(DockConfig.PanelRadius + 1.2);
            fringe.BorderThickness = new Thickness(1);
            LinearGradientBrush disp = new LinearGradientBrush();
            disp.StartPoint = new Point(0.0, 0.0);
            disp.EndPoint = new Point(1.0, 0.0);
            disp.GradientStops.Add(new GradientStop(Color.FromArgb(66, 0x7F, 0xB8, 0xFF), 0.0));
            disp.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5));
            disp.GradientStops.Add(new GradientStop(Color.FromArgb(66, 0xC9, 0xB6, 0xFF), 1.0));
            fringe.BorderBrush = disp;
            fringe.Background = Brushes.Transparent;
            fringe.Visibility = Visibility.Visible;
        }

        private void ApplyLensShell(bool noFx)
        {
            panel.CornerRadius = new CornerRadius(DockConfig.PanelRadius);
            panel.Effect = null;
            rim.CornerRadius = new CornerRadius(DockConfig.PanelRadius);
            rim.Background = null;
            rim.BorderThickness = new Thickness(1);
            fringe.Visibility = Visibility.Collapsed;

            panel.Background = (lensBrush != null)
                ? (Brush)lensBrush
                : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));

            rim.BorderBrush = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255));
            SetLensAffinity(true);
            UpdateLensBackdrop(false);
            SyncLensLive();
        }

        private void SetLensAffinity(bool on)
        {
            try
            {
                if (on && capturable) { on = false; }
                if (hwnd == IntPtr.Zero || lensAffinity == on) { return; }
                uint v = on ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
                bool ok = NativeMethods.SetWindowDisplayAffinity(hwnd, v);
                lensAffinity = on && ok;
                if (lensAffinity) { NativeMethods.DwmFlush(); }
                Logger.Write("光感：抓屏排除自己 -> " + lensAffinity + (ok ? "" : "（API 失败，实时背景可能自反馈）"));
            }
            catch (Exception ex) { Logger.Write("SetLensAffinity", ex); }
        }

        private bool capturable = Environment.GetEnvironmentVariable("SIDESTACK_CAPTURABLE") == "1";
        public bool Capturable { get { return capturable; } }

        public void SetCapturable(bool on)
        {
            capturable = on;
            SetLensAffinity(!on && IsLensStyle(cfg != null ? cfg.PanelStyle : "glass"));
            Logger.Write((on ? "允许截图: 开（面板会出现在截图/录屏里，光感背景可能自反馈）" : "允许截图: 关（已恢复抓屏排除）"));
        }

        private void SyncLensLive()
        {
            bool want = (cfg != null && IsLensStyle(cfg.PanelStyle) && ItemCount > 0
                         && !forcedHidden && (wantShow || animT > 0.001));
            if (want) { StartLensLive(); }
            else if (animT <= 0.001) { StopLensLive(); }
        }

        private void StartLensLive()
        {
            if (lensThreadRun || hwnd == IntPtr.Zero) { return; }
            SetLensAffinity(true);
            lensThreadRun = true;
            lensSig = 0;
            Logger.Write("光感实时循环启动（" + (IsLeft ? "左" : "右") + "）: 间隔 " + LensFrameMs + " ms → 目标 "
                         + (int)Math.Round(1000.0 / Math.Max(1, LensFrameMs)) + " fps；内部渲染 "
                         + lensScalePct + "%");
            lensThread = new System.Threading.Thread(LensLiveLoop);
            lensThread.IsBackground = true;
            try { lensThread.Priority = System.Threading.ThreadPriority.BelowNormal; } catch { }
            lensThread.Name = "SideStackLens" + (IsLeft ? "L" : "R");
            lensThread.Start();
        }

        private void StopLensLive()
        {
            lensThreadRun = false;
            lensThread = null;
        }

        private static bool IsLensStyle(string s) { return s == "lens" || s == "lensstrong"; }

        private bool LensFollowsAnimClock()
        {
            return cfg != null && cfg.PanelStyle == "lensstrong";
        }

        private LensGlassOptions LensPreset(int w, int h, double dpiScale)
        {
            bool strong = (cfg != null && cfg.PanelStyle == "lensstrong");
            LensGlassOptions o = strong ? LensGlassOptions.SidebarStrong(w, h, dpiScale)
                                        : LensGlassOptions.Sidebar(w, h, dpiScale);

            if (strong)
            {
                o.ScreenSide = IsLeft ? LensGlassOptions.ScreenSideLeft
                                      : LensGlassOptions.ScreenSideRight;
            }
            return o;
        }

        private void StepLensFrameSync(int winX, int winY)
        {
            try
            {
                if (!LensFollowsAnimClock()) { return; }
                if (ItemCount == 0 || hwnd == IntPtr.Zero) { return; }
                if (winX == int.MinValue) { return; }
                int w = ToPx(dockWidth), h = ToPx(dockHeight);
                if (w <= 0 || h <= 0) { return; }

                double edge = EdgePadDip();
                int x = winX + ToPx(IsLeft ? ShadowPad : ShadowPad + edge);
                int y = winY + ToPx(ShadowPad);

                byte[] glass; int gw, gh;
                if (!RenderLensInto(x, y, w, h, syncLensCapturer, syncLensDst, ref syncLensDstSlot,
                                    out glass, out gw, out gh)) { return; }

                PushLensFrame(glass, gw, gh, 0, true);

                if (!syncLensEntered)
                {
                    syncLensEntered = true;
                    Logger.Write("光感·强：背景改为逐帧同步渲染（在面板动画帧内抓屏+渲染，与面板同帧）: "
                                 + w + "×" + h + " 物理像素，内部渲染 " + lensScalePct + "%");
                }
            }
            catch (Exception ex) { Logger.Write("StepLensFrameSync", ex); }
        }

        private bool RenderLensInto(int x, int y, int w, int h,
            ScreenBgraCapturer capturer, byte[][] dst, ref int dstSlot,
            out byte[] glass, out int gw, out int gh)
        {
            glass = null; gw = 0; gh = 0;

            int rw0 = Math.Max(2, (int)Math.Round(w * lensScalePct / 100.0));
            int rh0 = Math.Max(2, (int)Math.Round(h * lensScalePct / 100.0));
            LensFrame lf = LensGeometry(x, y, w, h);

            DateTime t0 = DateTime.UtcNow;

            byte[] src = capturer.Capture(lf.X, lf.Y, lf.W, lf.H);
            if (src == null) { return false; }
            int capMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;

            ulong sig = Signature(src);

            if (sig == System.Threading.Volatile.Read(ref lensSig)) { PerfFrame(capMs, -1); return false; }
            System.Threading.Volatile.Write(ref lensSig, sig);

            int rw = rw0, rh = rh0;
            int slot = (dstSlot = 1 - dstSlot);
            int need = rw * rh * 4;
            if (dst[slot] == null || dst[slot].Length != need) { dst[slot] = new byte[need]; }

            LensGlassOptions o = LensPreset(rw, rh, dpiScale * lensScalePct / 100.0);
            o.SrcOffsetX = x - lf.X;
            o.SrcOffsetY = y - lf.Y;
            o.SrcPanelW = w;
            o.SrcPanelH = h;
            o.MarginLeft = lf.Ml;
            o.MarginRight = lf.Mr;
            o.MarginTop = lf.Mt;
            o.MarginBottom = lf.Mb;
            int dop = Math.Max(2, Environment.ProcessorCount / 4);
            byte[] outGlass = LensGlassRenderer.Render(src, lf.W, lf.H, o, dop, dst[slot]);
            if (outGlass == null) { return false; }

            int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            PerfFrame(capMs, ms - capMs);
            glass = outGlass; gw = rw; gh = rh;
            return true;
        }

        private struct LensFrame
        {
            public int X, Y, W, H;
            public double Ml, Mr, Mt, Mb;
        }

        private LensFrame LensGeometry(int panelLeft, int panelTop, int w, int h)
        {
            LensFrame f = new LensFrame();
            f.X = panelLeft;
            f.Y = panelTop;
            f.W = w;
            f.H = h;
            f.Ml = 0.0; f.Mr = 0.0; f.Mt = 0.0; f.Mb = 0.0;
            return f;
        }

        private void LensLiveLoop()
        {
            while (lensThreadRun)
            {
                int sleep = LensFrameMs;
                try
                {
                    if (cfg == null || !IsLensStyle(cfg.PanelStyle)) { break; }
                    if (!wantShow && animT <= 0.001) { break; }

                    if (LensFollowsAnimClock() && IsAnimating) { System.Threading.Thread.Sleep(2); continue; }

                    if (lensPushPending) { System.Threading.Thread.Sleep(2); continue; }

                    int w = ToPx(dockWidth), h = ToPx(dockHeight);
                    if (w <= 0 || h <= 0 || lastPlacedWinX == int.MinValue || hwnd == IntPtr.Zero)
                    {
                        System.Threading.Thread.Sleep(30);
                        continue;
                    }

                    double edge = EdgePadDip();
                    int x = lastPlacedWinX + ToPx(IsLeft ? ShadowPad : ShadowPad + edge);
                    int y = lastPlacedWinY + ToPx(ShadowPad);

                    DateTime t0 = DateTime.UtcNow;
                    byte[] glass; int rw, rh;
                    if (!RenderLensInto(x, y, w, h, lensCapturer, lensDst, ref lensDstSlot,
                                        out glass, out rw, out rh))
                    {
                        int skip = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                        sleep = LensFrameMs - skip;
                        if (sleep < 1) { sleep = 1; }
                        continue;
                    }

                    lensPushPending = true;
                    Dispatcher.BeginInvoke(new Action(delegate { PushLensFrame(glass, rw, rh, 0, false); }));

                    int elapsed = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                    sleep = LensFrameMs - elapsed;
                    if (sleep < 1) { sleep = 1; }
                }
                catch (Exception ex) { Logger.Write("LensLiveLoop", ex); sleep = 200; }
                try { System.Threading.Thread.Sleep(sleep); } catch { }
            }
            lensThreadRun = false;
        }

        private int perfFrames, perfCapSum, perfRenderSum, perfRenderCount, perfPushSum, perfPushCount;
        private DateTime perfT0;

        private void PerfFrame(int capMs, int renderMs)
        {
            try
            {
                perfFrames++;
                perfCapSum += capMs;
                if (renderMs >= 0) { perfRenderSum += renderMs; perfRenderCount++; }
                if (perfT0 == default(DateTime)) { perfT0 = DateTime.UtcNow; }
                if (perfLog && perfFrames >= 90)
                {
                    double sec = (DateTime.UtcNow - perfT0).TotalSeconds;
                    Logger.Write("光感性能: " + (perfFrames / sec).ToString("F1") + " fps；抓屏 "
                        + (perfCapSum / perfFrames) + " ms，渲染 "
                        + (perfRenderCount > 0 ? (perfRenderSum / perfRenderCount) : 0) + " ms（重算 "
                        + perfRenderCount + "/" + perfFrames + " 帧），位图上传 "
                        + (perfPushCount > 0 ? (perfPushSum / perfPushCount) : 0) + " ms");
                    perfFrames = 0; perfCapSum = 0; perfRenderSum = 0; perfRenderCount = 0;
                    perfPushSum = 0; perfPushCount = 0; perfT0 = DateTime.UtcNow;
                }
            }
            catch { }
        }

        private static ulong Signature(byte[] px)
        {
            ulong h = 1469598103934665603UL;
            int step = 4 * 97;
            for (int i = 0; i + 3 < px.Length; i += step)
            {
                h ^= px[i]; h *= 1099511628211UL;
                h ^= px[i + 1]; h *= 1099511628211UL;
                h ^= px[i + 2]; h *= 1099511628211UL;
            }
            h ^= (ulong)px.Length;
            return h;
        }

        private void PushLensFrame(byte[] glass, int w, int h, int ms, bool sync)
        {
            DateTime pushT0 = DateTime.UtcNow;
            try
            {
                if (cfg == null || !IsLensStyle(cfg.PanelStyle)) { return; }

                if (!sync && LensFollowsAnimClock() && IsAnimating) { return; }
                bool created = false;
                if (lensWb == null || lensWb.PixelWidth != w || lensWb.PixelHeight != h)
                {
                    lensWb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                    created = true;
                }

                lensWb.WritePixels(new Int32Rect(0, 0, w, h), glass, w * 4, 0);
                perfPushSum += (int)(DateTime.UtcNow - pushT0).TotalMilliseconds; perfPushCount++;
                if (created)
                {
                    lensBrush = new ImageBrush(lensWb);
                    lensBrush.Stretch = Stretch.Fill;
                    panel.Background = lensBrush;
                }
                if (perfLog && (lensFrames < 2 || (lensFrames % 120) == 0))
                {
                    Logger.Write("光感实时帧: " + w + "×" + h + " " + ms + " ms（第 " + (lensFrames + 1) + " 帧"
                                 + (sync ? "，同步" : "") + "）");
                }

                if (lensDumpDir != null && (lensFrames % 20) == 0) { DumpGlass(glass, w, h); }
                lensFrames++;
            }
            catch (Exception ex) { Logger.Write("PushLensFrame", ex); }
            finally { lensPushPending = false; }
        }

        private int lensFrames;

        private static readonly string lensDumpDir = Environment.GetEnvironmentVariable("SIDESTACK_LENS_DUMP");
        private static readonly bool perfLog = Environment.GetEnvironmentVariable("SIDESTACK_LENS_PERF") != null;

        private void DumpGlass(byte[] px, int w, int h)
        {
            try
            {
                System.IO.Directory.CreateDirectory(lensDumpDir);
                string file = System.IO.Path.Combine(lensDumpDir,
                    "glass-" + (IsLeft ? "L" : "R") + "-" + lensFrames.ToString("D5") + ".png");
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(
                        new System.Drawing.Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int row = 0; row < h; row++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(px, row * w * 4,
                                IntPtr.Add(bd.Scan0, row * bd.Stride), w * 4);
                        }
                    }
                    finally { bmp.UnlockBits(bd); }
                    bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception ex) { Logger.Write("DumpGlass", ex); }
        }

        private void UpdateLensBackdrop(bool force)
        {
            try
            {
                if (cfg == null || !IsLensStyle(cfg.PanelStyle)) { return; }
                if (ItemCount == 0) { return; }
                if (double.IsNaN(Width) || double.IsNaN(Height)) { return; }
                if (lensBusy) { lensPending = true; return; }
                if (!force && animT > 0.001) { return; }

                RECT mon = ScreenUtils.GetPrimaryMonitorPx();
                RECT work = ScreenUtils.GetPrimaryWorkPx();
                int monL = mon.Left, monR = mon.Right;
                int workT = work.Top, workB = work.Bottom;
                if (workB - workT <= 100) { workT = mon.Top; workB = mon.Bottom; }

                int w = ToPx(dockWidth);
                int h = ToPx(dockHeight);
                int top = workT + ToPx(EdgeMargin);
                int minTop = workB - ToPx(EdgeMargin) - h;
                if (top > minTop) { top = minTop; }
                int left = IsLeft ? (monL) : (monR - w);

                string key = left + "," + top + "," + w + "," + h + "," + dpiScale.ToString("F3");
                if (!force && lensBrush != null && key == lensKey) { return; }
                lensKey = key;

                byte[] src = ScreenUtils.CaptureBgra(left, top, w, h);
                if (src == null) { return; }

                lensBusy = true;

                int usePct = lensScalePct;
                double scale = dpiScale * usePct / 100.0;
                int pw = Math.Max(2, (int)Math.Round(w * usePct / 100.0));
                int ph = Math.Max(2, (int)Math.Round(h * usePct / 100.0));
                DateTime t0 = DateTime.UtcNow;
                System.Threading.Tasks.Task.Factory.StartNew(delegate
                {
                    byte[] glass = null;
                    try
                    {
                        LensGlassOptions o = LensPreset(pw, ph, scale);
                        glass = LensGlassRenderer.Render(src, w, h, o);
                    }
                    catch (Exception ex) { Logger.Write("LensGlass.Render", ex); }
                    int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                    Dispatcher.BeginInvoke(new Action(delegate { ApplyLensBitmap(glass, pw, ph, ms); }));
                });
            }
            catch (Exception ex) { Logger.Write("UpdateLensBackdrop", ex); }
        }

        private byte[] CaptureBackdropQuiet(int monL, int monR, int workT,
                                            int left, int top, int w, int h)
        {
            int savedX = lastPlacedWinX, savedY = lastPlacedWinY;
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    int awayX = IsLeft ? (monL - w - 64) : (monR + 64);
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, awayX, workT, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                    NativeMethods.DwmFlush();
                }
                return ScreenUtils.CaptureBgra(left, top, w, h);
            }
            catch (Exception ex) { Logger.Write("CaptureBackdropQuiet", ex); return null; }
            finally
            {
                if (hwnd != IntPtr.Zero && savedX != int.MinValue)
                {
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, savedX, savedY, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                }
            }
        }

        public void RequestLensRefresh(int delayMs)
        {
            try
            {
                if (cfg == null || !IsLensStyle(cfg.PanelStyle)) { return; }
                DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
                t.Interval = TimeSpan.FromMilliseconds(Math.Max(50, delayMs));
                t.Tick += delegate
                {
                    t.Stop();
                    UpdateLensBackdrop(false);
                };
                t.Start();
            }
            catch (Exception ex) { Logger.Write("RequestLensRefresh", ex); }
        }

        private void ApplyLensBitmap(byte[] glass, int w, int h, int ms)
        {
            lensBusy = false;

            int expW = Math.Max(2, (int)Math.Round(ToPx(dockWidth) * lensScalePct / 100.0));
            int expH = Math.Max(2, (int)Math.Round(ToPx(dockHeight) * lensScalePct / 100.0));
            bool stale = (w != expW || h != expH);
            if (glass != null && !stale)
            {
                try
                {
                    if (lensWb == null || lensWb.PixelWidth != w || lensWb.PixelHeight != h)
                    {
                        lensWb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                        lensBrush = new ImageBrush(lensWb);
                        lensBrush.Stretch = Stretch.Fill;
                    }
                    lensWb.WritePixels(new Int32Rect(0, 0, w, h), glass, w * 4, 0);

                    if (cfg != null && IsLensStyle(cfg.PanelStyle)) { panel.Background = lensBrush; }
                    Logger.Write("光感玻璃底已更新: " + w + "×" + h + " px, " + ms + " ms");
                }
                catch (Exception ex) { Logger.Write("ApplyLensBitmap", ex); }
            }
            else if (stale)
            {
                Logger.Write("光感：几何在渲染期间变了（" + w + "×" + h + " → "
                             + expW + "×" + expH + "），丢弃并重算");
            }

            if (glass == null)
            {
                lensFail++;
                if (lensFail <= 3) { RequestLensRefresh(80 * lensFail); }
                else if (lensFail == 4) { Logger.Write("光感：玻璃底渲染连续失败，暂停重试（实时循环仍会尝试）"); }
                return;
            }
            lensFail = 0;

            if (lensPending || stale)
            {
                lensPending = false;
                RequestLensRefresh(80);
            }
        }

        private Border CreateItemBox(DockItem item, int index, List<DockItem> items,
            Color hoverBg, double hoverAlpha)
        {
            Border b = new Border();

            b.BorderThickness = new Thickness(0);
            b.SnapsToDevicePixels = true;
            b.ClipToBounds = false;
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            b.RenderTransform = new ScaleTransform(1, 1);
            b.Background = new SolidColorBrush(Colors.Transparent);
            b.BorderBrush = TransparentBrush;

            if (item.IsEmpty)
            {
                b.BorderThickness = new Thickness(0);
                b.Background = TransparentBrush;
                b.Cursor = Cursors.Arrow;
                b.ContextMenu = null;
                b.ToolTip = null;
                return b;
            }

            Image img = new Image();
            img.Stretch = Stretch.Uniform;
            img.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            string customIcon = CustomIconOf(item);
            double iconScale = ScaleOf(item);
            try
            {
                img.Source = (customIcon != null)
                    ? IconLoader.GetImage(customIcon, (int)Math.Max(32, ToPx(cfg != null ? cfg.IconSize : 44)))
                    : IconLoader.Get(item.Path);
                if (customIcon != null || Math.Abs(iconScale - 1.0) > 0.001)
                {
                    Logger.Write("单图标外观: " + item.Path + " → 图片=[" + (customIcon == null ? "默认" : customIcon) + "] 缩放=" + iconScale.ToString("F2"));
                }
                if (Math.Abs(iconScale - 1.0) > 0.001)
                {
                    img.RenderTransformOrigin = new Point(0.5, 0.5);
                    img.RenderTransform = new ScaleTransform(iconScale, iconScale);
                }
            }
            catch (Exception ex) { Logger.Write("icon load " + item.Path, ex); }

            Grid content = new Grid();
            content.VerticalAlignment = VerticalAlignment.Stretch;
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            img.HorizontalAlignment = HorizontalAlignment.Center;
            img.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(img, 0);
            content.Children.Add(img);

            if (cfg.NameMode == "always")
            {
                TextBlock lab = new TextBlock();
                lab.Text = item.Name;
                lab.FontSize = NameFont;
                lab.Foreground = new SolidColorBrush(Theme.Ink);
                lab.TextTrimming = TextTrimming.CharacterEllipsis;
                lab.TextAlignment = TextAlignment.Center;
                lab.HorizontalAlignment = HorizontalAlignment.Center;
                lab.VerticalAlignment = VerticalAlignment.Center;
                lab.IsHitTestVisible = false;

                Border pill = new Border();
                pill.CornerRadius = new CornerRadius(6);
                pill.Background = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
                pill.Padding = new Thickness(5, 1, 5, 1);
                pill.Margin = new Thickness(0, 1, 0, 0);
                pill.IsHitTestVisible = false;
                pill.Child = lab;

                Grid nameHost = new Grid();
                nameHost.Children.Add(pill);
                Grid.SetRow(nameHost, 1);
                content.Children.Add(nameHost);
            }

            b.Child = content;
            b.Tag = img;
            b.Cursor = Cursors.Hand;

            SolidColorBrush bg = (SolidColorBrush)b.Background;
            Color toBg = Color.FromArgb((byte)Math.Round(hoverAlpha * 255.0), hoverBg.R, hoverBg.G, hoverBg.B);
            b.MouseEnter += delegate
            {
                AnimateBrush(bg, toBg, 110);
                Theme.AnimateBounce(b, DockConfig.HoverScale, 200);
                ShowHoverBox(b);
            };
            b.MouseLeave += delegate
            {
                AnimateBrush(bg, Colors.Transparent, 170);
                Theme.AnimateScale(b, 1.0, 150);
                HideHoverBox();
            };

            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs me)
            {
                if (dragging || MovedFar(me.GetPosition(this))) { return; }
                me.Handled = true;
                if (ItemActivated != null) { ItemActivated(this, item); }
            };

            b.ContextMenu = BuildItemMenu(item, index, items);
            Theme.StyleMenu(b.ContextMenu);
            WindowCapture.Guard(b.ContextMenu);
            if (cfg.NameMode == "hover")
            {
                ToolTip tt = new ToolTip();
                tt.Content = item.Name;
                tt.Placement = IsLeft ? PlacementMode.Right : PlacementMode.Left;
                tt.PlacementTarget = b;
                tt.HorizontalOffset = IsLeft ? 6 : -6;
                tt.HasDropShadow = true;
                Theme.StyleToolTip(tt);
                b.ToolTip = tt;
            }

            HookDrag(b, item, index, items);
            return b;
        }

        private void HookDrag(Border box, DockItem item, int index, List<DockItem> items)
        {
            if (!cfg.DragReorder) { return; }

            box.PreviewMouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount != 1) { return; }
                dragBox = box;
                dragItem = item;
                dragFrom = index;
                dragTo = index;
                dragStart = e.GetPosition(this);
                dragging = false;
            };

            box.PreviewMouseMove += delegate (object s, MouseEventArgs e)
            {
                if (dragBox != box || item == null) { return; }
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    EndDrag(false);
                    return;
                }
                Point p = e.GetPosition(this);
                if (!dragging)
                {
                    if (Math.Abs(p.X - dragStart.X) < 6 && Math.Abs(p.Y - dragStart.Y) < 6) { return; }
                    dragging = true;
                    box.CaptureMouse();
                    box.Opacity = 0.55;
                    ValidateDragList();
                    Theme.AnimateScale(box, 1.14, 110);
                }

                int t = IndexAtPoint(p, items.Count);
                if (t != dragTo)
                {
                    dragTo = t;
                    ClearDropHints();
                    HighlightDrop(dragTo, items.Count);
                }
            };

            box.PreviewMouseLeftButtonUp += delegate
            {
                if (dragBox != box) { return; }
                EndDrag(true);
            };
        }

        private void ValidateDragList()
        {
            List<DockItem> list = IsLeft ? cfg.Left : cfg.Right;
            if (dragFrom < 0 || dragFrom >= list.Count || !object.ReferenceEquals(list[dragFrom], dragItem))
            {
                dragFrom = list.IndexOf(dragItem);
            }
        }

        private void EndDrag(bool commit)
        {
            Border box = dragBox;
            bool moved = dragging;
            DockItem it = dragItem;
            int from = dragFrom;
            int to = dragTo;
            dragBox = null;
            dragItem = null;
            dragFrom = -1;
            dragTo = -1;
            dragging = false;

            if (box != null)
            {
                box.Opacity = 1.0;
                Theme.AnimateScale(box, 1.0, 120);
                if (box.IsMouseCaptured) { box.ReleaseMouseCapture(); }
            }
            ClearDropHints();

            if (!moved || !commit || it == null) { return; }

            List<DockItem> list = IsLeft ? cfg.Left : cfg.Right;
            ValidateDragListFor(list, it, ref from);
            if (from < 0) { return; }
            if (to < 0) { to = 0; }
            if (to > list.Count - 1) { to = list.Count - 1; }
            if (from == to) { return; }

            try
            {
                DockItem tmp = list[from];
                list[from] = list[to];
                list[to] = tmp;
                ConfigStore.Save(cfg);
                Logger.Write("图标换位: " + it.Name + " " + from + " -> " + to);
            }
            catch (Exception ex)
            {
                Logger.Write("图标换位失败", ex);
            }

            lastDropMs = Clock.NowMs();
            RebuildItems();
            Relayout();
            if (LayoutChanged != null) { LayoutChanged(this); }
        }

        private static void ValidateDragListFor(List<DockItem> list, DockItem it, ref int from)
        {
            if (from < 0 || from >= list.Count || !object.ReferenceEquals(list[from], it))
            {
                from = list.IndexOf(it);
            }
        }

        private List<Border> SlotBoxes()
        {
            List<Border> r = new List<Border>();
            for (int i = 0; i < grid.Children.Count; i++)
            {
                Border b = grid.Children[i] as Border;
                if (b != null) { r.Add(b); }
            }
            return r;
        }

        private int IndexAtPoint(Point p, int count)
        {
            if (count <= 0) { return 0; }
            double best = double.MaxValue;
            int bestIdx = 0;
            List<Border> boxes = SlotBoxes();
            for (int i = 0; i < boxes.Count && i < count; i++)
            {
                FrameworkElement fe = boxes[i];
                Point tl = fe.TranslatePoint(new Point(0, 0), this);
                double cx = tl.X + fe.ActualWidth / 2.0;
                double cy = tl.Y + fe.ActualHeight / 2.0;
                double d = (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy);
                if (d < best) { best = d; bestIdx = i; }
            }
            return bestIdx;
        }

        private void ClearDropHints()
        {
            RestoreDropHint();
            List<Border> boxes = SlotBoxes();
            for (int i = 0; i < boxes.Count; i++)
            {
                Border b = boxes[i];
                if (!(b.RenderTransform is ScaleTransform)) { continue; }
                if (b != dragBox)
                {
                    Theme.AnimateScale(b, 1.0, 120);
                }
            }
        }

        private string CustomIconOf(DockItem it)
        {
            if (cfg == null || cfg.CustomIcons == null || it == null || string.IsNullOrEmpty(it.Path)) { return null; }
            for (int i = 0; i < cfg.CustomIcons.Count; i++)
            {
                string s = cfg.CustomIcons[i];
                int j = s.IndexOf('|');
                if (j > 0 && string.Equals(s.Substring(0, j), it.Path, StringComparison.OrdinalIgnoreCase))
                {
                    return s.Substring(j + 1);
                }
            }
            return null;
        }

        private double ScaleOf(DockItem it)
        {
            if (cfg == null || cfg.IconScales == null || it == null || string.IsNullOrEmpty(it.Path)) { return 1.0; }
            for (int i = 0; i < cfg.IconScales.Count; i++)
            {
                string s = cfg.IconScales[i];
                int j = s.IndexOf('|');
                if (j <= 0) { continue; }
                if (!string.Equals(s.Substring(0, j), it.Path, StringComparison.OrdinalIgnoreCase)) { continue; }
                double v;
                if (double.TryParse(s.Substring(j + 1), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                {
                    return Math.Max(0.6, Math.Min(1.4, v));
                }
            }
            return 1.0;
        }

        private void HighlightDrop(int index, int count)
        {
            if (index < 0 || index >= count || index >= grid.Children.Count) { return; }
            Border b = grid.Children[index] as Border;
            if (b == null || b == dragBox) { return; }
            Theme.AnimateScale(b, 1.12, 110);

            if (!object.ReferenceEquals(dropHintSlot, b))
            {
                RestoreDropHint();
                dropHintSlot = b;
                dropHintBrush = b.BorderBrush;
                dropHintThickness = b.BorderThickness;
                b.BorderBrush = new SolidColorBrush(Theme.Accent);
                b.BorderThickness = new Thickness(2);
                Logger.Write("落位高亮: 目标槽 " + index);
            }
        }

        private void RestoreDropHint()
        {
            if (dropHintSlot == null) { return; }
            try
            {
                dropHintSlot.BorderBrush = dropHintBrush;
                dropHintSlot.BorderThickness = dropHintThickness;
            }
            catch { }
            dropHintSlot = null;
        }

        private bool MovedFar(Point p)
        {
            return Math.Abs(p.X - dragStart.X) > 6 || Math.Abs(p.Y - dragStart.Y) > 6;
        }

        private ContextMenu BuildItemMenu(DockItem item, int index, List<DockItem> items)
        {
            ContextMenu cm = new ContextMenu();

            MenuItem miOpen = new MenuItem();
            miOpen.Header = "打开";
            miOpen.Click += delegate { if (ItemActivated != null) { ItemActivated(this, item); } };

            MenuItem miReveal = new MenuItem();
            miReveal.Header = "打开文件所在位置";
            miReveal.Click += delegate { if (RevealRequested != null) { RevealRequested(this, item); } };

            MenuItem miRename = new MenuItem();
            miRename.Header = "修改名称…";
            miRename.Click += delegate
            {
                cm.IsOpen = false;
                Dispatcher.BeginInvoke(new Action(delegate { RenameItem(item); }), DispatcherPriority.Background);
            };

            MenuItem miTransfer = new MenuItem();
            miTransfer.Header = IsLeft ? "转移到右侧" : "转移到左侧";
            miTransfer.Click += delegate
            {
                if (TransferRequested != null) { TransferRequested(this, item); }
            };

            MenuItem miRound = new MenuItem();
            miRound.Header = "不修改圆角";
            miRound.IsCheckable = true;
            miRound.IsChecked = IsNoRound(item.Path);
            miRound.Click += delegate
            {
                if (NoRoundToggled != null) { NoRoundToggled(this, item, !IsNoRound(item.Path)); }
            };

            MenuItem miRemove = new MenuItem();
            miRemove.Header = "从 Dock 移除";
            miRemove.Click += delegate { if (RemoveRequested != null) { RemoveRequested(this, item); } };

            MenuItem miSet = new MenuItem();
            miSet.Header = "Dock 设置…";
            miSet.Click += delegate { if (SettingsRequested != null) { SettingsRequested(this); } };

            cm.Items.Add(miOpen);
            cm.Items.Add(miReveal);
            cm.Items.Add(new Separator());
            cm.Items.Add(miRename);
            cm.Items.Add(miTransfer);

            if (cfg != null && cfg.IconRadiusRatio > 0.0001) { cm.Items.Add(miRound); }

            MenuItem miIcon = new MenuItem();
            miIcon.Header = "自定义图标…";
            miIcon.Click += delegate { if (CustomIconRequested != null) { CustomIconRequested(this, item); } };
            cm.Items.Add(miIcon);

            string ci = CustomIconOf(item);
            if (ci != null)
            {
                MenuItem miIconClear = new MenuItem();
                miIconClear.Header = "恢复默认图标";
                miIconClear.Click += delegate
                {
                    if (CustomIconRequested != null) { CustomIconRequested(this, item); }
                };
                miIconClear.Tag = "clear";
                cm.Items.Add(miIconClear);
            }

            MenuItem miScale = new MenuItem();
            miScale.Header = "图标缩放";

            WindowCapture.Guard(miScale);
            miScale.BorderBrush = new SolidColorBrush(Theme.Line);
            double cur = ScaleOf(item);
            double[] opts = new double[] { 0.7, 0.85, 1.0, 1.15, 1.3 };
            for (int oi = 0; oi < opts.Length; oi++)
            {
                double sc = opts[oi];
                MenuItem one = new MenuItem();
                one.Header = (int)Math.Round(sc * 100) + "%" + (Math.Abs(cur - sc) < 0.001 ? "  ✓" : "");
                one.Click += delegate
                {
                    if (IconScaleRequested != null) { IconScaleRequested(this, item, sc); }
                };
                miScale.Items.Add(one);
            }
            cm.Items.Add(miScale);
            cm.Items.Add(new Separator());
            cm.Items.Add(miRemove);
            cm.Items.Add(new Separator());
            cm.Items.Add(miSet);
            return cm;
        }

        private bool IsNoRound(string path)
        {
            if (cfg == null || cfg.NoRound == null || string.IsNullOrEmpty(path)) { return false; }
            for (int i = 0; i < cfg.NoRound.Count; i++)
            {
                if (string.Equals(cfg.NoRound[i], path, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private ContextMenu BuildPanelMenu()
        {
            ContextMenu cm = new ContextMenu();

            MenuItem miSet = new MenuItem();
            miSet.Header = "Dock 设置…";
            miSet.Click += delegate { if (SettingsRequested != null) { SettingsRequested(this); } };

            MenuItem miReload = new MenuItem();
            miReload.Header = "重新加载图标与配置";
            miReload.Click += delegate { if (ReloadRequested != null) { ReloadRequested(this); } };

            MenuItem miFolder = new MenuItem();
            miFolder.Header = "打开配置文件夹";
            miFolder.Click += delegate { if (OpenConfigFolderRequested != null) { OpenConfigFolderRequested(this); } };

            MenuItem miExit = new MenuItem();
            miExit.Header = "退出 SideStack";
            miExit.Click += delegate { if (ExitRequested != null) { ExitRequested(this); } };

            cm.Items.Add(miSet);
            cm.Items.Add(miReload);
            cm.Items.Add(new Separator());
            cm.Items.Add(miFolder);
            cm.Items.Add(new Separator());
            cm.Items.Add(miExit);
            return cm;
        }

        private void RenameItem(DockItem item)
        {
            try
            {
                string nm = Dialog.Prompt("修改图标名称", "显示名称（留空 = 用文件名）：", item.Name);
                if (nm == null) { return; }
                nm = nm.Trim();
                if (nm.Length == 0)
                {
                    nm = System.IO.Path.GetFileNameWithoutExtension(item.Path);
                }
                item.Name = nm;
                RebuildItems();
                Relayout();
                ConfigStore.Save(cfg);
                Logger.Write("图标改名 -> " + nm);
                if (LayoutChanged != null) { LayoutChanged(this); }
            }
            catch (Exception ex) { Logger.Write("RenameItem", ex); }
        }

        private static void AnimateBrush(SolidColorBrush brush, Color to, int ms)
        {
            try
            {
                ColorAnimation a = new ColorAnimation(to, TimeSpan.FromMilliseconds(ms));
                a.FillBehavior = FillBehavior.HoldEnd;
                brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
            }
            catch { }
        }

        public void UpdateDpi()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                uint dpi = 0;
                if (h != IntPtr.Zero) { dpi = NativeMethods.GetDpiForWindow(h); }
                if (dpi >= 72 && dpi <= 480)
                {
                    dpiScale = dpi / 96.0;
                }
                else
                {
                    dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                }
            }
            catch
            {
                dpiScale = 1.0;
            }
            if (dpiScale <= 0.1 || dpiScale > 8) { dpiScale = 1.0; }
            shadowPadPx = ToPx(ShadowPad);

            if (cfg != null && IsLensStyle(cfg.PanelStyle)) { UpdateLensBackdrop(true); }
        }

        private static void DetachAnimations(DependencyObject root)
        {
            try
            {
                if (root == null) { return; }
                UIElement el = root as UIElement;
                if (el != null) { el.BeginAnimation(UIElement.OpacityProperty, null); }
                Border b = root as Border;
                if (b != null)
                {
                    b.BeginAnimation(Border.BackgroundProperty, null);
                    b.BeginAnimation(Border.BorderBrushProperty, null);
                    ScaleTransform st = b.RenderTransform as ScaleTransform;
                    if (st != null)
                    {
                        st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    }
                }
                int n = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < n; i++)
                {
                    DetachAnimations(VisualTreeHelper.GetChild(root, i));
                }
            }
            catch { }
        }

        private double panelPadDip = DockConfig.PanelPadding;

        private void ComputeMetrics(int n, out double slotW, out double slotH, out double spacing,
            out double iconSize, out double panelH, out double panelW, out int cols, out int rows)
        {
            RECT work = ScreenUtils.GetPrimaryWorkPx();
            double workH = work.Height / dpiScale;
            double workW = work.Width / dpiScale;

            double availContentH = workH - EdgeMargin * 2;
            double maxPanelW = workW - EdgeMargin * 2;

            bool showNames = cfg.NameMode == "always";

            spacing = Math.Round(cfg.IconSpacing);
            if (spacing < 0) { spacing = 0; }

            double k = DockConfig.HoverScale - 1.0;
            double iconPad = cfg.IconPad;

            double scale = 1.0;
            slotW = slotH = iconSize = 0;
            double pad = 0;
            cols = 1; rows = 0; panelH = 0; panelW = 0;

            for (int iter = 0; iter < 5; iter++)
            {
                iconSize = cfg.IconSize * scale;
                double box = iconSize + iconPad * 2 * scale;
                slotW = showNames ? Math.Max(box, iconSize + 28 * scale) : box;
                slotH = box + (showNames ? NameH * scale : 0);

                double padNeed = 0;
                if (k > 0.0001)
                {
                    padNeed = (slotW - 2.0 * HoverFrameInset) * k / 2.0 - HoverFrameInset + DockConfig.HoverSafeDip;
                }
                pad = Math.Max(DockConfig.PanelPadding, Math.Max(0, padNeed));
                double chromeIter = pad * 2 + 2;

                int[] g = DockConfig.GridFor(n, slotW, slotH, spacing, availContentH);
                cols = g[0];
                rows = g[1];

                panelH = chromeIter + rows * Math.Ceiling(slotH) + ((rows > 1) ? (rows - 1) * spacing : 0);
                panelW = chromeIter + cols * Math.Ceiling(slotW) + ((cols > 1) ? (cols - 1) * spacing : 0);

                if (panelW <= maxPanelW || scale <= 0.45) { break; }
                double kw = (maxPanelW - chromeIter) / (panelW - chromeIter);
                if (double.IsNaN(kw) || kw <= 0) { kw = 0.9; }
                scale *= Math.Max(0.45 / scale, Math.Min(0.98, kw));
            }

            panelPadDip = pad;
            double chrome = pad * 2 + 2;

            double maxPanelH = workH - EdgeMargin * 2;
            panelH = Math.Min(panelH, Math.Max(chrome, maxPanelH));
            panelW = Math.Min(panelW, Math.Max(chrome, maxPanelW));
        }

        public void Relayout()
        {
            List<DockItem> items = IsLeft ? cfg.Left : cfg.Right;

            double slotW, slotH, spacing, iconSize, panelH, panelW;
            int cols, rows;
            ComputeMetrics(items.Count, out slotW, out slotH, out spacing, out iconSize,
                           out panelH, out panelW, out cols, out rows);

            dockWidth = panelW;
            dockHeight = Math.Max(panelH, panelPadDip * 2 + 2);

            shadowPadPx = ToPx(ShadowPad);

            Width = Math.Ceiling(dockWidth) + ShadowPad * 2;
            Height = Math.Ceiling(dockHeight) + ShadowPad * 2;

            double radius = Math.Min(12, slotW * 0.24);

            int gap = (int)Math.Round(spacing);
            double cellW = Math.Ceiling(slotW);
            double cellH = Math.Ceiling(slotH);

            for (int i = 0; i < grid.Children.Count; i++)
            {
                Border b = grid.Children[i] as Border;
                if (b == null) { continue; }
                LayoutSlot(b, radius, gap, slotW, cellW, cellH, iconSize, cols);
            }

            Thickness padTh = new Thickness(panelPadDip);
            Thickness noPad = new Thickness(0);
            if (grid.Margin != noPad) { grid.Margin = noPad; }
            if (hoverLayer.Margin != padTh) { hoverLayer.Margin = padTh; }
            if (panel.Padding != padTh) { panel.Padding = padTh; }

            ApplyEdgeMargins();

            panel.CornerRadius = new CornerRadius(DockConfig.PanelRadius);
            rim.CornerRadius = new CornerRadius(DockConfig.PanelRadius);

            ApplyPosition();

            UpdateLensBackdrop(false);
        }

        private double EdgePadDip()
        {
            return EdgePadMin;
        }

        private void LayoutSlot(Border b, double radius, int gap, double slotW,
            double cellW, double cellH, double iconSize, int cols)
        {
            b.Width = cellW;
            b.Height = cellH;
            b.CornerRadius = new CornerRadius(radius);

            int row = boxRow.ContainsKey(b) ? boxRow[b] : Grid.GetRow(b);
            int dcol = boxCol.ContainsKey(b) ? boxCol[b] : Grid.GetColumn(b);
            double gapLeft = (dcol == 0) ? 0 : gap;
            double gapTop = (row == 0) ? 0 : gap;
            b.Margin = new Thickness(gapLeft, gapTop, 0, 0);

            Image img = b.Tag as Image;
            if (img != null)
            {
                img.Width = Math.Round(iconSize);
                img.Height = Math.Round(iconSize);
            }

            Grid content = b.Child as Grid;
            if (content != null)
            {
                Border namePill = FindNamePill(content);
                if (namePill != null) { namePill.MaxWidth = Math.Max(20, slotW - 4); }
            }
            if (b.ContextMenu != null) { b.ContextMenu.Placement = PlacementMode.MousePoint; }
        }

        private static Border FindNamePill(Grid content)
        {
            for (int i = 0; i < content.Children.Count; i++)
            {
                Grid nh = content.Children[i] as Grid;
                if (nh != null && nh.Children.Count > 0)
                {
                    return nh.Children[0] as Border;
                }
            }
            return null;
        }

        private void ApplyPosition()
        {
            RECT mon = ScreenUtils.GetPrimaryMonitorPx();
            RECT work = ScreenUtils.GetPrimaryWorkPx();

            int monL = mon.Left, monR = mon.Right;
            int workT = work.Top, workB = work.Bottom;
            if (workB - workT <= 100) { workT = mon.Top; workB = mon.Bottom; }
            int panelWpx = ToPx(dockWidth);
            int panelTop = workT + ToPx(EdgeMargin);
            int minPanelTop = workB - ToPx(EdgeMargin) - ToPx(dockHeight);
            if (panelTop > minPanelTop) { panelTop = minPanelTop; }
            double e = Ease(animT);

            double peekDip = (cfg != null) ? cfg.PeekWidth : PeekW;
            if (peekDip < 0.0) { peekDip = 0.0; }
            double rimDip = 1.0;
            double shownPanelLeft = IsLeft ? 0.0 : (monR - panelWpx);
            double hiddenPanelLeft = IsLeft ? (-panelWpx - 12.0) : (monR + 12.0);
            double peekPanelLeft = IsLeft
                ? (ToPx(peekDip) - ToPx(rimDip) - panelWpx)
                : (monR - ToPx(peekDip) + ToPx(rimDip));
            double collapsedPanelLeft = coveredHide ? hiddenPanelLeft : peekPanelLeft;
            int panelLeft = (int)Math.Round(collapsedPanelLeft + (shownPanelLeft - collapsedPanelLeft) * e);

            if (animTrace && (Environment.TickCount - lastTraceMs) >= 40)
            {
                lastTraceMs = Environment.TickCount;
                Logger.Write("动画追踪(" + (IsLeft ? "左" : "右") + "): animT=" + animT.ToString("F3")
                             + " e=" + e.ToString("F3") + " 面板左沿=" + panelLeft
                             + " 位移=" + (int)Math.Round((collapsedPanelLeft - panelLeft) / Math.Max(1.0, collapsedPanelLeft - shownPanelLeft) * 100) + "%");
            }

            int winX, winY;
            CalcWindowRect(panelLeft, panelTop, out winX, out winY);

            double leftDip = winX / dpiScale;
            double topDip = winY / dpiScale;
            if (Left != leftDip) { Left = leftDip; }
            if (Top != topDip) { Top = topDip; }

            if (coveredHide) { Opacity = (e <= 0.002) ? 0.0 : (noFade ? 1.0 : e); }
            else { Opacity = noFade ? 1.0 : Math.Max(e, 0.92); }

            bool moved = (winX != lastPlacedWinX) || (winY != lastPlacedWinY);
            if (!moved)
            {
                stableFrames++;
                MeasureOffsetOnce(winX, winY);
                return;
            }
            stableFrames = 0;

            StepLensFrameSync(winX, winY);

            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, winX, winY, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                lastPlacedWinX = winX;
                lastPlacedWinY = winY;
            }
        }

        private void MeasureOffsetOnce(int winX, int winY)
        {
            if (offsetMeasured || hwnd == IntPtr.Zero) { return; }
            if (winX != lastPlacedWinX || winY != lastPlacedWinY) { return; }
            if (stableFrames < 2) { return; }
            offsetMeasured = true;
            try
            {
                RECT wr;
                if (!NativeMethods.GetWindowRect(hwnd, out wr)) { return; }
                int dx = wr.Left - winX;
                int dy = wr.Top - winY;
                Logger.Write("Dock 定位诊断: " + (IsLeft ? "左" : "右") + "面板 窗口矩形与期望位置差 = (" +
                    dx + ", " + dy + ") 物理像素（缩放 " +
                    dpiScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    "，该值仅供参考、不参与定位）");
            }
            catch (Exception ex) { Logger.Write("MeasureOffsetOnce", ex); }
        }

        private double Ease(double t)
        {
            if (t <= 0) { return 0; }
            if (t >= 1) { return 1; }
            string mode = (cfg != null) ? cfg.Easing : "smooth";
            if (mode == "linear") { return t; }
            if (mode == "snap") { double u = 1 - t; return 1 - u * u * u; }
            return t * t * (3 - 2 * t);
        }

        private void CalcWindowRect(double panelLeft, int panelTop,
            out int winX, out int winY)
        {
            int shadowPx = shadowPadPx;
            int cutPx = shadowPadPx + Math.Max(1, (int)Math.Round(EdgePadDip() * dpiScale));

            int panelLeftPx = (int)Math.Round(panelLeft);

            winX = IsLeft ? (panelLeftPx - shadowPx) : (panelLeftPx - cutPx);
            winY = panelTop - shadowPx;
        }

        private int ToPx(double dip)
        {
            double v = dip * dpiScale;
            if (v > 1000000) { v = 1000000; }
            if (v < -1000000) { v = -1000000; }
            return (int)Math.Round(v);
        }

        private void InvalidateWindowOffset()
        {
            offsetMeasured = false;
            stableFrames = 0;
        }

        public bool ContainsCursorDip(double cx, double cy)
        {
            if (animT <= 0.02) { return false; }
            double pad = 6;
            return cx >= Left - pad && cx <= Left + Width + pad
                && cy >= Top - pad && cy <= Top + Height + pad;
        }

        public void UpdateState(bool covered, bool inTrigger, bool inDock, int nowMs)
        {
            if (ItemCount == 0)
            {
                wantShow = false;
                forcedHidden = true;
                return;
            }
            forcedHidden = false;
            coveredHide = covered && !panelPinned;

            if (panelPinned)
            {
                wantShow = true;
                lastInsideMs = nowMs;
                return;
            }

            if (covered)
            {
                wantShow = false;
                lastInsideMs = nowMs - cfg.CollapseDelayMs - 1;
                return;
            }

            if (inTrigger || inDock)
            {
                wantShow = true;
                lastInsideMs = nowMs;
            }
            else if (wantShow && (nowMs - lastInsideMs) >= cfg.CollapseDelayMs)
            {
                wantShow = false;
            }
        }

        public void ForceCollapse()
        {
            wantShow = false;
            lastInsideMs = 0;
        }

        public void ForceHide()
        {
            ForceCollapse();
            coveredHide = true;
            animT = 0;
            ApplyPosition();
        }

        public void ForceShow()
        {
            wantShow = true;
            coveredHide = false;
            lastInsideMs = Clock.NowMs();
            animT = 1;
            ApplyPosition();
        }

        public bool StepAnim(int nowMs)
        {
            SyncLensLive();
            if (lastTickMs == 0) { lastTickMs = nowMs; }
            double dt = nowMs - lastTickMs;
            lastTickMs = nowMs;
            if (dt <= 0) { dt = 16; }
            if (dt > 250) { dt = 250; }
            double target = wantShow ? 1.0 : 0.0;
            if (Math.Abs(animT - target) < 0.0015)
            {
                animSettled = true;
                if (animT != target)
                {
                    animT = target;
                    ApplyPosition();

                    if (target <= 0.001) { UpdateLensBackdrop(false); }
                }
                return false;
            }

            double perMs = (cfg.AnimMs > 0) ? (1.0 / cfg.AnimMs) : 1.0;

            if (animSettled)
            {
                animSettled = false;
                if (dt > 20) { dt = 16; }
            }

            double step = dt * perMs;

            if (animT < target)
            {
                animT += step;
                if (animT > target) { animT = target; }
            }
            else
            {
                animT -= step;
                if (animT < target) { animT = target; }
            }

            ApplyPosition();
            return true;
        }

        public bool IsShowing { get { return wantShow; } }

        public bool IsAnimating
        {
            get
            {
                double target = wantShow ? 1.0 : 0.0;
                return Math.Abs(animT - target) > 0.0015;
            }
        }
        public bool IsForcedHidden { get { return forcedHidden; } }

        public bool PanelPinned
        {
            get { return panelPinned; }
            set { panelPinned = value; }
        }

        private void ApplyLensKnobs(DockConfig config)
        {
            if (config == null) { return; }
            int newPct = (int)Math.Round(Math.Max(0.3, Math.Min(1.0, config.LensScale)) * 100.0);
            int newMs = (int)Math.Round(1000.0 / Math.Max(1.0, Math.Min(240.0, config.AnimFps)));
            if (newMs < 4) { newMs = 4; }

            if (newPct != lensScalePct)
            {
                lensScalePct = newPct;
                if (cfg != null && IsLensStyle(cfg.PanelStyle)) { lensWb = null; lensBrush = null; }
            }
            if (newMs != LensFrameMs)
            {
                LensFrameMs = newMs;
                StopLensLive();
                Logger.Write("光感：帧间隔 -> " + newMs + " ms（目标 " + (int)Math.Round(1000.0 / newMs)
                             + " fps，跟随动画刷新率 " + config.AnimFps + " Hz）；内部渲染 " + newPct + "%");
            }
        }

        public void ApplyConfig(DockConfig config)
        {
            cfg = config;
            ApplyLensKnobs(config);
            RebuildItems();
            Relayout();

            if (IsLensStyle(cfg.PanelStyle)) { UpdateLensBackdrop(true); }
        }

        private void CloseIdleMenus()
        {
            try
            {
                if (lastDropMs != 0 && (Clock.NowMs() - lastDropMs) < 220)
                {
                    return;
                }

                ContextMenu open = null;
                if (panelMenu != null && panelMenu.IsOpen) { open = panelMenu; }
                if (open == null)
                {
                    for (int i = 0; i < grid.Children.Count; i++)
                    {
                        Border b = grid.Children[i] as Border;
                        if (b == null || b.ContextMenu == null) { continue; }
                        if (b.ContextMenu.IsOpen) { open = b.ContextMenu; break; }
                    }
                }
                if (open == null) { return; }

                POINT cur;
                if (!NativeMethods.GetCursorPos(out cur)) { return; }

                Rect mr = MenuScreenRectPx(open);
                bool inMenu;
                if (mr.IsEmpty) { inMenu = true; }
                else { Rect grow = mr; grow.Inflate(10, 10); inMenu = grow.Contains(cur.X, cur.Y); }

                bool inPanel = PanelScreenRectPx().Contains(cur.X, cur.Y);

                if (!inMenu && !inPanel)
                {
                    open.IsOpen = false;
                    Logger.Write("右键菜单自动关闭（鼠标离开 Dock 与菜单）");
                }
            }
            catch (Exception ex) { Logger.Write("CloseIdleMenus", ex); }
        }

        public void PollMenuAutoClose()
        {
            CloseIdleMenus();
        }

        private Rect PanelScreenRectPx()
        {
            try
            {
                Point tl = panel.PointToScreen(new Point(0, 0));
                double w = panel.ActualWidth * dpiScale;
                double h = panel.ActualHeight * dpiScale;
                if (w <= 0 || h <= 0) { w = Width * dpiScale; h = Height * dpiScale; }
                return new Rect(tl.X, tl.Y, w, h);
            }
            catch
            {
                return Rect.Empty;
            }
        }

        private static Rect MenuScreenRectPx(ContextMenu cm)
        {
            try
            {
                Point tl = cm.PointToScreen(new Point(0, 0));
                DpiScale d = VisualTreeHelper.GetDpi(cm);
                double w = cm.ActualWidth * d.DpiScaleX;
                double h = cm.ActualHeight * d.DpiScaleY;
                if (w <= 0 || h <= 0) { return Rect.Empty; }
                return new Rect(tl.X, tl.Y, w, h);
            }
            catch
            {
                return Rect.Empty;
            }
        }
    }
}
