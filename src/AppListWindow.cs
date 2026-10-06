using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SideStack
{
    public class AppListWindow : Window
    {
        private DockConfig d;
        private Action onChanged;
        private StackPanel itemsPanel;
        private TextBlock tip;

        public AppListWindow(DockConfig cfg, Action changed)
        {
            d = cfg;
            onChanged = changed;

            Title = "应用列表";
            try { Icon = IconLoader.AppIcon(); } catch { }
            Width = 700;
            Height = 640;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;

            BuildUi();
            Rebuild();
        }

        private void BuildUi()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Margin = new Thickness(Theme.PadWindow, 2, Theme.PadWindow, Theme.PadWindowBottom);

            StackPanel bar = new StackPanel();
            bar.Orientation = Orientation.Horizontal;
            bar.Margin = new Thickness(0, 0, 0, 14);
            bar.Children.Add(Theme.PillButton("添加应用…", true, delegate { AddApp(); }));
            bar.Children.Add(Theme.PillButton("返回设置", false, delegate { Close(); }));
            Grid.SetRow(bar, 0);
            root.Children.Add(bar);

            Border card = Theme.GlassCard(Theme.RadiusCard);
            card.Padding = new Thickness(12, 10, 12, 10);
            ScrollViewer sv = new ScrollViewer();

            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            itemsPanel = new StackPanel();
            sv.Content = itemsPanel;

            sv.Margin = new Thickness(0, 0, -SystemParameters.VerticalScrollBarWidth, 0);
            card.Child = sv;
            Grid.SetRow(card, 2);
            root.Children.Add(card);

            Grid foot = new Grid();
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.Margin = new Thickness(0, 10, 0, 0);

            StackPanel fleft = new StackPanel();
            tip = Theme.Text("", 11.5, Theme.Accent, false);
            tip.Margin = new Thickness(0, 2, 0, 0);
            fleft.Children.Add(tip);
            Grid.SetColumn(fleft, 0);
            foot.Children.Add(fleft);

            StackPanel fbtns = new StackPanel();
            fbtns.Orientation = Orientation.Horizontal;
            fbtns.Children.Add(Theme.PillButton("完成", true, delegate { Close(); }));
            Grid.SetColumn(fbtns, 1);
            foot.Children.Add(fbtns);
            Grid.SetRow(foot, 3);
            root.Children.Add(foot);

            Grid shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            FrameworkElement tb = Theme.TitleBar(this, "应用列表", "", delegate { Close(); });

            tb.Margin = new Thickness(Theme.PadWindow, Theme.PadContentTop, Theme.PadWindow, 8);
            Grid.SetRow(tb, 0);
            shell.Children.Add(tb);
            Grid.SetRow(root, 1);
            shell.Children.Add(root);

            Grid liquid = Theme.LiquidGlassShell(Theme.RadiusWindow, shell);
            liquid.Margin = new Thickness(18, 16, 18, 18);
            Content = liquid;
            Theme.PlayEntry(this, liquid);
        }

        private void Rebuild()
        {
            itemsPanel.Children.Clear();
            AddGroup("左侧面板", d.Left, true);
            AddGroup("右侧面板", d.Right, false);

            int l = d.Left.Count - DockConfig.CountEmpty(d.Left);
            int rr = d.Right.Count - DockConfig.CountEmpty(d.Right);

            if (l == 0 && rr == 0)
            {
                itemsPanel.Children.Add(Theme.Hint("列表为空：点上方「添加应用…」把程序或快捷方式加进 Dock。"));
            }
        }

        private void AddGroup(string title, List<DockItem> list, bool isLeft)
        {
            int empties = DockConfig.CountEmpty(list);
            string head = title + "（" + (list.Count - empties) + " 个应用";
            if (empties > 0) { head += "、" + empties + " 个空白位置"; }
            head += "）";

            TextBlock t = Theme.Text(head, 12.5, Theme.Ink, true);
            t.Margin = new Thickness(0, 10, 0, 6);
            itemsPanel.Children.Add(t);

            if (list.Count == 0)
            {
                itemsPanel.Children.Add(Theme.Hint("（这一侧还没有内容）"));
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                itemsPanel.Children.Add(BuildRow(list[i], isLeft));
            }
        }

        private Border BuildRow(DockItem item, bool isLeft)
        {
            Border row = new Border();
            row.CornerRadius = new CornerRadius(Theme.RadiusCard / 2.0);
            row.Background = Theme.Brush(Theme.GlassBase, 0.72);
            row.BorderThickness = new Thickness(1);
            row.BorderBrush = Theme.Brush(Theme.Line, 0.7);
            row.Padding = new Thickness(12, 4, 10, 4);
            row.Margin = new Thickness(0, 0, 0, 4);
            row.SnapsToDevicePixels = true;
            row.ToolTip = item.IsEmpty ? null : item.Path;
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Child = g;

            string name = item.IsEmpty
                ? "空白位置"
                : (item.Name != null && item.Name.Length > 0
                    ? item.Name : System.IO.Path.GetFileNameWithoutExtension(item.Path));

            Grid line = new Grid();
            line.VerticalAlignment = VerticalAlignment.Center;
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock t = Theme.Text(name, 12.5, item.IsEmpty ? Theme.Accent : Theme.Ink, true);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.MaxWidth = 200;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 0);
            line.Children.Add(t);

            TextBlock sub = Theme.Text(item.IsEmpty
                ? "占位，不指向文件"
                : item.Path, 10.5, Theme.InkSoft, false);
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.Margin = new Thickness(10, 1, 0, 0);
            sub.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(sub, 1);
            line.Children.Add(sub);

            Grid.SetColumn(line, 0);
            g.Children.Add(line);

            StackPanel ops = new StackPanel();
            ops.Orientation = Orientation.Horizontal;
            ops.VerticalAlignment = VerticalAlignment.Center;

            if (item.IsEmpty)
            {
                ops.Children.Add(Mini("清除", "删掉这个空白位置", Theme.InkSoft, Theme.HoverTint,
                    delegate { RemoveItem(item, isLeft); }));
            }
            else
            {
                ops.Children.Add(Mini("改名", "修改这个图标在 Dock 上显示的名称", Theme.Accent, Theme.AccentSoft,
                    delegate { RenameItem(item); }));
                ops.Children.Add(Mini("删除", "从 Dock 列表移除（不删除磁盘文件）", Theme.Danger, Theme.DangerSoft,
                    delegate { RemoveItem(item, isLeft); }));
            }
            ops.Children.Add(Mini("↑", "上移一位", Theme.InkSoft, Theme.HoverTint, delegate { MoveItem(item, isLeft, -1); }));
            ops.Children.Add(Mini("↓", "下移一位", Theme.InkSoft, Theme.HoverTint, delegate { MoveItem(item, isLeft, 1); }));
            ops.Children.Add(Mini("⇄", isLeft ? "移到右侧面板" : "移到左侧面板", Theme.InkSoft, Theme.HoverTint,
                delegate { TransferItem(item, isLeft); }));

            Grid.SetColumn(ops, 1);
            g.Children.Add(ops);

            ops.Opacity = 0.0;
            ops.IsHitTestVisible = false;
            row.MouseEnter += delegate
            {
                row.Background = Theme.Brush(Theme.HoverTint, 0.95);
                ops.IsHitTestVisible = true;
                ops.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120)));
            };
            row.MouseLeave += delegate
            {
                row.Background = Theme.Brush(Theme.GlassBase, 0.72);
                ops.IsHitTestVisible = false;
                ops.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120)));
            };
            return row;
        }

        private static Border Mini(string text, string tipText, Color fg, Color hoverBg, Action onClick)
        {
            Border b = Theme.MiniButton(text, fg, hoverBg, onClick);
            if (tipText != null && tipText.Length > 0)
            {
                ToolTip tt = new ToolTip();
                tt.Content = tipText;
                Theme.StyleToolTip(tt);
                b.ToolTip = tt;
            }
            return b;
        }

        private void AddApp()
        {
            try
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Title = "选择要添加到 Dock 的应用（程序 / 快捷方式 / 任意文件）";
                dlg.Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.url|所有文件|*.*";
                dlg.Multiselect = true;
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != true) { return; }

                int n = 0;
                foreach (string f in dlg.FileNames)
                {
                    d.Right.Add(new DockItem(f, System.IO.Path.GetFileNameWithoutExtension(f)));

                    for (int k = d.Ignored.Count - 1; k >= 0; k--)
                    {
                        if (string.Equals(d.Ignored[k], f, StringComparison.OrdinalIgnoreCase))
                        {
                            d.Ignored.RemoveAt(k);
                        }
                    }
                    Logger.Write("应用列表添加: " + f);
                    n++;
                }
                Rebuild();
                Notify();
                SetTip("已添加 " + n + " 个应用到右侧面板（已即时生效）。");
            }
            catch (Exception ex)
            {
                Logger.Write("AppListWindow.AddApp", ex);
                SetTip("添加应用失败: " + ex.Message);
            }
        }

        private void RenameItem(DockItem item)
        {
            try
            {
                string now = (item.Name != null && item.Name.Length > 0)
                    ? item.Name : System.IO.Path.GetFileNameWithoutExtension(item.Path);
                string nn = Dialog.Prompt("修改应用名称", "Dock 上显示的名称：", now);
                if (nn == null) { return; }
                nn = nn.Trim();
                if (nn.Length == 0) { SetTip("名称不能为空，未做修改。"); return; }
                item.Name = nn;
                Rebuild();
                Notify();
                SetTip("已改名为「" + nn + "」（已即时生效）。");
            }
            catch (Exception ex) { Logger.Write("AppListWindow.RenameItem", ex); }
        }

        private void MoveItem(DockItem item, bool isLeft, int dir)
        {
            List<DockItem> list = isLeft ? d.Left : d.Right;
            int i = list.IndexOf(item);
            int j = i + dir;
            if (i < 0 || j < 0 || j >= list.Count) { return; }
            list.RemoveAt(i);
            list.Insert(j, item);
            Rebuild();
            Notify();
        }

        private void TransferItem(DockItem item, bool isLeft)
        {
            List<DockItem> from = isLeft ? d.Left : d.Right;
            List<DockItem> to = isLeft ? d.Right : d.Left;
            if (!from.Remove(item)) { return; }
            to.Add(item);
            Rebuild();
            Notify();
        }

        private void RemoveItem(DockItem item, bool isLeft)
        {
            string nm = item.IsEmpty ? "空白位置" : ((item.Name != null && item.Name.Length > 0)
                ? item.Name : System.IO.Path.GetFileNameWithoutExtension(item.Path));
            if (!Dialog.Confirm("从 Dock 列表移除", "确定把「" + nm + "」从 Dock 栏移出吗？\n（只影响 Dock 列表，不会删除磁盘上的程序文件）"))
            {
                return;
            }
            List<DockItem> list = isLeft ? d.Left : d.Right;
            list.Remove(item);

            if (!ConfigStore.ContainsCI(d.Ignored, item.Path)) { d.Ignored.Add(item.Path); }
            Rebuild();
            Notify();
            SetTip("已从 Dock 列表移出「" + nm + "」（磁盘文件未做任何改动）。");
        }

        private void Notify()
        {
            if (onChanged != null)
            {
                try { onChanged(); } catch { }
            }
        }

        private void SetTip(string s)
        {
            try { if (tip != null) { tip.Text = s; } } catch { }
        }
    }
}
