using System;
using System.Collections.Generic;

namespace SideStack
{
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

        public bool UiRefraction = false;
        public double TriggerWidth = 4;

        public double PeekWidth = 4;

        public int AnimMs = 800;
        public int CollapseDelayMs = 320;
        public bool AutoStart = false;
        public bool ShowToolTip = true;

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

        public static int[] GridFor(int slots, double slotW, double slotH, double spacing, double availContentH)
        {
            if (slots <= 0) { return new int[] { 1, 0 }; }
            double step = slotH + spacing;
            int maxRows;
            if (availContentH <= 0) { maxRows = slots; }
            else { maxRows = (int)Math.Floor((availContentH + spacing) / step); }
            if (maxRows < 1) { maxRows = 1; }
            int rows = Math.Min(slots, maxRows);
            int cols = (int)Math.Ceiling((double)slots / rows);
            return new int[] { cols, rows };
        }

        public DockConfig Clone()
        {
            DockConfig c = new DockConfig();
            c.IconSize = IconSize; c.IconSpacing = IconSpacing; c.IconPad = IconPad;
            c.IconRadiusRatio = IconRadiusRatio;
            c.PanelStyle = PanelStyle; c.NameMode = NameMode;
            c.HoverColor = HoverColor; c.HoverOpacity = HoverOpacity;
            c.HoverBorderColor = HoverBorderColor; c.HoverBorderWidth = HoverBorderWidth;
            c.TriggerWidth = TriggerWidth; c.AnimMs = AnimMs;
            c.CollapseDelayMs = CollapseDelayMs; c.AutoStart = AutoStart;
            c.ShowToolTip = ShowToolTip;
            c.AnimFps = AnimFps; c.Easing = Easing; c.DragReorder = DragReorder;
            c.HideDesktopIcons = HideDesktopIcons;

            c.LensScale = LensScale; c.PeekWidth = PeekWidth; c.SettingsOpacity = SettingsOpacity; c.UiRefraction = UiRefraction;
            foreach (string p in Ignored) { c.Ignored.Add(p); }
            foreach (string p in NoRound) { c.NoRound.Add(p); }
            foreach (string p in CustomIcons) { c.CustomIcons.Add(p); }
            foreach (string p in IconScales) { c.IconScales.Add(p); }
            foreach (DockItem i in Left) { c.Left.Add(CloneItem(i)); }
            foreach (DockItem i in Right) { c.Right.Add(CloneItem(i)); }
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
        }
    }
}

namespace SideStack
{
    public static class Clock
    {
        private static readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        public static int NowMs()
        {
            return (int)sw.Elapsed.TotalMilliseconds;
        }
    }
}
