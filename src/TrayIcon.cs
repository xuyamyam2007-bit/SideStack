using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using WinForms = System.Windows.Forms;

namespace SideStack
{
    // 系统托盘图标与右键菜单。
    public class TrayIcon : IDisposable
    {
        public event Action SettingsRequested;
        public event Action TogglePanelRequested;
        public event Action OpenConfigFolderRequested;
        public event Action ExitRequested;

        public event Action RecheckRequested;

        private WinForms.NotifyIcon notify;
        private WinForms.ToolStripMenuItem miToggle;
        private bool trayIconOwned;
        private bool disposed;

        public TrayIcon()
        {
            notify = new WinForms.NotifyIcon();

            bool iconOwned;
            Icon ico = LoadIcon(out iconOwned);
            if (ico != null) { notify.Icon = ico; }
            trayIconOwned = iconOwned;
            notify.Text = "SideStack";
            notify.Visible = true;

            notify.MouseClick += delegate(object s, WinForms.MouseEventArgs e)
            {
                if (e.Button == WinForms.MouseButtons.Left) { Raise(SettingsRequested); }
            };
            notify.DoubleClick += delegate { Raise(SettingsRequested); };

            WinForms.ContextMenuStrip menu = new WinForms.ContextMenuStrip();
            menu.ShowImageMargin = false;

            WinForms.ToolStripMenuItem miSettings = new WinForms.ToolStripMenuItem("打开设置");
            miSettings.Click += delegate { Raise(SettingsRequested); };
            menu.Items.Add(miSettings);

            miToggle = new WinForms.ToolStripMenuItem("显示面板");
            miToggle.Click += delegate { Raise(TogglePanelRequested); };
            menu.Items.Add(miToggle);

            WinForms.ToolStripMenuItem miRecheck = new WinForms.ToolStripMenuItem("重新检测并唤出面板");
            miRecheck.Click += delegate { Raise(RecheckRequested); };
            menu.Items.Add(miRecheck);

            WinForms.ToolStripMenuItem miConfig = new WinForms.ToolStripMenuItem("打开配置目录");
            miConfig.Click += delegate { Raise(OpenConfigFolderRequested); };
            menu.Items.Add(miConfig);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            WinForms.ToolStripMenuItem miExit = new WinForms.ToolStripMenuItem("退出 SideStack");
            miExit.Click += delegate { Raise(ExitRequested); };
            menu.Items.Add(miExit);

            notify.ContextMenuStrip = menu;
        }

        public void SetPanelPinned(bool pinned)
        {
            if (miToggle != null) { miToggle.Text = pinned ? "隐藏面板" : "显示面板"; }
        }

        public void ShowBalloon(string title, string text)
        {
            try
            {
                notify.ShowBalloonTip(2500, title, text, WinForms.ToolTipIcon.None);
            }
            catch { }
        }

        private static Icon LoadIcon(out bool owned)
        {
            owned = false;
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir))
                {
                    string ico = Path.Combine(dir, "app.ico");
                    if (File.Exists(ico)) { owned = true; return new Icon(ico, new Size(32, 32)); }
                }
            }
            catch { }
            try { Icon e = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); if (e != null) { owned = true; } return e; }
            catch { }
            try { return SystemIcons.Application; }
            catch { }
            return null;
        }

        private static void Raise(Action a)
        {
            if (a == null) { return; }
            try { a(); }
            catch (Exception ex) { Logger.Write("TrayIcon.Raise", ex); }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            try
            {
                if (notify != null)
                {
                    notify.Visible = false;
                    // 自己从 app.ico / exe 资源里 new 出来的 Icon 要释放；SystemIcons.Application 是共享静态对象，不能动。
                    if (trayIconOwned && notify.Icon != null) { notify.Icon.Dispose(); }
                    notify.Icon = null;
                    notify.Dispose();
                    notify = null;
                }
            }
            catch { }
        }
    }
}
