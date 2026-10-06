using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SideStack
{
    public class SettingsWindow : Window
    {
        private readonly DockConfig cfg;
        private readonly string exePath;
        private readonly Action<DockConfig> onApply;
        private readonly Func<bool> getPinned;
        private readonly Action togglePin;
        private readonly Action reloadCfg;
        private readonly Action openFolder;
        private readonly Action quitApp;

        private readonly List<Action> refreshers = new List<Action>();

        private Border chipHover, chipBorder;
        private bool syncing;
        private int maxFps = 120;

        public SettingsWindow(DockConfig config, string exe, Action<DockConfig> onApply,
                              Func<bool> getPinned, Action togglePin, Action reload,
                              Action openFolder, Action quit)
        {
            cfg = config;
            exePath = exe;
            this.onApply = onApply;
            this.getPinned = getPinned;
            this.togglePin = togglePin;
            reloadCfg = reload;
            this.openFolder = openFolder;
            quitApp = quit;

            try { maxFps = DisplayInfo.GetMaxRefreshRate(); }
            catch { maxFps = 120; }
            if (maxFps < 30) { maxFps = 30; }

            Title = "SideStack 设置";
            try { Icon = IconLoader.AppIcon(); } catch { }
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 1000;
            Height = 720;
            MinWidth = 900;
            MinHeight = 600;
            FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            BuildUi();
            LoadValues();

            Loaded += delegate
            {
                try
                {
                    double mw = SystemParameters.WorkArea.Width - 30;
                    double mh = SystemParameters.WorkArea.Height - 30;
                    if (Width > mw) { Width = mw; }
                    if (Height > mh) { Height = mh; }
                    Theme.PlayEntry(this, Content as FrameworkElement);
                }
                catch { }
            };
        }

        private void Change(Action mutate)
        {
            if (mutate == null) { return; }
            try
            {
                mutate();
                SchedulePush();
            }
            catch (Exception ex) { Logger.Write("SettingsWindow.Change", ex); }
        }

        private System.Windows.Threading.DispatcherTimer pushTimer;
        private int lastPushMs;

        private void SchedulePush()
        {
            int now = Clock.NowMs();
            bool continuous = (now - lastPushMs) < 90;
            lastPushMs = now;

            if (!continuous)
            {
                PushConfig();
                return;
            }

            if (pushTimer == null)
            {
                pushTimer = new System.Windows.Threading.DispatcherTimer();
                pushTimer.Interval = TimeSpan.FromMilliseconds(90);
                pushTimer.Tick += delegate
                {
                    pushTimer.Stop();
                    PushConfig();
                };
            }
            pushTimer.Stop();
            pushTimer.Start();
        }

        private void PushConfig()
        {
            if (onApply == null) { return; }
            try { onApply(cfg.Clone()); }
            catch (Exception ex) { Logger.Write("SettingsWindow.PushConfig", ex); }
        }

        private void BuildUi()
        {
            Grid body = new Grid();
            body.Margin = new Thickness(Theme.PadWindow, 20, Theme.PadWindow, 22);
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            FrameworkElement title = Theme.TitleBar(this, "SideStack 设置",
                "", new Action(Close));
            Grid.SetRow(title, 0);
            body.Children.Add(title);

            FrameworkElement scroll = BuildScroll();
            Grid.SetRow(scroll, 1);
            body.Children.Add(scroll);

            Grid shell = Theme.LiquidGlassShell(Theme.RadiusWindow, body, cfg.SettingsOpacity);
            glassHost = shell;

            Grid root = new Grid();
            root.Margin = new Thickness(18, 16, 18, 18);
            root.Children.Add(shell);

            Grid outer = new Grid();
            outer.Background = Brushes.Transparent;
            outer.Children.Add(root);

            outer.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) { return; }
                if (e.ButtonState != MouseButtonState.Pressed) { return; }
                Point p = e.GetPosition(this);
                if (p.X <= DragBand || p.Y <= DragBandTop ||
                    p.X >= ActualWidth - DragBand || p.Y >= ActualHeight - DragBand)
                {
                    try { DragMove(); } catch { }
                }
            };
            Content = outer;
        }

        private ToggleSwitch cbPinned, cbAutoStart, cbDrag, cbHideIcons, cbStartupPriority;
        private Grid glassHost;

        private const double DragBand = 40;

        private const double DragBandTop = 80;

        private FrameworkElement BuildScroll()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            FrameworkElement appearance = Card("外观与图标", BuildAppearance());
            Grid.SetRow(appearance, 0);
            Grid.SetColumn(appearance, 0);
            grid.Children.Add(appearance);

            FrameworkElement behavior = Card("行为与动画", BuildBehavior());
            Grid.SetRow(behavior, 0);
            Grid.SetColumn(behavior, 2);
            grid.Children.Add(behavior);

            FrameworkElement tools = Card("Dock 内容与工具", BuildTools());
            Grid.SetRow(tools, 1);
            Grid.SetColumn(tools, 0);
            Grid.SetColumnSpan(tools, 3);
            grid.Children.Add(tools);

            ScrollViewer sv = new ScrollViewer();

            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.Padding = new Thickness(0, 0, 10, 0);
            sv.Content = grid;

            sv.Loaded += delegate
            {
                try
                {
                    Theme.MakeInvisibleScrollBar(FindScrollBar(sv, Orientation.Vertical));
                }
                catch (Exception ex) { Logger.Write("SettingsWindow 滚动条", ex); }
            };
            return sv;
        }

        private static System.Windows.Controls.Primitives.ScrollBar FindScrollBar(DependencyObject root, Orientation o)
        {
            if (root == null) { return null; }
            System.Windows.Controls.Primitives.ScrollBar sb =
                root as System.Windows.Controls.Primitives.ScrollBar;
            if (sb != null && sb.Orientation == o) { return sb; }

            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                System.Windows.Controls.Primitives.ScrollBar r = FindScrollBar(VisualTreeHelper.GetChild(root, i), o);
                if (r != null) { return r; }
            }
            return null;
        }

        private Grid Card(string title, UIElement content)
        {
            Grid host = new Grid();
            host.Margin = new Thickness(0, 0, 0, 14);
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            FrameworkElement st = Theme.SectionTitle(title);
            Grid.SetRow(st, 0);
            host.Children.Add(st);

            Border card = Theme.GlassCard(Theme.RadiusCard);
            card.VerticalAlignment = VerticalAlignment.Stretch;
            card.Padding = new Thickness(18, 14, 18, 16);
            card.Child = content;
            Grid.SetRow(card, 1);
            host.Children.Add(card);

            return host;
        }

        private static FrameworkElement GroupCaption(string text)
        {
            TextBlock t = Theme.Text(text, 11, Theme.Accent, true);
            t.Margin = new Thickness(0, 14, 0, 2);
            t.Tag = text;
            return t;
        }

        private FrameworkElement BuildAppearance()
        {
            StackPanel sp = new StackPanel();

            sp.Children.Add(SegField("面板风格",
                new string[] { "经典", "透亮", "光感", "光感·清" },
                new string[] { "glass", "clear", "lens", "lensstrong" },
                delegate { return cfg.PanelStyle; },
                delegate (string v) { cfg.PanelStyle = v; }));

            sp.Children.Add(NumField("光感清晰度", "%", 50, 100, 0,
                delegate { return Math.Round(cfg.LensScale * 100.0); },
                delegate (double v) { cfg.LensScale = v / 100.0; }));

            sp.Children.Add(NumField("本窗口不透明度", "%", 35, 100, 0,
                delegate { return Math.Round(cfg.SettingsOpacity * 100.0); },
                delegate (double v)
                {
                    cfg.SettingsOpacity = v / 100.0;
                    Theme.SetShellOpacity(glassHost, cfg.SettingsOpacity);
                }));

            sp.Children.Add(SegField("名称显示",
                new string[] { "关闭", "悬停", "常显" },
                new string[] { "off", "hover", "always" },
                delegate { return cfg.NameMode; },
                delegate (string v) { cfg.NameMode = v; }));

            sp.Children.Add(GroupCaption("图标"));
            sp.Children.Add(NumField("图标大小", "px", 24, 96, 0,
                delegate { return cfg.IconSize; },
                delegate (double v) { cfg.IconSize = v; }));

            sp.Children.Add(NumField("图标间距", "px", 0, 40, 0,
                delegate { return cfg.IconSpacing; },
                delegate (double v) { cfg.IconSpacing = v; }));

            sp.Children.Add(NumField("图标内边距", "px", 0, 20, 0,
                delegate { return cfg.IconPad; },
                delegate (double v) { cfg.IconPad = v; }));

            sp.Children.Add(NumField("图标圆角", "%", 0, 50, 0,
                delegate { return cfg.IconRadiusRatio * 100.0; },
                delegate (double v) { cfg.IconRadiusRatio = v / 100.0; }));

            chipHover = MakeColorChip(delegate { return cfg.HoverColor; },
                                      delegate (string hex) { cfg.HoverColor = hex; });
            sp.Children.Add(GroupCaption("悬停高亮"));
            sp.Children.Add(ColorField("悬停底色", chipHover));

            sp.Children.Add(NumField("悬停不透明度", "%", 0, 100, 0,
                delegate { return cfg.HoverOpacity * 100.0; },
                delegate (double v) { cfg.HoverOpacity = v / 100.0; }));

            chipBorder = MakeColorChip(delegate { return cfg.HoverBorderColor; },
                                       delegate (string hex) { cfg.HoverBorderColor = hex; });
            sp.Children.Add(ColorField("悬停描边色", chipBorder));

            sp.Children.Add(NumField("描边粗细", "px", 0, 6, 1,
                delegate { return cfg.HoverBorderWidth; },
                delegate (double v) { cfg.HoverBorderWidth = v; }));

            return sp;
        }

        private FrameworkElement BuildBehavior()
        {
            StackPanel sp = new StackPanel();

            sp.Children.Add(NumField("边缘触发带宽", "px", 1, 24, 0,
                delegate { return cfg.TriggerWidth; },
                delegate (double v) { cfg.TriggerWidth = v; }));

            sp.Children.Add(NumField("贴边露出", "px", 0, 16, 0,
                delegate { return cfg.PeekWidth; },
                delegate (double v) { cfg.PeekWidth = v; }));

            sp.Children.Add(NumField("展开/收起动画", "ms", 40, 800, 0,
                delegate { return (double)cfg.AnimMs; },
                delegate (double v) { cfg.AnimMs = (int)Math.Round(v); }));

            sp.Children.Add(NumField("鼠标离开后收回", "ms", 0, 2000, 0,
                delegate { return (double)cfg.CollapseDelayMs; },
                delegate (double v) { cfg.CollapseDelayMs = (int)Math.Round(v); }));

            sp.Children.Add(NumField("动画刷新率", "Hz", 30, maxFps, 0,
                delegate { return (double)cfg.AnimFps; },
                delegate (double v) { cfg.AnimFps = (int)Math.Round(v); }));

            sp.Children.Add(SegField("动画缓动",
                new string[] { "平滑", "线性", "干脆" },
                new string[] { "smooth", "linear", "snap" },
                delegate { return cfg.Easing; },
                delegate (string v) { cfg.Easing = v; }));

            cbPinned = MakeToggle(new Action(PinChanged));
            sp.Children.Add(CheckRow(cbPinned, "面板常显"));

            cbAutoStart = MakeToggle(delegate { ApplyAutoStart(); });
            sp.Children.Add(CheckRow(cbAutoStart, "开机自动启动"));

            cbStartupPriority = MakeToggle(delegate { ApplyStartupPriority(); });
            sp.Children.Add(CheckRow(cbStartupPriority, "启动优先级",
                "开启后用「任务计划程序」在登录时启动，并把进程优先级设为「高于正常」（5）；" +
                "关掉只把优先级恢复为「正常」（4）。若自启还没由计划任务接管，开启它会顺便迁移过来。" +
                "实测本机：SideStack 比 explorer 早约 0.2 秒启动，TranslucentTB 晚约 7.5 秒。"));

            cbDrag = MakeToggle(delegate
            {
                Change(delegate { cfg.DragReorder = cbDrag.IsChecked == true; });
            });
            sp.Children.Add(CheckRow(cbDrag, "允许拖动图标互换位置"));

            cbHideIcons = MakeToggle(delegate
            {
                Change(delegate { cfg.HideDesktopIcons = cbHideIcons.IsChecked == true; });
            });
            sp.Children.Add(CheckRow(cbHideIcons, "隐藏桌面图标"));

            return sp;
        }

        private FrameworkElement BuildTools()
        {
            StackPanel sp = new StackPanel();

            Grid btns = ButtonGrid(5,
                Theme.PillButton("恢复默认", false, new Action(DoResetDefaults)),
                Theme.PillButton("管理应用列表", false, new Action(OpenAppList)),
                Theme.PillButton("重新加载配置", false, new Action(DoReload)),
                Theme.PillButton("打开配置文件夹", false, new Action(DoOpenFolder)),
                Theme.PillButton("退出 SideStack", false, new Action(DoQuit)));
            sp.Children.Add(btns);

            return sp;
        }

        private void DoResetDefaults()
        {
            try
            {
                DockConfig d = new DockConfig();
                if (!Dialog.Confirm("恢复默认设置",
                        "将把以下参数恢复为出厂默认值：\r\n\r\n" +
                        "  · 名称显示\r\n" +
                        "  · 图标大小 " + d.IconSize + " / 间距 " + d.IconSpacing + " / 内边距 " + d.IconPad +
                        " / 圆角 " + (d.IconRadiusRatio * 100) + "%\r\n" +
                        "  · 悬停底色 " + d.HoverColor + " / 不透明度 " + (d.HoverOpacity * 100) + "% / 描边 " + d.HoverBorderWidth + "px\r\n" +
                        "  · 触发带宽 " + d.TriggerWidth + " / 动画 " + d.AnimMs + "ms / 收回延迟 " + d.CollapseDelayMs + "ms / 刷新率 " + d.AnimFps + "Hz\r\n" +
                        "  · 允许拖动图标互换位置\r\n\r\n" +
                        "开机自启与隐藏桌面图标也不会被改动。\r\n" +
                        "确定要恢复吗？"))
                {
                    return;
                }

                cfg.ResetToDefaults();
                if (cfg.AnimFps > maxFps) { cfg.AnimFps = maxFps; }
                if (cfg.AnimFps < 30) { cfg.AnimFps = 30; }
                PushConfig();

                bool old = syncing;
                syncing = true;
                try
                {
                    if (cbDrag != null) { cbDrag.SetCheckedSilently(cfg.DragReorder); }
                    if (cbPinned != null) { cbPinned.SetCheckedSilently((getPinned != null) && getPinned()); }
                    if (cbAutoStart != null) { cbAutoStart.SetCheckedSilently(ShellUtils.IsAutoStartOn() || StartupTask.Exists()); }
                    if (cbHideIcons != null) { cbHideIcons.SetCheckedSilently(cfg.HideDesktopIcons); }
                    if (cbStartupPriority != null) { cbStartupPriority.SetCheckedSilently(StartupTask.IsPriorityBoostWanted()); }

                    for (int i = 0; i < refreshers.Count; i++)
                    {
                        try { refreshers[i](); } catch { }
                    }
                    RefreshChips();
                }
                finally { syncing = old; }

                Logger.Write("设置已恢复默认值");
            }
            catch (Exception ex)
            {
                Logger.Write("SettingsWindow.DoResetDefaults", ex);
            }
        }

        private const double LabelW = 104;

        private static Grid FieldRow(string label, FrameworkElement control)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 4, 0, 4);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelW) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lab = Theme.Text(label, 12, Theme.Ink, true);
            lab.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(lab, 0);
            g.Children.Add(lab);

            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        private FrameworkElement SegField(string label, string[] labels, string[] values,
                                          Func<string> get, Action<string> set)
        {
            StackPanel h = new StackPanel();
            h.Orientation = Orientation.Horizontal;
            List<Border> chips = new List<Border>();

            for (int i = 0; i < labels.Length && i < values.Length; i++)
            {
                string v = values[i];
                Border c = new Border();
                c.CornerRadius = new CornerRadius(9);
                c.Padding = new Thickness(14, 5, 14, 5);
                c.Margin = new Thickness(0, 0, 6, 0);
                c.Cursor = Cursors.Hand;
                c.SnapsToDevicePixels = true;
                c.Child = Theme.Text(labels[i], 12, Theme.Ink, false);
                c.MouseLeftButtonUp += delegate
                {
                    set(v);
                    SyncSeg(chips, values, get());
                    SchedulePush();
                };
                chips.Add(c);
                h.Children.Add(c);
            }

            Action refresh = delegate { SyncSeg(chips, values, get()); };
            refreshers.Add(refresh);
            return FieldRow(label, h);
        }

        private static void SyncSeg(List<Border> chips, string[] values, string current)
        {
            for (int i = 0; i < chips.Count && i < values.Length; i++)
            {
                bool on = string.Equals(values[i], current, StringComparison.OrdinalIgnoreCase);
                Border c = chips[i];
                c.Background = new SolidColorBrush(on ? Theme.Accent : Color.FromArgb(120, 255, 255, 255));
                c.BorderThickness = new Thickness(1);
                c.BorderBrush = new SolidColorBrush(on ? Theme.Accent : Color.FromArgb(130, 0xB8, 0xC4, 0xD8));
                TextBlock t = c.Child as TextBlock;
                if (t != null) { t.Foreground = new SolidColorBrush(on ? Colors.White : Theme.Ink); }
            }
        }

        private static Grid CheckRow(ToggleSwitch ts, string label)
        {
            return CheckRow(ts, label, null);
        }

        /// <summary>开关行；hint 非空时在开关下面再加一行小字说明（缩进与标签列对齐）</summary>
        private static Grid CheckRow(ToggleSwitch ts, string label, string hint)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 4, 0, 4);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelW) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel h = new StackPanel();
            h.Orientation = Orientation.Horizontal;
            h.VerticalAlignment = VerticalAlignment.Center;
            TextBlock t = Theme.Text(label, 12.5, Theme.Ink, false);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, 0, 10, 0);
            h.Children.Add(t);
            h.Children.Add(ts);

            if (string.IsNullOrEmpty(hint))
            {
                Grid.SetColumn(h, 1);
                g.Children.Add(h);
                return g;
            }

            StackPanel col = new StackPanel();
            col.VerticalAlignment = VerticalAlignment.Center;
            col.Children.Add(h);
            TextBlock tip = Theme.Hint(hint);
            tip.Margin = new Thickness(0, 3, 0, 0);
            col.Children.Add(tip);

            Grid.SetColumn(col, 1);
            g.Children.Add(col);
            return g;
        }

        /// <summary>独立的小字说明（与开关行的提示同款样式）</summary>
        private static TextBlock Hint(string s)
        {
            TextBlock t = Theme.Hint(s);
            t.Margin = new Thickness(LabelW, 2, 0, 2);
            return t;
        }


        private static Grid ButtonGrid(int count, params FrameworkElement[] buttons)
        {
            Grid g = new Grid();
            for (int i = 0; i < count; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            for (int i = 0; i < buttons.Length && i < count; i++)
            {
                buttons[i].Margin = new Thickness(i == 0 ? 0 : 6, 0, i == count - 1 ? 0 : 6, 0);
                buttons[i].VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(buttons[i], i);
                g.Children.Add(buttons[i]);
            }
            return g;
        }

        private FrameworkElement NumField(string label, string unit, double min, double max,
                                          int decimals, Func<double> get, Action<double> set)
        {
            Slider sl = new Slider();
            sl.Minimum = min;
            sl.Maximum = max;
            sl.IsSnapToTickEnabled = false;
            sl.SmallChange = (max - min) / 100.0;
            sl.LargeChange = (max - min) / 10.0;
            sl.Height = 24;
            sl.VerticalAlignment = VerticalAlignment.Center;

            TextBox tb = NumBox(68);
            tb.ToolTip = "输入后回车";
            tb.Margin = new Thickness(12, 0, 0, 0);
            tb.VerticalAlignment = VerticalAlignment.Center;

            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(sl, 0);
            row.Children.Add(sl);
            Grid.SetColumn(tb, 1);
            row.Children.Add(tb);

            FrameworkElement cell = FieldRow(label, row);

            System.Windows.Threading.DispatcherTimer settle = null;
            Action applyNow = delegate
            {
                if (settle != null) { settle.Stop(); }
                double v = Clamp(sl.Value, min, max);
                bool old = syncing;
                syncing = true;
                try { set(v); }
                catch (Exception ex) { Logger.Write("SettingsWindow.NumField", ex); }
                finally { syncing = old; }
                SchedulePush();
            };
            Action armSettle = delegate
            {
                if (settle == null)
                {
                    settle = new System.Windows.Threading.DispatcherTimer();
                    settle.Interval = TimeSpan.FromMilliseconds(260);
                    settle.Tick += delegate { settle.Stop(); applyNow(); };
                }
                settle.Stop();
                settle.Start();
            };

            Action push = delegate
            {
                bool old = syncing;
                syncing = true;
                try
                {
                    double v = Clamp(get(), min, max);
                    sl.Value = v;
                    tb.Text = Fmt(v, unit, decimals);
                }
                catch { }
                finally { syncing = old; }
            };

            sl.ValueChanged += delegate
            {
                if (syncing) { return; }

                tb.Text = Fmt(sl.Value, unit, decimals);
                armSettle();
            };

            sl.PreviewMouseLeftButtonUp += delegate { applyNow(); };
            sl.LostMouseCapture += delegate { applyNow(); };

            tb.LostFocus += delegate
            {
                Commit(tb, sl, unit, decimals);
                applyNow();
            };
            tb.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    Commit(tb, sl, unit, decimals);
                    applyNow();
                }
            };

            refreshers.Add(push);
            push();

            sl.Loaded += delegate { Theme.TintSlider(sl); };
            return cell;
        }

        private static void Commit(TextBox tb, Slider sl, string unit, int decimals)
        {
            double v = ParseNum(tb.Text, sl.Value);
            if (v < sl.Minimum) { v = sl.Minimum; }
            if (v > sl.Maximum) { v = sl.Maximum; }
            sl.Value = v;
            tb.Text = Fmt(v, unit, decimals);
        }

        private static string Fmt(double v, string unit, int decimals)
        {
            string s = v.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture);
            if (unit == null || unit.Length == 0) { return s; }
            if (unit == "%") { return s + "%"; }
            return s + " " + unit;
        }

        private static double ParseNum(string text, double fallback)
        {
            if (text == null) { return fallback; }
            string t = "";
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsDigit(c)) { t += c; }
                else if (c == '.' || c == ',') { t += '.'; }
            }
            double v;
            if (t.Length > 0 && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return v;
            }
            return fallback;
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) { return min; }
            if (v > max) { return max; }
            return v;
        }

        private static TextBox NumBox(double w)
        {
            TextBox tb = new TextBox();
            tb.Width = w;
            tb.Height = 26;
            tb.FontSize = 12.5;
            tb.Padding = new Thickness(7, 2, 7, 2);
            tb.VerticalContentAlignment = VerticalAlignment.Center;
            tb.Background = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255));
            tb.BorderBrush = new SolidColorBrush(Color.FromArgb(130, 0xB8, 0xC4, 0xD8));
            tb.BorderThickness = new Thickness(1);
            tb.Foreground = new SolidColorBrush(Theme.Ink);
            return tb;
        }

        private Border MakeColorChip(Func<string> get, Action<string> set)
        {
            Border chip = Theme.ColorChip(null);
            chip.ToolTip = "点击打开选色界面";
            chip.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                try
                {
                    Color cur = Ui.ParseColor(get(), Colors.White);
                    Color? pick = ColorPickerWindow.Pick(this, "选择颜色", Ui.ToHex(cur));
                    if (pick.HasValue)
                    {
                        set(Ui.ToHex(pick.Value));
                        chip.Background = new SolidColorBrush(pick.Value);
                        PushConfig();
                    }
                }
                catch (Exception ex) { Logger.Write("SettingsWindow.PickColor", ex); }
            };
            return chip;
        }

        private FrameworkElement ColorField(string label, Border chip)
        {
            StackPanel h = new StackPanel();
            h.Orientation = Orientation.Horizontal;
            h.VerticalAlignment = VerticalAlignment.Center;

            chip.VerticalAlignment = VerticalAlignment.Center;
            h.Children.Add(chip);

            TextBlock tip = Theme.Text("点击选色", 10.5, Theme.InkSoft, false);
            tip.VerticalAlignment = VerticalAlignment.Center;
            tip.Margin = new Thickness(8, 0, 0, 0);
            h.Children.Add(tip);

            return FieldRow(label, h);
        }

        private ToggleSwitch MakeToggle(Action onChanged)
        {
            ToggleSwitch ts = new ToggleSwitch();
            ts.Checked += delegate { if (!syncing && onChanged != null) { onChanged(); } };
            ts.Unchecked += delegate { if (!syncing && onChanged != null) { onChanged(); } };
            return ts;
        }

        private void LoadValues()
        {
            bool old = syncing;
            syncing = true;
            try
            {
                if (cfg.AnimFps > maxFps) { cfg.AnimFps = maxFps; }

                if (cbAutoStart != null) { cbAutoStart.SetCheckedSilently(ShellUtils.IsAutoStartOn() || StartupTask.Exists()); }
                if (cbDrag != null) { cbDrag.SetCheckedSilently(cfg.DragReorder); }
                if (cbHideIcons != null) { cbHideIcons.SetCheckedSilently(cfg.HideDesktopIcons); }
                if (cbPinned != null) { cbPinned.SetCheckedSilently((getPinned != null) && getPinned()); }
            }
            catch (Exception ex) { Logger.Write("SettingsWindow.LoadValues", ex); }
            finally { syncing = old; }

            for (int i = 0; i < refreshers.Count; i++)
            {
                try { refreshers[i](); }
                catch { }
            }
            RefreshChips();
        }

        private void RefreshChips()
        {
            if (chipHover != null) { chipHover.Background = new SolidColorBrush(Ui.ParseColor(cfg.HoverColor, Colors.White)); }
            if (chipBorder != null) { chipBorder.Background = new SolidColorBrush(Ui.ParseColor(cfg.HoverBorderColor, Colors.White)); }
        }

        private void PinChanged()
        {
            try { if (togglePin != null) { togglePin(); } }
            catch (Exception ex) { Logger.Write("SettingsWindow.PinChanged", ex); }
        }

        private void ApplyAutoStart()
        {
            try
            {
                bool want = cbAutoStart != null && cbAutoStart.IsChecked == true;
                bool taskExists = StartupTask.Exists();
                Logger.Write("开机自启 -> " + (want ? "开" : "关") + "（计划任务存在 " + taskExists + "）");

                // 登录自启有两条通道：Run 项与计划任务。两条同时存在会启动两份，所以始终只留一条：
                //   打开 -> 已有计划任务就保持它（不再补写 Run 项）；否则写 Run 项
                //   关闭 -> 删掉计划任务 + 删掉 Run 项（任务引用的就是当前 exe，删掉即失效）
                if (want)
                {
                    if (!taskExists)
                    {
                        if (!ShellUtils.SetAutoStart(true, exePath))
                        {
                            Logger.Write("开机自启写入失败（可能被安全软件拦截）");
                        }
                    }
                }
                else
                {
                    if (taskExists) { StartupTask.Delete(exePath); }
                    ShellUtils.SetAutoStart(false, exePath);
                }

                Change(delegate { cfg.AutoStart = want; });

                // 回填开关的真实状态：写盘失败或被拦截时，不让界面显示成"已生效"
                if (cbAutoStart != null)
                {
                    cbAutoStart.SetCheckedSilently(ShellUtils.IsAutoStartOn() || StartupTask.Exists());
                }
                if (cbStartupPriority != null)
                {
                    cbStartupPriority.SetCheckedSilently(StartupTask.IsPriorityBoostWanted());
                }
            }
            catch (Exception ex) { Logger.Write("SettingsWindow.ApplyAutoStart", ex); }
        }

        /// <summary>
        /// 「启动优先级」开关。
        ///
        /// 背景（本机实测）：计划任务 XML 里的 &lt;Priority&gt; 并不可靠 —— Priority=4/5/6
        /// 起出来的进程都是 Normal(8)，7 反而变成 BelowNormal(6)。所以真正提升优先级这件事
        /// 由程序启动时自己做（StartupTask.BoostProcess），任务里的值只作为"用户要不要"的意图记录。
        /// 切换开关时立即作用到当前进程，不用等下次登录。
        /// 若登录自启还挂在 Run 项上，会顺便迁移成计划任务版（避免两条通道并存）。
        /// </summary>
        private void ApplyStartupPriority()
        {
            try
            {
                bool want = cbStartupPriority != null && cbStartupPriority.IsChecked == true;
                Logger.Write("启动优先级：用户切换到 " + (want ? "要提升" : "不提升"));

                if (!StartupTask.Exists())
                {
                    Logger.Write("启动优先级：当前为 Run 项自启，切换为计划任务版");
                    if (!StartupTask.Create(want, exePath))
                    {
                        Logger.Write("启动优先级：创建计划任务失败，保持原自启方式不变");
                        if (cbStartupPriority != null) { cbStartupPriority.SetCheckedSilently(StartupTask.IsPriorityBoostWanted()); }
                        return;
                    }
                    ShellUtils.SetAutoStart(false, exePath);
                    Change(delegate { cfg.AutoStart = true; });
                }
                else
                {
                    StartupTask.SetPriorityBoostIntent(want);
                }

                // 立即作用到当前进程（关掉时是"下次启动不再提升"，本次运行不强行降回去）
                if (want) { StartupTask.BoostProcess(); }

                if (cbStartupPriority != null)
                {
                    cbStartupPriority.SetCheckedSilently(StartupTask.IsPriorityBoostWanted());
                }
                if (cbAutoStart != null) { cbAutoStart.SetCheckedSilently(ShellUtils.IsAutoStartOn() || StartupTask.Exists()); }
            }
            catch (Exception ex) { Logger.Write("SettingsWindow.ApplyStartupPriority", ex); }
        }

        private void OpenAppList()
        {
            try
            {
                AppListWindow w = new AppListWindow(cfg, new Action(delegate
                {
                    PushConfig();
                }));
                w.Owner = this;
                w.ShowDialog();
                RefreshAfterExternalChange();
            }
            catch (Exception ex)
            {
                Logger.Write("SettingsWindow.OpenAppList", ex);
            }
        }

        private void DoReload()
        {
            try
            {
                bool old = syncing;
                syncing = true;
                try
                {
                    DockConfig fresh = ConfigStore.Load();
                    if (fresh != null) { CopyInto(cfg, fresh); }
                }
                finally { syncing = old; }

                if (reloadCfg != null) { reloadCfg(); }
                RefreshAfterExternalChange();
            }
            catch (Exception ex) { Logger.Write("SettingsWindow.DoReload", ex); }
        }

        private void RefreshAfterExternalChange()
        {
            bool old = syncing;
            syncing = true;
            try { LoadValues(); }
            finally { syncing = old; }
        }

        private static void CopyInto(DockConfig dst, DockConfig src)
        {
            dst.IconSize = src.IconSize;
            dst.IconSpacing = src.IconSpacing;
            dst.IconPad = src.IconPad;
            dst.PanelStyle = src.PanelStyle;
            dst.NameMode = src.NameMode;
            dst.HoverColor = src.HoverColor;
            dst.HoverOpacity = src.HoverOpacity;
            dst.HoverBorderColor = src.HoverBorderColor;
            dst.HoverBorderWidth = src.HoverBorderWidth;
            dst.TriggerWidth = src.TriggerWidth;
            dst.AnimMs = src.AnimMs;
            dst.CollapseDelayMs = src.CollapseDelayMs;
            dst.AutoStart = src.AutoStart;
            dst.ShowToolTip = src.ShowToolTip;
            dst.AnimFps = src.AnimFps;
            dst.DragReorder = src.DragReorder;
            dst.HideDesktopIcons = src.HideDesktopIcons;

            dst.Ignored.Clear();
            for (int i = 0; i < src.Ignored.Count; i++) { dst.Ignored.Add(src.Ignored[i]); }

            dst.Left.Clear();
            for (int i = 0; i < src.Left.Count; i++) { dst.Left.Add(src.Left[i]); }
            dst.Right.Clear();
            for (int i = 0; i < src.Right.Count; i++) { dst.Right.Add(src.Right[i]); }
        }

        private void DoOpenFolder()
        {
            try { if (openFolder != null) { openFolder(); } }
            catch (Exception ex) { Logger.Write("SettingsWindow.DoOpenFolder", ex); }
        }

        private void DoQuit()
        {
            try
            {
                if (!Dialog.Confirm("退出 SideStack", "确定退出吗？"))
                {
                    return;
                }
            }
            catch { }
            try { if (quitApp != null) { quitApp(); } }
            catch (Exception ex) { Logger.Write("SettingsWindow.DoQuit", ex); }
        }
    }
}
