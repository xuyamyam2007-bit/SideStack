using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;

namespace SideStack
{
    public static class ConfigStore
    {
        public static string FilePath = "";

        public static readonly string[] TrackedExtensions = new string[]
        {
            ".lnk", ".url", ".exe", ".appref-ms", ".bat", ".cmd",

            ".txt", ".md", ".pdf", ".doc", ".docx", ".rtf", ".odt",
            ".xls", ".xlsx", ".csv", ".ppt", ".pptx",

            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico",
            ".zip", ".7z", ".rar",
        };

        public static bool IsTrackedExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) { return false; }
            ext = ext.ToLowerInvariant();
            for (int i = 0; i < TrackedExtensions.Length; i++)
            {
                if (TrackedExtensions[i] == ext) { return true; }
            }
            return false;
        }

        public static bool IsTrackedDesktopFile(string path)
        {
            if (string.IsNullOrEmpty(path)) { return false; }
            return IsTrackedExtension(Path.GetExtension(path));
        }

        private const string K_ICON_SIZE = "icon.size";
        private const string K_ICON_SPACING = "icon.spacing";
        private const string K_ICON_PAD = "icon.pad";
        private const string K_ICON_RADIUS = "icon.radiusratio";
        private const string K_PANEL_STYLE = "panel.style";
        private const string K_LENS_SCALE = "panel.lens.scale";
        private const string K_EASING = "behavior.easing";
        private const string K_NAME_MODE = "icon.namemode";
        private const string K_HOVER_COLOR = "hover.color";
        private const string K_HOVER_OPACITY = "hover.opacity";
        private const string K_HOVER_BORDER = "hover.border";
        private const string K_HOVER_BORDER_W = "hover.borderwidth";
        private const string K_TRIGGER = "behavior.triggerwidth";
        private const string K_PEEK = "behavior.peekwidth";
        private const string K_UI_OPACITY = "ui.settingsopacity";
        private const string K_UI_REFRACT = "ui.refraction";
        private const string K_ANIM = "behavior.animms";
        private const string K_COLLAPSE_DELAY = "behavior.collapsedelayms";
        private const string K_AUTOSTART = "behavior.autostart";
        private const string K_TOOLTIP = "behavior.showtooltip";
        private const string K_ANIM_FPS = "behavior.animfps";
        private const string K_DRAG = "behavior.dragreorder";
        private const string K_HIDE_DESKTOP_ICONS = "behavior.hidedesktopicons";
        private const string K_IGNORE = "ignore.item";
        private const string K_NOROUND = "noround.item";
        private const string K_ICON_CUSTOM = "icon.custom";
        private const string K_ICON_SCALE = "icon.scale";
        private const string K_ITEM_L = "left.item";
        private const string K_ITEM_R = "right.item";
        private const string K_EMPTY_L = "left.empty";
        private const string K_EMPTY_R = "right.empty";
        private const string K_GAP_L_LEGACY = "left.gap";
        private const string K_GAP_R_LEGACY = "right.gap";

        private static void AddPair(List<string> list, string v)
        {
            if (list == null || string.IsNullOrEmpty(v)) { return; }
            int i = v.IndexOf('|');
            if (i <= 0 || i >= v.Length - 1) { return; }
            string key = v.Substring(0, i).Trim();
            string val = v.Substring(i + 1).Trim();
            if (key.Length == 0 || val.Length == 0) { return; }
            for (int k = 0; k < list.Count; k++)
            {
                int j = list[k].IndexOf('|');
                if (j > 0 && string.Equals(list[k].Substring(0, j), key, StringComparison.OrdinalIgnoreCase))
                {
                    list[k] = key + "|" + val;
                    return;
                }
            }
            list.Add(key + "|" + val);
        }

        public static DockConfig Load()
        {
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                return CreateDefault();
            }

            DockConfig c = new DockConfig();
            List<DockItem> left = new List<DockItem>();
            List<DockItem> right = new List<DockItem>();
            bool nameModeExplicit = false;

            try
            {
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) { continue; }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) { continue; }

                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();

                    if (k == K_ICON_SIZE) { c.IconSize = D(v, c.IconSize); }
                    else if (k == K_ICON_SPACING) { c.IconSpacing = D(v, c.IconSpacing); }
                    else if (k == K_ICON_PAD) { c.IconPad = D(v, c.IconPad); }
                    else if (k == K_ICON_RADIUS) { c.IconRadiusRatio = D(v, c.IconRadiusRatio); }
                    else if (k == K_PANEL_STYLE) { c.PanelStyle = v; }
            else if (k == K_LENS_SCALE) { double d; if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { c.LensScale = d; } }
            else if (k == K_EASING) { c.Easing = v; }
                    else if (k == K_NAME_MODE) { c.NameMode = v; nameModeExplicit = true; }
                    else if (k == K_HOVER_COLOR) { c.HoverColor = v; }
                    else if (k == K_HOVER_OPACITY) { c.HoverOpacity = D(v, c.HoverOpacity); }
                    else if (k == K_HOVER_BORDER) { c.HoverBorderColor = v; }
                    else if (k == K_HOVER_BORDER_W) { c.HoverBorderWidth = D(v, c.HoverBorderWidth); }
                    else if (k == K_TRIGGER) { c.TriggerWidth = D(v, c.TriggerWidth); }
            else if (k == K_PEEK) { c.PeekWidth = D(v, c.PeekWidth); }
            else if (k == K_UI_OPACITY) { c.SettingsOpacity = D(v, c.SettingsOpacity); }
            else if (k == K_UI_REFRACT) { c.UiRefraction = (v == "1" || v.ToLower() == "true"); }
                    else if (k == K_ANIM) { c.AnimMs = (int)D(v, c.AnimMs); }
                    else if (k == K_COLLAPSE_DELAY) { c.CollapseDelayMs = (int)D(v, c.CollapseDelayMs); }
                    else if (k == K_AUTOSTART) { c.AutoStart = (v == "1" || v.ToLower() == "true"); }
                    else if (k == K_TOOLTIP) { c.ShowToolTip = (v == "1" || v.ToLower() == "true"); }
                    else if (k == K_ANIM_FPS) { c.AnimFps = (int)D(v, c.AnimFps); }
                    else if (k == K_DRAG) { c.DragReorder = (v == "1" || v.ToLower() == "true"); }
                    else if (k == K_HIDE_DESKTOP_ICONS) { c.HideDesktopIcons = (v == "1" || v.ToLower() == "true"); }
                    else if (k == K_IGNORE)
                    {
                        if (v.Length > 0 && !ContainsCI(c.Ignored, v)) { c.Ignored.Add(v); }
                    }
                    else if (k == K_NOROUND)
                    {
                        if (v.Length > 0 && !ContainsCI(c.NoRound, v)) { c.NoRound.Add(v); }
                    }
                    else if (k == K_ICON_CUSTOM) { AddPair(c.CustomIcons, v); }
                    else if (k == K_ICON_SCALE) { AddPair(c.IconScales, v); }
                    else if (k == K_ITEM_L) { AddItem(left, v, false); }
                    else if (k == K_ITEM_R) { AddItem(right, v, false); }
                    else if (k == K_EMPTY_L || k == K_GAP_L_LEGACY) { AddItem(left, v, true); }
                    else if (k == K_EMPTY_R || k == K_GAP_R_LEGACY) { AddItem(right, v, true); }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("ConfigStore.Load", ex);
            }

            if (!nameModeExplicit) { c.NameMode = c.ShowToolTip ? "hover" : "off"; }

            c.Left = left;
            c.Right = right;
            Clamp(c);
            return c;
        }

        public static void Save(DockConfig c)
        {
            if (string.IsNullOrEmpty(FilePath)) { return; }
            try
            {
                BackupCurrent();
                Clamp(c);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# SideStack 配置文件 —— 可用记事本直接编辑，保存后右键 Dock 面板选择\"重新加载\"");
                sb.AppendLine("# 颜色格式: #RRGGBB 或 #AARRGGBB");
                sb.AppendLine();
                sb.AppendLine("# ===== 图标 =====");
                sb.AppendLine(K_ICON_SIZE + "=" + N(c.IconSize));
                sb.AppendLine(K_ICON_SPACING + "=" + N(c.IconSpacing));
                sb.AppendLine(K_ICON_PAD + "=" + N(c.IconPad));
                sb.AppendLine("# icon.radiusratio: 图标圆角比例（占图标短边，0 ~ 0.5；0 = 不圆角）");
                sb.AppendLine("#   只作用在图标自身不透明的像素上，非方形图标（文档形 / 圆形）不受影响");
                sb.AppendLine(K_ICON_RADIUS + "=" + N(c.IconRadiusRatio));
                sb.AppendLine("# panel.style: 面板风格 glass=经典玻璃 / clear=透亮（高透+轮廓高光+边缘色散） / lens=光感（液态玻璃放大镜：折射+色散，背景取面板背后的屏幕）");
                sb.AppendLine(K_PANEL_STYLE + "=" + c.PanelStyle);
            sb.AppendLine("# panel.lens.scale: 《光感》玻璃清晰度（内部渲染分辨率 0.5~1.0）。1.0=满分辨率最锐（默认）；调小更省 CPU，但玻璃会明显变柔");
            sb.AppendLine(K_LENS_SCALE + "=" + c.LensScale.ToString("F2", CultureInfo.InvariantCulture));
            sb.AppendLine("# easing: 展开/收起的缓动曲线 smooth=平滑（两端都慢，位移全程可见）/ linear=线性 / snap=干脆（旧行为：前 1/5 时间走完一半位移，看着像瞬间弹出）");
            sb.AppendLine(K_EASING + "=" + c.Easing);
                sb.AppendLine("# icon.namemode: 名称显示 off=关闭 / hover=悬停 / always=常显");
                sb.AppendLine(K_NAME_MODE + "=" + c.NameMode);
                sb.AppendLine();
                sb.AppendLine("# ===== 鼠标悬停高亮 =====");
                sb.AppendLine(K_HOVER_COLOR + "=" + c.HoverColor);
                sb.AppendLine(K_HOVER_OPACITY + "=" + N(c.HoverOpacity));
                sb.AppendLine(K_HOVER_BORDER + "=" + c.HoverBorderColor);
                sb.AppendLine(K_HOVER_BORDER_W + "=" + N(c.HoverBorderWidth));
                sb.AppendLine();
                sb.AppendLine("# ===== 行为 =====");
                sb.AppendLine("# behavior.peekwidth: 收起状态在屏幕边缘露出的宽度（DIP）。0 = 完全收进屏幕、边缘看不到任何东西");
            sb.AppendLine("#                    露不露出与「能不能唤出」无关 —— 鼠标贴到屏幕边缘（宽度见 triggerwidth）就会滑出。");
            sb.AppendLine(K_PEEK + "=" + N(c.PeekWidth));
            sb.AppendLine("# ui.settingsopacity: 设置界面自身的不透明度（0.35~1.0）。只影响窗口玻璃背景的透明程度，");
            sb.AppendLine("#                    文字与控件始终不透明。1.00 = 几乎完全遮住背后（默认 0.88）。");
            sb.AppendLine(K_UI_OPACITY + "=" + N(c.SettingsOpacity));
            sb.AppendLine("# ui.refraction: 设置界面各区域（窗口外壳 + 每张卡片）的边缘折射，1 = 开（默认）/ 0 = 关。");
            sb.AppendLine("#                开启时会在抓屏的一瞬间把该窗口排除出抓屏（避免自反馈），不影响截图与屏幕显示。");
            sb.AppendLine(K_UI_REFRACT + "=" + (c.UiRefraction ? "1" : "0"));
            sb.AppendLine(K_TRIGGER + "=" + N(c.TriggerWidth));
                sb.AppendLine(K_ANIM + "=" + c.AnimMs.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(K_COLLAPSE_DELAY + "=" + c.CollapseDelayMs.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(K_AUTOSTART + "=" + (c.AutoStart ? "1" : "0"));
                sb.AppendLine(K_TOOLTIP + "=" + (c.ShowToolTip ? "1" : "0"));
                sb.AppendLine();
                sb.AppendLine("# ===== 动画与交互 =====");
                sb.AppendLine("# animfps: 面板滑入/滑出动画的刷新率（FPS），取值 1 ~ 显示器最高刷新率，超出上限按上限处理；");
            sb.AppendLine("#          《光感》的实时玻璃背景也按这个帧率刷新（同一个值，不再单独设置）");
                sb.AppendLine(K_ANIM_FPS + "=" + c.AnimFps.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("# dragreorder: 是否允许在 Dock 栏内拖动图标（1/0）；拖到另一个图标上时仅这两个图标互换位置");
                sb.AppendLine(K_DRAG + "=" + (c.DragReorder ? "1" : "0"));
                sb.AppendLine("# hidedesktopicons: 1=隐藏系统桌面图标（收纳模式）；退出 SideStack 时自动恢复显示");
                sb.AppendLine(K_HIDE_DESKTOP_ICONS + "=" + (c.HideDesktopIcons ? "1" : "0"));
                sb.AppendLine();
                sb.AppendLine("# ===== 已从 Dock 移出的桌面图标（实时同步时不再自动加回）=====");
                foreach (string p in c.Ignored) { sb.AppendLine(K_IGNORE + "=" + p); }
                sb.AppendLine();
                sb.AppendLine("# ===== 不修改圆角的图标（右键图标 →「不修改圆角」；不受「恢复默认」影响）=====");
                foreach (string p in c.NoRound) { sb.AppendLine(K_NOROUND + "=" + p); }
                  sb.AppendLine();
                  sb.AppendLine("# ===== 单个图标的自定义图片与缩放（右键图标 →「自定义图标…」/「图标缩放」；不受「恢复默认」影响）=====");
                  foreach (string p in c.CustomIcons) { sb.AppendLine(K_ICON_CUSTOM + "=" + p); }
                  foreach (string p in c.IconScales) { sb.AppendLine(K_ICON_SCALE + "=" + p); }
                sb.AppendLine();
                sb.AppendLine("# ===== 图标分组：格式 绝对路径|显示名；left.empty / right.empty 表示该位置是空白位置（可落下图标，使图标之间不必紧挨）=====");
                foreach (DockItem it in c.Left)
                {
                    sb.AppendLine((it.IsEmpty ? K_EMPTY_L : K_ITEM_L) + "=" + it.Path + "|" + it.Name);
                }
                foreach (DockItem it in c.Right)
                {
                    sb.AppendLine((it.IsEmpty ? K_EMPTY_R : K_ITEM_R) + "=" + it.Path + "|" + it.Name);
                }

                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
                lastSavedText = sb.ToString();
            }
            catch (Exception ex)
            {
                Logger.Write("ConfigStore.Save", ex);
            }
        }

        private const int BackupKeep = 12;
        private static string lastSavedText;
        private static bool backupReminderLogged;

        private static void BackupCurrent()
        {
            try
            {
                if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath)) { return; }

                string cur = File.ReadAllText(FilePath);
                if (cur == lastSavedText) { return; }
                string dir = Path.GetDirectoryName(FilePath);
                if (string.IsNullOrEmpty(dir)) { return; }
                string bakDir = Path.Combine(dir, "backups");
                Directory.CreateDirectory(bakDir);

                string name = "config-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                string dst = Path.Combine(bakDir, name);
                if (!File.Exists(dst)) { File.Copy(FilePath, dst, true); }

                FileInfo[] all = new DirectoryInfo(bakDir).GetFiles("config-*.txt");
                if (all.Length > BackupKeep)
                {
                    Array.Sort(all, delegate (FileInfo a, FileInfo b)
                    {
                        return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
                    });
                    for (int i = BackupKeep; i < all.Length; i++)
                    {
                        try { all[i].Delete(); } catch { }
                    }
                }

                if (!backupReminderLogged)
                {
                    backupReminderLogged = true;
                    Logger.Write("配置历史备份目录: " + bakDir + "（最多保留 " + BackupKeep + " 份）");
                }
            }
            catch (Exception ex)
            {
                Logger.Write("ConfigStore.BackupCurrent", ex);
            }
        }

        private static void AddItem(List<DockItem> list, string v, bool empty)
        {
            if (empty) { list.Add(DockItem.MakeEmpty()); return; }
            if (string.IsNullOrEmpty(v)) { return; }
            int bar = v.LastIndexOf('|');
            string p, n;
            if (bar > 0)
            {
                p = v.Substring(0, bar).Trim();
                n = v.Substring(bar + 1).Trim();
            }
            else
            {
                p = v.Trim();
                n = "";
            }
            if (p.Length == 0) { return; }
            if (n.Length == 0) { n = System.IO.Path.GetFileNameWithoutExtension(p); }
            list.Add(new DockItem(p, n));
        }

        public static DockConfig CreateDefault()
        {
            DockConfig c = new DockConfig();
            List<DockItem> all = ScanDesktop();
            for (int i = 0; i < all.Count; i++)
            {
                if (i % 2 == 0) { c.Left.Add(all[i]); }
                else { c.Right.Add(all[i]); }
            }
            Logger.Write("CreateDefault: 桌面共扫描到 " + all.Count + " 个快捷方式, 左=" + c.Left.Count + " 右=" + c.Right.Count);
            return c;
        }

        public static List<DockItem> ScanDesktop()
        {
            List<DockItem> result = new List<DockItem>();
            List<string> dirs = new List<string>();
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)); } catch { }

            List<string> seen = new List<string>();
            foreach (string d in dirs)
            {
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) { continue; }
                try
                {
                    string[] files = Directory.GetFiles(d);
                    foreach (string f in files)
                    {
                        if (!IsTrackedDesktopFile(f)) { continue; }
                        string name = System.IO.Path.GetFileNameWithoutExtension(f);

                        string key = System.IO.Path.GetFileName(f);
                        if (seen.Contains(key)) { continue; }
                        seen.Add(key);
                        result.Add(new DockItem(f, name));
                    }
                }
                catch (Exception ex) { Logger.Write("ScanDesktop(" + d + ")", ex); }
            }

            result.Sort(delegate (DockItem a, DockItem b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        private static void Clamp(DockConfig c)
        {
            c.IconSize = ClampD(c.IconSize, 20, 96);
            c.IconSpacing = ClampD(c.IconSpacing, 0, 40);
            c.IconPad = ClampD(c.IconPad, 0, 20);
            c.IconRadiusRatio = ClampD(c.IconRadiusRatio, 0, 0.5);
            if (c.PanelStyle != "clear" && c.PanelStyle != "lens" && c.PanelStyle != "lensstrong") { c.PanelStyle = "glass"; }
            c.LensScale = ClampD(c.LensScale, 0.5, 1.0);
            if (c.Easing != "linear" && c.Easing != "snap") { c.Easing = "smooth"; }
            if (c.NameMode != "off" && c.NameMode != "hover" && c.NameMode != "always") { c.NameMode = "hover"; }
            c.HoverOpacity = ClampD(c.HoverOpacity, 0.0, 1.0);
            c.HoverBorderWidth = ClampD(c.HoverBorderWidth, 0, 6);
            c.TriggerWidth = ClampD(c.TriggerWidth, 1, 24);
            c.PeekWidth = ClampD(c.PeekWidth, 0, 24);
            c.SettingsOpacity = ClampD(c.SettingsOpacity, 0.35, 1.0);
            if (c.AnimMs < 0) { c.AnimMs = 0; }
            if (c.AnimMs > 1000) { c.AnimMs = 1000; }
            if (c.CollapseDelayMs < 0) { c.CollapseDelayMs = 0; }
            if (c.CollapseDelayMs > 3000) { c.CollapseDelayMs = 3000; }

            if (c.AnimFps < 1) { c.AnimFps = 1; }
            if (c.AnimFps > 240) { c.AnimFps = 240; }
            if (c.HoverColor == null || c.HoverColor.Length == 0) { c.HoverColor = "#FFFFFF"; }
            if (c.HoverBorderColor == null || c.HoverBorderColor.Length == 0) { c.HoverBorderColor = "#FFFFFF"; }
            if (c.Left == null) { c.Left = new List<DockItem>(); }
            if (c.Right == null) { c.Right = new List<DockItem>(); }
            if (c.Ignored == null) { c.Ignored = new List<string>(); }
            if (c.NoRound == null) { c.NoRound = new List<string>(); }
            if (c.CustomIcons == null) { c.CustomIcons = new List<string>(); }
            if (c.IconScales == null) { c.IconScales = new List<string>(); }

            c.ShowToolTip = (c.NameMode != "off");
        }

        public static bool ContainsCI(List<string> list, string v)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], v, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private static double ClampD(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) { return lo; }
            if (v < lo) { return lo; }
            if (v > hi) { return hi; }
            return v;
        }

        private static double D(string s, double def)
        {
            double r;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) { return r; }
            return def;
        }

        private static string N(double v)
        {
            return v.ToString("0.0##", CultureInfo.InvariantCulture);
        }
    }

    public class DesktopWatcher : IDisposable
    {
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly Dispatcher dispatcher;
        private readonly Action onChanged;
        private DispatcherTimer timer;

        public DesktopWatcher(Dispatcher dispatcher, Action onChanged)
        {
            this.dispatcher = dispatcher;
            this.onChanged = onChanged;
        }

        public void Start()
        {
            List<string> dirs = new List<string>();
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)); } catch { }

            foreach (string d in dirs)
            {
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) { continue; }
                try
                {
                    FileSystemWatcher w = new FileSystemWatcher(d);
                    w.NotifyFilter = NotifyFilters.FileName;
                    w.Created += OnFs;
                    w.Deleted += OnFs;
                    w.Renamed += OnFs;
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception ex) { Logger.Write("DesktopWatcher(" + d + ")", ex); }
            }

            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(450);
            timer.Tick += delegate
            {
                timer.Stop();
                try { if (onChanged != null) { onChanged(); } }
                catch (Exception ex) { Logger.Write("DesktopWatcher.onChanged", ex); }
            };
            Logger.Write("桌面实时监控已启动（监控 " + watchers.Count + " 个目录）");
        }

        private void OnFs(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (e == null || string.IsNullOrEmpty(e.Name)) { return; }

                if (!ConfigStore.IsTrackedDesktopFile(e.Name)) { return; }
                dispatcher.BeginInvoke(new Action(Arm));
            }
            catch { }
        }

        private void Arm()
        {
            try { if (timer != null) { timer.Stop(); timer.Start(); } }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (timer != null) { timer.Stop(); }
                foreach (FileSystemWatcher w in watchers)
                {
                    try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
                }
                watchers.Clear();
            }
            catch { }
        }
    }
}
