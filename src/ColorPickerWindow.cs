using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SideStack
{
    public class ColorPickerWindow : Window
    {
        public Color? Result;

        private static readonly string[] Palette = new string[]
        {
            "#FFFFFF", "#F8FAFC", "#F1F5F9", "#E2E8F0", "#CBD5E1", "#94A3B8",
            "#64748B", "#475569", "#334155", "#1E293B", "#0F172A", "#000000",
            "#FEF2F2", "#FECACA", "#F87171", "#EF4444", "#DC2626", "#B91C1C",
            "#FFF7ED", "#FDBA74", "#FB923C", "#F97316", "#EA580C",
            "#FEF3C7", "#FCD34D", "#F59E0B", "#D97706",
            "#ECFDF5", "#A7F3D0", "#34D399", "#10B981", "#059669", "#15803D",
            "#DCFCE7", "#86EFAC", "#22C55E",
            "#ECFEFF", "#67E8F9", "#06B6D4", "#0891B2",
            "#E0F2FE", "#7DD3FC", "#38BDF8", "#0EA5E9", "#2563EB", "#1D4ED8",
            "#EDE9FE", "#C4B5FD", "#8B5CF6", "#6D28D9",
            "#FDF2F8", "#F9A8D4", "#EC4899", "#BE185D", "#F43F5E"
        };

        private int r, g, b;
        private Color original;
        private Border currentChip, originalChip;
        private Slider sr, sg, sb;
        private TextBlock vr, vg, vb;
        private readonly List<Border> cells = new List<Border>();
        private bool sync;

        public static Color? Pick(Window owner, string title, string hex)
        {
            Color start = Ui.ParseColor(hex, Colors.White);
            ColorPickerWindow w = new ColorPickerWindow(title, start);
            if (owner != null && !object.ReferenceEquals(owner, w))
            {
                try { w.Owner = owner; } catch { }
            }
            w.ShowDialog();
            return w.Result;
        }

        public ColorPickerWindow(string title, Color start)
        {
            original = start;
            r = start.R; g = start.G; b = start.B;

            Title = title;
            try { Icon = IconLoader.AppIcon(); } catch { }
            Width = 460;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 720;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;

            BuildUi(title);
        }

        private void BuildUi(string title)
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border cmp = Theme.GlassCard(Theme.RadiusCard);
            StackPanel cp = new StackPanel();
            cp.Orientation = Orientation.Horizontal;

            originalChip = Chip(original, 54, 32, 11);
            currentChip = Chip(Color.FromRgb((byte)r, (byte)g, (byte)b), 54, 32, 11);

            StackPanel left = new StackPanel();
            left.Margin = new Thickness(0, 0, 14, 0);
            left.Children.Add(RowLabel("打开时的颜色"));
            left.Children.Add(originalChip);
            cp.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Children.Add(RowLabel("正在选择的颜色"));
            right.Children.Add(currentChip);
            cp.Children.Add(right);
            cmp.Child = cp;
            Grid.SetRow(cmp, 0);
            root.Children.Add(cmp);

            Border pcard = Theme.GlassCard(Theme.RadiusCard);
            pcard.Margin = new Thickness(0, 10, 0, 0);
            StackPanel ps = new StackPanel();
            ps.Children.Add(RowLabel("调色板"));
            WrapPanel wp = new WrapPanel();
            wp.Margin = new Thickness(0, 6, 0, 0);
            for (int i = 0; i < Palette.Length; i++)
            {
                Border sw = Chip(Ui.ParseColor(Palette[i], Colors.White), 26, 26, 8);
                sw.Margin = new Thickness(0, 0, 5, 5);
                Color c = Ui.ParseColor(Palette[i], Colors.White);
                sw.MouseLeftButtonUp += delegate (object s, System.Windows.Input.MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    SetColor(c);
                };
                cells.Add(sw);
                wp.Children.Add(sw);
            }
            ps.Children.Add(wp);
            pcard.Child = ps;
            Grid.SetRow(pcard, 1);
            root.Children.Add(pcard);

            Border rgbCard = Theme.GlassCard(Theme.RadiusCard);
            rgbCard.Margin = new Thickness(0, 10, 0, 0);
            StackPanel rs = new StackPanel();
            rs.Children.Add(RowLabel("颜色调整"));
            sr = RgbRow(rs, "红", out vr, delegate { Apply(0); });
            sg = RgbRow(rs, "绿", out vg, delegate { Apply(1); });
            sb = RgbRow(rs, "蓝", out vb, delegate { Apply(2); });
            rgbCard.Child = rs;
            Grid.SetRow(rgbCard, 2);
            root.Children.Add(rgbCard);

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.HorizontalAlignment = HorizontalAlignment.Right;
            btns.Margin = new Thickness(0, 14, 0, 0);
            btns.Children.Add(Theme.PillButton("取消", false, delegate { Result = null; Close(); }));
            btns.Children.Add(Theme.PillButton("确定", true, delegate
            {
                Result = Color.FromRgb((byte)r, (byte)g, (byte)b);
                Close();
            }));
            Grid.SetRow(btns, 3);
            root.Children.Add(btns);

            Grid shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            FrameworkElement bar = Theme.TitleBar(this, title, "", delegate { Result = null; Close(); });
            bar.Margin = new Thickness(Theme.PadWindow, Theme.PadContentTop, Theme.PadWindow, 8);
            Grid.SetRow(bar, 0);
            shell.Children.Add(bar);
            root.Margin = new Thickness(Theme.PadWindow, 2, Theme.PadWindow, Theme.PadWindowBottom);
            Grid.SetRow(root, 1);
            shell.Children.Add(root);

            Grid liquid = Theme.LiquidGlassShell(Theme.RadiusWindow, shell);
            liquid.Margin = new Thickness(16, 14, 16, 16);
            Content = liquid;
            Theme.PlayEntry(this, liquid);

            UpdateChips();
        }

        private Slider RgbRow(Panel host, string label, out TextBlock value, Action onChanged)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 4, 0, 4);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });

            TextBlock t = Theme.Text(label, 12, Theme.InkSoft, false);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 0);
            g.Children.Add(t);

            Slider sl = new Slider();
            sl.Minimum = 0;
            sl.Maximum = 255;
            sl.SmallChange = 1;
            sl.LargeChange = 16;
            sl.Height = 24;
            sl.VerticalAlignment = VerticalAlignment.Center;
            sl.ValueChanged += delegate { onChanged(); };
            Grid.SetColumn(sl, 1);
            g.Children.Add(sl);

            TextBlock v = Theme.Text("0", 11.5, Theme.Ink, true);
            v.VerticalAlignment = VerticalAlignment.Center;
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 2);
            g.Children.Add(v);

            Theme.TintSlider(sl);
            host.Children.Add(g);
            value = v;
            return sl;
        }

        private void Apply(int which)
        {
            if (sync) { return; }
            sync = true;
            try
            {
                if (which == 0) { r = (int)Math.Round(sr.Value); }
                else if (which == 1) { g = (int)Math.Round(sg.Value); }
                else { b = (int)Math.Round(sb.Value); }
            }
            finally { sync = false; }

            vr.Text = r.ToString(CultureInfo.InvariantCulture);
            vg.Text = g.ToString(CultureInfo.InvariantCulture);
            vb.Text = b.ToString(CultureInfo.InvariantCulture);
            UpdateChips();
        }

        private void SetColor(Color c)
        {
            sync = true;
            try
            {
                r = c.R; g = c.G; b = c.B;
                sr.Value = r; sg.Value = g; sb.Value = b;
                vr.Text = r.ToString(CultureInfo.InvariantCulture);
                vg.Text = g.ToString(CultureInfo.InvariantCulture);
                vb.Text = b.ToString(CultureInfo.InvariantCulture);
            }
            finally { sync = false; }
            UpdateChips();
        }

        private void UpdateChips()
        {
            try
            {
                Color cur = Color.FromRgb((byte)r, (byte)g, (byte)b);
                currentChip.Background = new SolidColorBrush(cur);
                originalChip.Background = new SolidColorBrush(original);

                for (int i = 0; i < cells.Count && i < Palette.Length; i++)
                {
                    Color pc = Ui.ParseColor(Palette[i], Colors.White);
                    bool on = (pc.R == cur.R && pc.G == cur.G && pc.B == cur.B);
                    cells[i].BorderThickness = new Thickness(on ? 2.4 : 1);
                    cells[i].BorderBrush = on
                        ? new SolidColorBrush(Theme.Accent)
                        : new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
                }
            }
            catch { }
        }

        private static Border Chip(Color c, double w, double h, double radius)
        {
            Border b = new Border();
            b.Width = w;
            b.Height = h;
            b.CornerRadius = new CornerRadius(radius);
            b.Background = new SolidColorBrush(c);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            b.SnapsToDevicePixels = true;
            b.Cursor = System.Windows.Input.Cursors.Hand;
            return b;
        }

        private static TextBlock RowLabel(string s)
        {
            TextBlock t = Theme.Text(s, 11, Theme.InkSoft, false);
            t.Margin = new Thickness(0, 0, 0, 5);
            return t;
        }
    }
}
