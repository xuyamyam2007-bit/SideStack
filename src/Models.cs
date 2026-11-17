using System;
using System.Collections.Generic;

namespace SideStack
{
    // 面板里的一个图标槽位，IsEmpty 表示空白位。
    public class DockItem
    {
        public string Path = "";
        public string Name = "";

        public bool IsEmpty = false;

        public DockItem() { }

        public DockItem(string path, string name)
        {
            Path = path;
            Name = name;
        }

        public static DockItem MakeEmpty()
        {
            DockItem g = new DockItem();
            g.IsEmpty = true;
            g.Path = "";
            g.Name = "";
            return g;
        }
    }

    // 全部配置项与面板网格计算。
    public class DockConfig
    {
        public const double PanelRadius = 16;

        public const double PanelPadding = 13;
        public const double PanelOpacity = 0.92;

        public const double HoverScale = 1.08;

        public const double HoverSafeDip = 2.0;

        public double IconSize = 44;
        public double IconSpacing = 8;
        public double IconPad = 6;

        public string PanelStyle = "glass";

        public double LensScale = 1.0;

        public string NameMode = "hover";

        public string HoverColor = "#FFFFFF";
        public double HoverOpacity = 0.55;
        public string HoverBorderColor = "#FFFFFF";
        public double HoverBorderWidth = 1.5;

        public double IconRadiusRatio = 0.22;

        public double SettingsOpacity = 0.88;

        public double TriggerWidth = 4;

        public double PeekWidth = 4;

        public int AnimMs = 800;
        public int CollapseDelayMs = 320;
        public bool AutoStart = false;

        // 名称提示是否显示由「名称显示」派生：off 时悬停提示没有意义。
        // 旧配置里的 behavior.showtooltip 仍会被读取，仅用于推导 NameMode。
        public bool ShowToolTip
        {
            get { return !string.Equals(NameMode, "off", StringComparison.Ordinal); }
        }

        public bool HideDesktopIcons = false;

        public List<string> Ignored = new List<string>();

        public List<string> NoRound = new List<string>();

        public List<string> CustomIcons = new List<string>();

        public List<string> IconScales = new List<string>();

        public int AnimFps = 120;

        public string Easing = "smooth";

        public bool DragReorder = true;

        public static int CountEmpty(List<DockItem> list)
        {
            if (list == null) { return 0; }
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].IsEmpty) { n++; }
            }
            return n;
        }

        public static void TrimTrailingEmpty(List<DockItem> list)
        {
            if (list == null) { return; }
            while (list.Count > 0 && list[list.Count - 1] != null && list[list.Count - 1].IsEmpty)
            {
                list.RemoveAt(list.Count - 1);
            }
        }

        public List<DockItem> Left = new List<DockItem>();
        public List<DockItem> Right = new List<DockItem>();

        public static int[] GridFor(int slots, double slotH, double spacing, double availContentH)
        {
            bool heightLimited;
            return GridFor(slots, slotH, spacing, availContentH, out heightLimited);
        }

        public static int[] GridFor(int slots, double slotH, double spacing,
            double availContentH, out bool heightLimited)
        {
            heightLimited = false;
            if (slots <= 0) { return new int[] { 1, 0 }; }
            double step = slotH + spacing;
            if (step <= 0.0001) { step = 1; }
            int maxRows;
            if (availContentH <= 0) { maxRows = slots; }
            else { maxRows = (int)Math.Floor((availContentH + spacing) / step); }
            if (maxRows < 1) { maxRows = 1; heightLimited = true; }
            int rows = Math.Min(slots, maxRows);
            if (rows < slots) { heightLimited = true; }
            int cols = (int)Math.Ceiling((double)slots / rows);
            return new int[] { cols, rows };
        }

        // 原地覆盖：所有“换配置”的路径都走这里，保证 App 永远持有同一个实例，
        // 各个窗口缓存的引用不会变成脱节的旧副本。
        public void CopyFrom(DockConfig s)
        {
            if (s == null || object.ReferenceEquals(s, this)) { return; }
            IconSize = s.IconSize; IconSpacing = s.IconSpacing; IconPad = s.IconPad;
            PanelStyle = s.PanelStyle; LensScale = s.LensScale; NameMode = s.NameMode;
            HoverColor = s.HoverColor; HoverOpacity = s.HoverOpacity;
            HoverBorderColor = s.HoverBorderColor; HoverBorderWidth = s.HoverBorderWidth;
            IconRadiusRatio = s.IconRadiusRatio; SettingsOpacity = s.SettingsOpacity;
            TriggerWidth = s.TriggerWidth; PeekWidth = s.PeekWidth;
            AnimMs = s.AnimMs; CollapseDelayMs = s.CollapseDelayMs; AutoStart = s.AutoStart;
            HideDesktopIcons = s.HideDesktopIcons;
            AnimFps = s.AnimFps; Easing = s.Easing; DragReorder = s.DragReorder;

            Ignored.Clear(); Ignored.AddRange(s.Ignored);
            NoRound.Clear(); NoRound.AddRange(s.NoRound);
            CustomIcons.Clear(); CustomIcons.AddRange(s.CustomIcons);
            IconScales.Clear(); IconScales.AddRange(s.IconScales);
            Left.Clear(); foreach (DockItem i in s.Left) { Left.Add(CloneItem(i)); }
            Right.Clear(); foreach (DockItem i in s.Right) { Right.Add(CloneItem(i)); }
        }

        public DockConfig Clone()
        {
            DockConfig c = new DockConfig();
            c.CopyFrom(this);
            return c;
        }

        private static DockItem CloneItem(DockItem i)
        {
            DockItem n = new DockItem(i.Path, i.Name);
            n.IsEmpty = i.IsEmpty;
            return n;
        }

        public void ResetToDefaults()
        {
            DockConfig d = new DockConfig();

            IconSize = d.IconSize;
            IconSpacing = d.IconSpacing;
            IconPad = d.IconPad;
            IconRadiusRatio = d.IconRadiusRatio;
            HoverOpacity = d.HoverOpacity;
            HoverBorderWidth = d.HoverBorderWidth;
            TriggerWidth = d.TriggerWidth;
            PeekWidth = d.PeekWidth;
            LensScale = d.LensScale;
            SettingsOpacity = d.SettingsOpacity;
            AnimMs = d.AnimMs;
            CollapseDelayMs = d.CollapseDelayMs;
            AnimFps = d.AnimFps;

            // 选择性质的设置也回默认值；这些是用户能一眼看出来的「风格」，不跟着恢复会让人以为没生效。
            PanelStyle = d.PanelStyle;
            NameMode = d.NameMode;
            HoverColor = d.HoverColor;
            HoverBorderColor = d.HoverBorderColor;
            Easing = d.Easing;
            DragReorder = d.DragReorder;
        }
    }
}

namespace SideStack
{
    // 进程启动以来的毫秒数；必须是 long，int 毫秒 24.8 天就会溢出。
    public static class Clock
    {
        private static readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        public static long NowMs()
        {
            return sw.ElapsedMilliseconds;
        }
    }
}
