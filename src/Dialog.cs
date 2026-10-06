using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SideStack
{
    public static class Dialog
    {
        public static string Prompt(string title, string message, string def)
        {
            string result = null;
            try
            {
                Window w = new Window();
                w.WindowStyle = WindowStyle.None;
                w.AllowsTransparency = true;
                w.Background = Brushes.Transparent;
                w.ResizeMode = ResizeMode.NoResize;
                w.ShowInTaskbar = false;
                w.Topmost = true;
                w.SizeToContent = SizeToContent.WidthAndHeight;
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                w.FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

                Border panel = Theme.GlassPanel(16, 0.97);
                panel.Margin = new Thickness(16);
                panel.Padding = new Thickness(18, 12, 18, 16);
                panel.MinWidth = 380;

                StackPanel root = new StackPanel();
                root.Children.Add(Theme.TitleBar(w, title, null, delegate { w.Close(); }));

                if (!string.IsNullOrEmpty(message))
                {
                    TextBlock msg = Theme.Hint(message);
                    msg.Margin = new Thickness(0, 4, 0, 0);
                    root.Children.Add(msg);
                }

                TextBox tb = new TextBox();
                tb.Text = def == null ? "" : def;
                tb.FontSize = 13.5;
                tb.Padding = new Thickness(9, 7, 9, 7);
                tb.Margin = new Thickness(0, 12, 0, 0);
                tb.BorderThickness = new Thickness(1);
                tb.BorderBrush = new SolidColorBrush(Theme.Line);
                tb.Background = new SolidColorBrush(Colors.White);
                tb.Foreground = new SolidColorBrush(Theme.Ink);
                tb.CaretBrush = new SolidColorBrush(Theme.Accent);
                root.Children.Add(tb);

                StackPanel btns = new StackPanel();
                btns.Orientation = Orientation.Horizontal;
                btns.HorizontalAlignment = HorizontalAlignment.Right;
                btns.Margin = new Thickness(0, 16, 0, 0);

                Border cancel = Theme.PillButton("取消", false, delegate { w.Close(); });
                Border ok = Theme.PillButton("确定", true, delegate
                {
                    result = tb.Text;
                    w.Close();
                });
                cancel.Margin = new Thickness(0, 0, 8, 0);
                ok.Margin = new Thickness(0);
                btns.Children.Add(cancel);
                btns.Children.Add(ok);
                root.Children.Add(btns);

                panel.Child = root;
                w.Content = panel;

                w.PreviewKeyDown += delegate (object s, KeyEventArgs e)
                {
                    if (e.Key == Key.Enter)
                    {
                        result = tb.Text;
                        w.Close();
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        w.Close();
                        e.Handled = true;
                    }
                };

                w.Loaded += delegate
                {
                    Theme.PlayEntry(w, panel);
                    tb.Focus();
                    tb.SelectAll();
                };

                w.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Write("Dialog.Prompt", ex);
                return null;
            }
            return result;
        }

        public static bool Confirm(string title, string message)
        {
            bool ok = false;
            try
            {
                Window w = new Window();
                w.WindowStyle = WindowStyle.None;
                w.AllowsTransparency = true;
                w.Background = Brushes.Transparent;
                w.ResizeMode = ResizeMode.NoResize;
                w.ShowInTaskbar = false;
                w.Topmost = true;
                w.SizeToContent = SizeToContent.WidthAndHeight;
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                w.FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

                Border panel = Theme.GlassPanel(16, 0.97);
                panel.Margin = new Thickness(16);
                panel.Padding = new Thickness(18, 12, 18, 16);
                panel.MinWidth = 360;

                StackPanel root = new StackPanel();
                root.Children.Add(Theme.TitleBar(w, title, null, delegate { w.Close(); }));
                TextBlock msg = Theme.Hint(message);
                msg.Margin = new Thickness(0, 4, 0, 0);
                root.Children.Add(msg);

                StackPanel btns = new StackPanel();
                btns.Orientation = Orientation.Horizontal;
                btns.HorizontalAlignment = HorizontalAlignment.Right;
                btns.Margin = new Thickness(0, 16, 0, 0);

                Border cancel = Theme.PillButton("取消", false, delegate { w.Close(); });
                Border yes = Theme.PillButton("确定", true, delegate
                {
                    ok = true;
                    w.Close();
                });
                cancel.Margin = new Thickness(0, 0, 8, 0);
                yes.Margin = new Thickness(0);
                btns.Children.Add(cancel);
                btns.Children.Add(yes);
                root.Children.Add(btns);

                panel.Child = root;
                w.Content = panel;
                w.PreviewKeyDown += delegate (object s, KeyEventArgs e)
                {
                    if (e.Key == Key.Enter) { ok = true; w.Close(); e.Handled = true; }
                    else if (e.Key == Key.Escape) { w.Close(); e.Handled = true; }
                };
                w.Loaded += delegate { Theme.PlayEntry(w, panel); };
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Write("Dialog.Confirm", ex);
                return false;
            }
            return ok;
        }
    }
}
