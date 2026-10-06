using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using WinForms = System.Windows.Forms;

namespace SideStack
{
    public class TrayIcon : IDisposable
    {
        public event Action SettingsRequested;
        public event Action TogglePanelRequested;
        public event Action OpenConfigFolderRequested;
        public event Action ExitRequested;

        public event Action CaptureRequested;

        private WinForms.NotifyIcon notify;
        private WinForms.ToolStripMenuItem miToggle;
        private bool disposed;

        public TrayIcon()
        {
            notify = new WinForms.NotifyIcon();

            Icon ico = LoadIcon();
            if (ico != null) { notify.Icon = ico; }
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

            WinForms.ToolStripMenuItem miCapture = new WinForms.ToolStripMenuItem("允许截图侧边栏（60 秒）");
            miCapture.Click += delegate { Raise(CaptureRequested); };
            menu.Items.Add(miCapture);

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

        private static Icon LoadIcon()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir))
                {
                    string ico = Path.Combine(dir, "app.ico");
                    if (File.Exists(ico)) { return new Icon(ico, new Size(32, 32)); }
                }
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
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
                    notify.Dispose();
                    notify = null;
                }
            }
            catch { }
        }
    }
}
