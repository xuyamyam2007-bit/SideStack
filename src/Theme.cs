using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SideStack
{
    public static class Theme
    {
        public static readonly Color Accent = Color.FromRgb(0x2B, 0x6C, 0xE8);
        public static readonly Color AccentDeep = Color.FromRgb(0x1B, 0x4F, 0xB8);
        public static readonly Color AccentSoft = Color.FromRgb(0xE9, 0xF0, 0xFE);
        public static readonly Color Ink = Color.FromRgb(0x1E, 0x23, 0x2E);
        public static readonly Color InkSoft = Color.FromRgb(0x6C, 0x74, 0x84);
        public static readonly Color Danger = Color.FromRgb(0xC0, 0x2B, 0x1C);
        public static readonly Color DangerSoft = Color.FromRgb(0xFD, 0xE9, 0xEA);
        public static readonly Color GlassBase = Color.FromRgb(0xF8, 0xFA, 0xFD);
        public static readonly Color Line = Color.FromRgb(0xCF, 0xD8, 0xE6);
        public static readonly Color HoverTint = Color.FromRgb(0xF1, 0xF5, 0xFC);

        public const double GlassOpacity = 0.90;

        public static readonly Color GlassFill = Color.FromRgb(0xF6, 0xF8, 0xFC);

        public static Color GlassEdge { get { return Color.FromArgb(170, 255, 255, 255); } }

        private static ControlTemplate RoundPopupTemplate(Type target, double radius,
                                                          double padL, double padT, double padR, double padB,
                                                          bool itemsHost)
        {
            ControlTemplate tpl = new ControlTemplate(target);

            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "PART_Round";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(GlassBase));
            bd.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Line));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty, new Thickness(padL, padT, padR, padB));
            bd.SetValue(Border.SnapsToDevicePixelsProperty, true);
            bd.SetValue(UIElement.EffectProperty, Shadow(20, 0.24, 3));

            if (itemsHost)
            {
                FrameworkElementFactory sv = new FrameworkElementFactory(typeof(ScrollViewer));
                sv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                sv.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                FrameworkElementFactory ip = new FrameworkElementFactory(typeof(ItemsPresenter));
                sv.AppendChild(ip);
                bd.AppendChild(sv);
            }
            else
            {
                bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            }

            tpl.VisualTree = bd;
            return tpl;
        }

        public static void StyleToolTip(ToolTip tt)
        {
            if (tt == null) { return; }
            try
            {
                tt.Foreground = new SolidColorBrush(Ink);
                tt.FontSize = 12.5;
                tt.Padding = new Thickness(0);
                tt.BorderThickness = new Thickness(0);
                tt.Background = Brushes.Transparent;
                tt.HasDropShadow = false;
                tt.Template = RoundPopupTemplate(typeof(ToolTip), 9, 10, 6, 10, 6, false);
                WindowCapture.Guard(tt);
            }
            catch { }
        }

        public static SolidColorBrush Brush(Color c, double alpha)
        {
            double a = alpha;
            if (a < 0) { a = 0; }
            if (a > 1) { a = 1; }
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255.0), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        public static SolidColorBrush Clear()
        {
            return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        public static DropShadowEffect Shadow(double blur, double opacity, double depth)
        {
            DropShadowEffect e = new DropShadowEffect();
            e.BlurRadius = blur;
            e.Opacity = opacity;
            e.ShadowDepth = depth;
            e.Direction = 270;
            e.Color = Color.FromRgb(0x0C, 0x14, 0x26);
            return e;
        }

        public static Border GlassPanel(double radius, double opacity)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(radius);
            b.Background = Brush(GlassBase, opacity);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Brush(Colors.White, 0.80);
            b.SnapsToDevicePixels = true;
            b.Effect = Shadow(26, 0.26, 4);
            return b;
        }

        public static void AnimateBrush(SolidColorBrush b, Color to, int ms)
        {
            if (b == null) { return; }
            try
            {
                if (b.IsFrozen) { return; }
                ColorAnimation a = new ColorAnimation(to, TimeSpan.FromMilliseconds(ms));
                a.FillBehavior = FillBehavior.HoldEnd;
                b.BeginAnimation(SolidColorBrush.ColorProperty, a);
            }
            catch { }
        }

        public static void AnimateBounce(UIElement e, double to, int ms)
        {
            if (e == null) { return; }
            try
            {
                ScaleTransform st = e.RenderTransform as ScaleTransform;
                if (st == null)
                {
                    st = new ScaleTransform(1.0, 1.0);
                    e.RenderTransformOrigin = new Point(0.5, 0.5);
                    e.RenderTransform = st;
                }
                DoubleAnimationUsingKeyFrames kf = new DoubleAnimationUsingKeyFrames();
                kf.Duration = TimeSpan.FromMilliseconds(ms);
                kf.KeyFrames.Add(new EasingDoubleKeyFrame(to * 1.08, KeyTime.FromPercent(0.55),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                kf.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromPercent(1.0),
                    new CubicEase { EasingMode = EasingMode.EaseIn }));
                st.BeginAnimation(ScaleTransform.ScaleXProperty, kf);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, kf);
            }
            catch { }
        }
        public static void AnimateScale(UIElement e, double to, int ms)
        {
            if (e == null) { return; }
            try
            {
                ScaleTransform st = e.RenderTransform as ScaleTransform;
                if (st == null)
                {
                    st = new ScaleTransform(1.0, 1.0);
                    e.RenderTransformOrigin = new Point(0.5, 0.5);
                    e.RenderTransform = st;
                }
                DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
                a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            catch { }
        }

        public static void PlayEntry(Window win, FrameworkElement root)
        {
            if (win == null) { return; }
            try
            {
                win.Opacity = 0.0;
                DoubleAnimation fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
                fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                win.BeginAnimation(UIElement.OpacityProperty, fade);

                if (root != null)
                {
                    ScaleTransform st = new ScaleTransform(0.976, 0.976);
                    root.RenderTransformOrigin = new Point(0.5, 0.5);
                    root.RenderTransform = st;
                    DoubleAnimation sc = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(260));
                    sc.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
                }
            }
            catch { }
        }

        public static readonly Color SliderFill = Color.FromRgb(0x01, 0x66, 0xFF);

        public const double RadiusWindow = 24;
        public const double RadiusCard   = 16;
        public const double PadWindow    = 24;
        public const double PadContentTop = 12;
        public const double PadWindowBottom = 20;
        private const double SliderTrackH = 8;
        private const double SliderThumbD = 18;

        private static readonly Brush SliderFillBrush = MakeSliderBrush(Color.FromRgb(0x4C, 0x8D, 0xF6), Color.FromRgb(0x2B, 0x6C, 0xE8));
        private static readonly Brush SliderFillHoverBrush = MakeSliderBrush(Color.FromRgb(0x5A, 0x99, 0xF8), Color.FromRgb(0x1B, 0x4F, 0xB8));
        private static readonly Brush SliderEmptyBrush = MakeSliderBrush(Color.FromRgb(0xEC, 0xEE, 0xF2), Color.FromRgb(0xE2, 0xE5, 0xEB));
        private static readonly Brush SliderEmptyHoverBrush = MakeSliderBrush(Color.FromRgb(0xE2, 0xE5, 0xEB), Color.FromRgb(0xD6, 0xDA, 0xE2));

        private static Brush MakeSliderBrush(Color a, Color b)
        {
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(1, 0);
            g.GradientStops.Add(new GradientStop(a, 0.0));
            g.GradientStops.Add(new GradientStop(b, 1.0));
            g.Freeze();
            return g;
        }

        public static void TintSlider(Slider sl)
        {
            if (sl == null) { return; }
            try
            {
                sl.Foreground = new SolidColorBrush(Accent);
                sl.ApplyTemplate();
                Track tr = sl.Template.FindName("PART_Track", sl) as Track;
                if (tr == null) { return; }

                RebuildSliderButtons(tr);
                StyleThumb(tr.Thumb);
            }
            catch (Exception ex) { Logger.Write("Theme.TintSlider", ex); }
        }

        private static void RebuildSliderButtons(Track tr)
        {
            RepeatButton dec = tr.DecreaseRepeatButton;
            RepeatButton inc = tr.IncreaseRepeatButton;
            if (dec != null && dec.Tag as string != "flat")
            {
                dec.Tag = "flat";
                dec.Template = CapsuleButtonTemplate(true);
                dec.MouseEnter += delegate { SetCapsuleFill(dec, true, true); };
                dec.MouseLeave += delegate { SetCapsuleFill(dec, false, true); };
            }
            if (inc != null && inc.Tag as string != "flat")
            {
                inc.Tag = "flat";
                inc.Template = CapsuleButtonTemplate(false);
                inc.MouseEnter += delegate { SetCapsuleFill(inc, true, false); };
                inc.MouseLeave += delegate { SetCapsuleFill(inc, false, false); };
            }

            SetCapsuleFill(dec, false, true);
            SetCapsuleFill(inc, false, false);
        }

        private static void SetCapsuleFill(RepeatButton b, bool hover, bool filled)
        {
            if (b == null) { return; }
            if (ApplyCapsuleFill(b, hover, filled)) { return; }
            try
            {
                b.Dispatcher.BeginInvoke(new Action(delegate
                {
                    ApplyCapsuleFill(b, hover, filled);
                }), DispatcherPriority.Loaded);
            }
            catch { }
        }

        private static bool ApplyCapsuleFill(RepeatButton b, bool hover, bool filled)
        {
            if (b == null) { return false; }
            try
            {
                if (b.Template == null) { b.ApplyTemplate(); }
                if (VisualTreeHelper.GetChildrenCount(b) == 0) { b.ApplyTemplate(); }
                Border bd = b.Template == null ? null : b.Template.FindName("PART_Border", b) as Border;
                if (bd == null) { return false; }

                bd.Background = filled
                    ? (hover ? SliderFillHoverBrush : SliderFillBrush)
                    : (hover ? SliderEmptyHoverBrush : SliderEmptyBrush);
                return true;
            }
            catch { return false; }
        }

        private static ControlTemplate CapsuleButtonTemplate(bool roundLeft)
        {
            ControlTemplate tpl = new ControlTemplate(typeof(RepeatButton));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "PART_Border";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(SliderTrackH / 2.0));
            bd.SetValue(FrameworkElement.HeightProperty, SliderTrackH);

            bd.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            tpl.VisualTree = bd;
            return tpl;
        }

        private static void StyleThumb(Thumb th)
        {
            if (th == null) { return; }
            th.Width = SliderThumbD;
            th.Height = SliderThumbD;
            th.BorderThickness = new Thickness(0);
            th.Background = null;
            th.BorderBrush = null;
            th.Effect = null;
            th.Cursor = Cursors.Hand;
            if (th.Tag as string == "flat") { return; }
            th.Tag = "flat";

            ControlTemplate tpl = new ControlTemplate(typeof(Thumb));
            FrameworkElementFactory el = new FrameworkElementFactory(typeof(Ellipse));
            el.SetValue(Ellipse.FillProperty, new SolidColorBrush(Colors.White));

            el.SetValue(Ellipse.StrokeProperty, new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)));
            el.SetValue(Ellipse.StrokeThicknessProperty, 1.0);
            el.SetValue(FrameworkElement.WidthProperty, SliderThumbD);
            el.SetValue(FrameworkElement.HeightProperty, SliderThumbD);

            el.SetValue(FrameworkElement.MarginProperty, new Thickness(-0.5));
            el.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            el.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tpl.VisualTree = el;
            th.Template = tpl;

            th.Effect = ThumbShadow(0.16, 1.5);
            th.MouseEnter += delegate { th.Effect = ThumbShadow(0.26, 2.5); };
            th.MouseLeave += delegate { th.Effect = ThumbShadow(0.16, 1.5); };
        }

        private static System.Windows.Media.Effects.DropShadowEffect ThumbShadow(double opacity, double blur)
        {
            System.Windows.Media.Effects.DropShadowEffect e =
                new System.Windows.Media.Effects.DropShadowEffect();
            e.Color = Colors.Black;
            e.Opacity = opacity;
            e.BlurRadius = blur * 2.0;
            e.ShadowDepth = blur * 0.5;
            e.Direction = 270;
            e.Freeze();
            return e;
        }

        public static void MakeInvisibleScrollBar(ScrollBar sb)
        {
            if (sb == null) { return; }
            try
            {
                sb.Width = 0;
                sb.Opacity = 0;
                sb.IsHitTestVisible = false;
                sb.Template = EmptyTemplate(typeof(ScrollBar));
            }
            catch (Exception ex) { Logger.Write("Theme.MakeInvisibleScrollBar", ex); }
        }

        private static ControlTemplate EmptyTemplate(Type forType)
        {
            ControlTemplate tpl = new ControlTemplate(forType);
            FrameworkElementFactory g = new FrameworkElementFactory(typeof(Grid));
            g.SetValue(FrameworkElement.WidthProperty, 0.0);
            g.SetValue(FrameworkElement.HeightProperty, 0.0);
            tpl.VisualTree = g;
            return tpl;
        }

        public static TextBlock Text(string s, double size, Color color, bool bold)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.Foreground = new SolidColorBrush(color);
            if (bold) { t.FontWeight = FontWeights.SemiBold; }
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        public static TextBlock Hint(string s)
        {
            TextBlock t = Text(s, 11, InkSoft, false);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 2, 0, 2);
            return t;
        }

        public static FrameworkElement SectionTitle(string s)
        {
            Border bar = new Border();
            bar.Width = 3.5;
            bar.Height = 14;
            bar.CornerRadius = new CornerRadius(2);
            bar.Background = new SolidColorBrush(Accent);
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Margin = new Thickness(0, 0, 8, 0);

            TextBlock t = Text(s, 12.5, Ink, true);

            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.Children.Add(bar);
            sp.Children.Add(t);

            Border host = new Border();
            host.Child = sp;
            host.Margin = new Thickness(0, 12, 0, 6);
            return host;
        }

        public static FrameworkElement TitleBar(Window win, string title, string subtitle, Action onClose)
        {
            Grid g = new Grid();
            g.Height = 38;
            g.Margin = new Thickness(0, 0, 0, 8);
            g.Background = Clear();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinition auto = new ColumnDefinition();
            auto.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(auto);

            Border dot = new Border();
            dot.Width = 10;
            dot.Height = 10;
            dot.CornerRadius = new CornerRadius(5);
            dot.Background = new SolidColorBrush(Accent);
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Margin = new Thickness(0, 0, 9, 0);

            TextBlock t = Text(title, 15, Ink, true);
            t.VerticalAlignment = VerticalAlignment.Center;

            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(dot);
            sp.Children.Add(t);

            if (subtitle != null && subtitle.Length > 0)
            {
                TextBlock sub = Text(subtitle, 10.5, InkSoft, false);
                sub.VerticalAlignment = VerticalAlignment.Bottom;
                sub.Margin = new Thickness(8, 0, 0, 2);
                sp.Children.Add(sub);
            }

            Grid.SetColumn(sp, 0);
            g.Children.Add(sp);

            Border close = GlyphButton("✕", onClose);
            Grid.SetColumn(close, 1);
            g.Children.Add(close);

            g.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                try { win.DragMove(); }
                catch { }
            };
            return g;
        }

        public static Border GlyphButton(string glyph, Action onClick)
        {
            SolidColorBrush bg = Clear();
            Border b = new Border();
            b.Width = 28;
            b.Height = 28;
            b.CornerRadius = new CornerRadius(14);
            b.Background = bg;
            b.Cursor = Cursors.Hand;
            b.VerticalAlignment = VerticalAlignment.Center;
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            b.RenderTransform = new ScaleTransform(1.0, 1.0);

            TextBlock t = Text(glyph, 12, InkSoft, false);
            t.FontFamily = new FontFamily("Segoe UI Symbol, Microsoft YaHei");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;

            b.MouseEnter += delegate
            {
                AnimateBrush(bg, Color.FromRgb(0xE8, 0x3B, 0x3B), 130);
                t.Foreground = new SolidColorBrush(Colors.White);
                AnimateScale(b, 1.08, 130);
            };
            b.MouseLeave += delegate
            {
                AnimateBrush(bg, Color.FromArgb(0, 0, 0, 0), 170);
                t.Foreground = new SolidColorBrush(InkSoft);
                AnimateScale(b, 1.0, 150);
            };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onClick != null) { onClick(); }
            };
            return b;
        }

        public static Border PillButton(string text, bool primary, Action onClick)
        {
            Color baseColor = primary ? Accent : Color.FromArgb(0, 0, 0, 0);
            Color hoverColor = primary ? AccentDeep : HoverTint;
            Color fg = primary ? Colors.White : Ink;

            SolidColorBrush bg = new SolidColorBrush(baseColor);
            Border b = new Border();
            b.CornerRadius = new CornerRadius(9);
            b.Background = bg;
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = primary ? Clear() : Brush(Line, 0.85);
            b.Padding = new Thickness(15, 8, 15, 8);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.Cursor = Cursors.Hand;
            b.SnapsToDevicePixels = true;
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            b.RenderTransform = new ScaleTransform(1.0, 1.0);

            TextBlock t = Text(text, 13, fg, true);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;

            b.MouseEnter += delegate { AnimateBrush(bg, hoverColor, 130); AnimateScale(b, 1.04, 130); };
            b.MouseLeave += delegate { AnimateBrush(bg, baseColor, 170); AnimateScale(b, 1.0, 150); };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                AnimateScale(b, 0.96, 70);
            };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                AnimateScale(b, 1.04, 90);
                if (onClick != null) { onClick(); }
            };
            return b;
        }

        public static Border MiniButton(string text, Color fg, Color hoverBg, Action onClick)
        {
            SolidColorBrush bg = Clear();
            Border b = new Border();
            b.CornerRadius = new CornerRadius(6);
            b.Background = bg;
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Brush(fg, 0.22);
            b.Padding = new Thickness(7, 3, 7, 3);
            b.Margin = new Thickness(4, 0, 0, 0);
            b.Cursor = Cursors.Hand;
            b.VerticalAlignment = VerticalAlignment.Center;
            b.SnapsToDevicePixels = true;

            TextBlock t = Text(text, 11.5, fg, false);
            b.Child = t;

            b.MouseEnter += delegate { AnimateBrush(bg, hoverBg, 120); };
            b.MouseLeave += delegate { AnimateBrush(bg, Color.FromArgb(0, 0, 0, 0), 160); };
            b.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onClick != null) { onClick(); }
            };
            return b;
        }

        private static Style menuItemStyle;

        private static Style MenuItemStyle()
        {
            if (menuItemStyle != null) { return menuItemStyle; }
            string xaml = string.Join("\n", new string[]
            {
                "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"",
                "       xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"MenuItem\">",
                "  <Setter Property=\"Foreground\" Value=\"#1E232E\"/>",
                "  <Setter Property=\"FontSize\" Value=\"12.5\"/>",
                "  <Setter Property=\"Padding\" Value=\"12,7,16,7\"/>",
                "  <Setter Property=\"Template\">",
                "    <Setter.Value>",
                "      <ControlTemplate TargetType=\"MenuItem\">",
                "        <Border x:Name=\"Bd\" Background=\"Transparent\" CornerRadius=\"6\" SnapsToDevicePixels=\"True\">",
                "          <Grid>",
                "            <Grid.ColumnDefinitions>",
                "              <ColumnDefinition Width=\"Auto\"/>",
                "              <ColumnDefinition Width=\"*\"/>",
                "              <ColumnDefinition Width=\"Auto\"/>",
                "            </Grid.ColumnDefinitions>",
                "            <ContentPresenter Grid.Column=\"0\" ContentSource=\"Icon\" Margin=\"0,0,8,0\" VerticalAlignment=\"Center\"/>",
                "            <ContentPresenter Grid.Column=\"1\" ContentSource=\"Header\" RecognizesAccessKey=\"True\"",
                "                              Margin=\"{TemplateBinding Padding}\" VerticalAlignment=\"Center\"/>",
                "            <TextBlock x:Name=\"Arrow\" Grid.Column=\"2\" Text=\"&#x203A;\" FontSize=\"13\" Foreground=\"#6C7484\"",
                "                       VerticalAlignment=\"Center\" Margin=\"10,0,0,0\" Visibility=\"Collapsed\"/>",
                "            <Popup x:Name=\"PART_Popup\" Grid.Column=\"1\" AllowsTransparency=\"True\" Focusable=\"False\"",
                "                   IsOpen=\"{TemplateBinding IsSubmenuOpen}\" Placement=\"Right\" PopupAnimation=\"None\">",
                "              <Border Background=\"#F8FAFD\" BorderBrush=\"#CFD8E6\" BorderThickness=\"1\" CornerRadius=\"10\"",
                "                      Padding=\"6\" Margin=\"0,0,10,10\">",
                "                <Border.Effect>",
                "                  <DropShadowEffect BlurRadius=\"20\" ShadowDepth=\"3\" Opacity=\"0.24\" Color=\"#000000\"/>",
                "                </Border.Effect>",
                "                <ScrollViewer VerticalScrollBarVisibility=\"Auto\">",
                "                  <ItemsPresenter/>",
                "                </ScrollViewer>",
                "              </Border>",
                "            </Popup>",
                "          </Grid>",
                "        </Border>",
                "        <ControlTemplate.Triggers>",
                "          <Trigger Property=\"IsHighlighted\" Value=\"True\">",
                "            <Setter TargetName=\"Bd\" Property=\"Background\" Value=\"#FFE9EBEF\"/>",
                "          </Trigger>",
                "          <Trigger Property=\"IsEnabled\" Value=\"False\">",
                "            <Setter Property=\"Foreground\" Value=\"#A8AEB9\"/>",
                "          </Trigger>",
                "          <Trigger Property=\"Role\" Value=\"SubmenuHeader\">",
                "            <Setter TargetName=\"Arrow\" Property=\"Visibility\" Value=\"Visible\"/>",
                "          </Trigger>",
                "        </ControlTemplate.Triggers>",
                "      </ControlTemplate>",
                "    </Setter.Value>",
                "  </Setter>",
                "</Style>"
            });
            menuItemStyle = (Style)System.Windows.Markup.XamlReader.Parse(xaml);
            return menuItemStyle;
        }

        public static void StyleMenu(ContextMenu cm)
        {
            if (cm == null) { return; }
            try
            {
                cm.Background = Brushes.Transparent;
                cm.BorderThickness = new Thickness(0);
                cm.Padding = new Thickness(0);
                cm.HasDropShadow = false;
                cm.FontSize = 12.5;
                cm.Foreground = new SolidColorBrush(Ink);
                cm.Template = RoundPopupTemplate(typeof(ContextMenu), 10, 6, 6, 6, 6, true);

                cm.Resources[typeof(MenuItem)] = MenuItemStyle();
            }
            catch { }
        }

        public static MenuItem MenuEntry(string header, Action onClick)
        {
            MenuItem mi = new MenuItem();
            mi.Header = header;
            mi.Padding = new Thickness(10, 6, 16, 6);
            mi.Foreground = new SolidColorBrush(Ink);
            if (onClick != null)
            {
                mi.Click += delegate { onClick(); };
            }
            return mi;
        }

        public static Grid LiquidGlassShell(double radius, UIElement content)
        {
            return LiquidGlassShell(radius, content, 1.0);
        }

        public static Grid LiquidGlassShell(double radius, UIElement content, double opacity)
        {
            Grid host = new Grid();

            Border glass = new Border();
            glass.Name = "PART_Glass";
            glass.CornerRadius = new CornerRadius(radius);
            glass.BorderThickness = new Thickness(1.3);
            glass.SnapsToDevicePixels = true;
            glass.Effect = Shadow(40, 0.22, 7);
            ApplyShellOpacity(glass, opacity);
            host.Tag = glass;
            host.Children.Add(glass);

            Canvas blobs = new Canvas();
            blobs.Opacity = 0.85;
            blobs.IsHitTestVisible = false;
            Ellipse b1 = Blob(Accent, 0.30, 300, 300);
            Canvas.SetLeft(b1, -70); Canvas.SetTop(b1, -90);
            Ellipse b2 = Blob(Color.FromRgb(0x5A, 0xB6, 0xFF), 0.24, 320, 320);
            Canvas.SetLeft(b2, 280); Canvas.SetTop(b2, 190);
            blobs.Children.Add(b1);
            blobs.Children.Add(b2);
            host.Children.Add(blobs);

            host.SizeChanged += delegate
            {
                try
                {
                    blobs.Clip = new RectangleGeometry(
                        new Rect(0, 0, host.ActualWidth, host.ActualHeight), radius, radius);
                }
                catch { }
            };

            if (content != null) { host.Children.Add(content); }
            return host;
        }

        public static void SetShellOpacity(FrameworkElement host, double opacity)
        {
            if (host == null) { return; }
            ApplyShellOpacity(host.Tag as Border, opacity);
        }

        private static void ApplyShellOpacity(Border glass, double opacity)
        {
            if (glass == null) { return; }
            double a = Math.Max(0.05, Math.Min(1.0, opacity));
            LinearGradientBrush lg = new LinearGradientBrush();
            lg.StartPoint = new Point(0.0, 0.0);
            lg.EndPoint = new Point(1.0, 1.0);

            lg.GradientStops.Add(new GradientStop(ScaleAlpha(255, 255, 255, 238, a), 0.0));
            lg.GradientStops.Add(new GradientStop(ScaleAlpha(240, 246, 253, 208, a), 0.55));
            lg.GradientStops.Add(new GradientStop(ScaleAlpha(227, 238, 252, 216, a), 1.0));
            glass.Background = lg;
            glass.BorderBrush = new SolidColorBrush(ScaleAlpha(255, 255, 255, 215, a));
        }

        private static Color ScaleAlpha(byte r, byte g, byte b, int baseAlpha, double factor)
        {
            int a = (int)Math.Round(baseAlpha * factor);
            if (a < 0) { a = 0; } else if (a > 255) { a = 255; }
            return Color.FromArgb((byte)a, r, g, b);
        }

        private static Ellipse Blob(Color c, double alpha, double w, double h)
        {
            Ellipse e = new Ellipse();
            e.Width = w;
            e.Height = h;
            RadialGradientBrush rg = new RadialGradientBrush();
            rg.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(alpha * 255.0), c.R, c.G, c.B), 0.0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1.0));
            e.Fill = rg;
            BlurEffect be = new BlurEffect();
            be.Radius = 70;
            be.KernelType = KernelType.Gaussian;
            e.Effect = be;
            return e;
        }

        public static Border GlassCard(double radius)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(radius);
            LinearGradientBrush lg = new LinearGradientBrush();
            lg.StartPoint = new Point(0.0, 0.0);
            lg.EndPoint = new Point(0.0, 1.0);
            lg.GradientStops.Add(new GradientStop(Color.FromArgb(158, 255, 255, 255), 0.0));
            lg.GradientStops.Add(new GradientStop(Color.FromArgb(92, 255, 255, 255), 1.0));
            b.Background = lg;
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
            b.Padding = new Thickness(16, 12, 16, 14);
            b.SnapsToDevicePixels = true;
            return b;
        }

        public static Border ColorChip(Action onClick)
        {
            Border chip = new Border();
            chip.Width = 44;
            chip.Height = 26;
            chip.CornerRadius = new CornerRadius(9);
            chip.BorderThickness = new Thickness(1);
            chip.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0x4B, 0x55, 0x63));
            chip.Cursor = Cursors.Hand;
            chip.VerticalAlignment = VerticalAlignment.Center;
            chip.RenderTransformOrigin = new Point(0.5, 0.5);
            chip.RenderTransform = new ScaleTransform(1.0, 1.0);
            chip.MouseEnter += delegate { AnimateScale(chip, 1.06, 110); };
            chip.MouseLeave += delegate { AnimateScale(chip, 1.0, 130); };
            chip.MouseLeftButtonUp += delegate
            {
                if (onClick != null) { onClick(); }
            };
            return chip;
        }
    }

    public class ToggleSwitch : ContentControl
    {
        private Border track;
        private Border knob;
        private Canvas canvas;
        private double knobOffX;
        private double knobOnX;

        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register("IsChecked", typeof(bool), typeof(ToggleSwitch),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCheckedChanged));

        public bool IsChecked
        {
            get { return (bool)GetValue(IsCheckedProperty); }
            set { SetValue(IsCheckedProperty, value); }
        }

        public static readonly RoutedEvent CheckedEvent = EventManager.RegisterRoutedEvent(
            "Checked", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ToggleSwitch));
        public event RoutedEventHandler Checked { add { AddHandler(CheckedEvent, value); } remove { RemoveHandler(CheckedEvent, value); } }

        public static readonly RoutedEvent UncheckedEvent = EventManager.RegisterRoutedEvent(
            "Unchecked", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ToggleSwitch));
        public event RoutedEventHandler Unchecked { add { AddHandler(UncheckedEvent, value); } remove { RemoveHandler(UncheckedEvent, value); } }

        public ToggleSwitch()
        {
            Width = 46;
            Height = 26;
            VerticalAlignment = VerticalAlignment.Center;
            Focusable = true;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;

            canvas = new Canvas();
            canvas.Width = 46;
            canvas.Height = 26;

            track = new Border();
            track.Width = 46;
            track.Height = 26;
            track.CornerRadius = new CornerRadius(13);
            track.Background = new SolidColorBrush(Color.FromArgb(160, 200, 205, 212));
            canvas.Children.Add(track);

            knob = new Border();
            knob.Width = 22;
            knob.Height = 22;
            knob.CornerRadius = new CornerRadius(11);
            knob.Background = new SolidColorBrush(Colors.White);
            knob.Effect = new DropShadowEffect
            {
                Color = Color.FromArgb(80, 0, 0, 0),
                BlurRadius = 5,
                ShadowDepth = 1,
                Opacity = 0.8
            };
            Canvas.SetLeft(knob, 2);
            Canvas.SetTop(knob, 2);
            canvas.Children.Add(knob);

            knobOffX = 2;
            knobOnX = 46 - 22 - 2;

            Content = canvas;

            MouseLeftButtonUp += delegate
            {
                IsChecked = !IsChecked;
            };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Space || e.Key == Key.Enter)
                {
                    IsChecked = !IsChecked;
                    e.Handled = true;
                }
            };
            Loaded += delegate { UpdateVisual(false); };
        }

        private static void OnCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ToggleSwitch ts = d as ToggleSwitch;
            if (ts == null) { return; }
            bool on = (bool)e.NewValue;
            ts.UpdateVisual(true);
            if (ts.suppressEvents) { return; }
            if (on) { ts.RaiseEvent(new RoutedEventArgs(CheckedEvent, ts)); }
            else { ts.RaiseEvent(new RoutedEventArgs(UncheckedEvent, ts)); }
        }

        private bool suppressEvents;

        public void SetCheckedSilently(bool on)
        {
            suppressEvents = true;
            try { IsChecked = on; }
            finally { suppressEvents = false; }
        }

        private void UpdateVisual(bool animate)
        {
            bool on = IsChecked;

            if (animate)
            {
                SolidColorBrush bgBrush = track.Background as SolidColorBrush;
                if (bgBrush != null)
                {
                    ColorAnimation ca = new ColorAnimation();
                    ca.To = on ? Theme.Accent : Color.FromArgb(160, 200, 205, 212);
                    ca.Duration = TimeSpan.FromMilliseconds(220);
                    ca.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
                    try { bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, ca); }
                    catch { }
                }

                DoubleAnimation da = new DoubleAnimation();
                da.To = on ? knobOnX : knobOffX;
                da.Duration = TimeSpan.FromMilliseconds(220);
                da.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
                knob.BeginAnimation(Canvas.LeftProperty, da);
            }
            else
            {
                track.Background = new SolidColorBrush(on ? Theme.Accent : Color.FromArgb(160, 200, 205, 212));
                Canvas.SetLeft(knob, on ? knobOnX : knobOffX);
            }
        }
    }
}
