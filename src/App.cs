using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SideStack
{
    public class DockApp : Application
    {
        private DockConfig cfg;
        private DockWindow left;
        private DockWindow right;

        private DispatcherTimer timer;
        private System.Threading.Thread animThread;
        private volatile bool animThreadRun;
        private volatile bool animTickPending;
        private volatile int animThreadPeriodUs = 16000;
        private int lastCoverCheck;
        private bool desktopCovered;
        private bool quitting;

        private static int NowMs()
        {
            return Clock.NowMs();
        }
        private bool layoutHooked;
        private SettingsWindow settingsWin;
        private string exePath = "";

        private TrayIcon tray;
        private bool panelPinned;

        private DesktopWatcher desktopWatcher;

        private bool desktopIconsHiddenByUs;

        private static void MigrateLegacyData()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string legacy = Path.Combine(appData, "DeskDock");
                if (!Directory.Exists(legacy)) { return; }
                string target = Path.Combine(appData, "SideStack");
                Directory.CreateDirectory(target);

                foreach (string f in Directory.GetFiles(legacy, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(legacy.Length).TrimStart('\\');
                    string dst = Path.Combine(target, rel);
                    if (File.Exists(dst)) { continue; }
                    string dstDir = Path.GetDirectoryName(dst);
                    if (!Directory.Exists(dstDir)) { Directory.CreateDirectory(dstDir); }
                    File.Move(f, dst);
                }
                if (Directory.GetFiles(legacy, "*", SearchOption.AllDirectories).Length == 0)
                {
                    Directory.Delete(legacy, true);
                }
            }
            catch { }
        }

        [STAThread]
        public static void Main()
        {
            try { NativeMethods.SetProcessDpiAwareness(2); }
            catch
            {
                try { NativeMethods.SetProcessDpiAwareness(1); }
                catch { }
            }

            MigrateLegacyData();

            if (AutoRebuild.TryUpdateBeforeStart())
            {
                return;
            }

            bool created;
            Mutex mutex = new Mutex(true, "SideStack_SingleInstance_Mutex_7C41", out created);
            if (!created)
            {
                MessageBox.Show("SideStack 已经在运行了。", "SideStack",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DockApp app = new DockApp();
            try
            {
                app.Run();
            }
            catch (Exception ex)
            {
                Logger.Write("Main", ex);
                MessageBox.Show(ex.ToString(), "SideStack 运行异常");
            }
            finally
            {
                GC.KeepAlive(mutex);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try { exePath = Process.GetCurrentProcess().MainModule.FileName; }
            catch
            {
                try
                {
                    Assembly asm = Assembly.GetEntryAssembly();
                    exePath = (asm != null) ? asm.Location : "";
                }
                catch { }
            }

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SideStack");
            try { Directory.CreateDirectory(dir); } catch { }

            Logger.Init(Path.Combine(dir, "sidestack.log"));
            Logger.Write("================ SideStack 启动 ================");
            Logger.Write("exe = " + exePath);

            ShellUtils.MigrateLegacyAutoStart(exePath);
            ConfigStore.FilePath = Path.Combine(dir, "config.txt");

            bool exists = File.Exists(ConfigStore.FilePath);
            cfg = ConfigStore.Load();
            IconLoader.RadiusRatio = cfg.IconRadiusRatio;
            IconLoader.SetNoRound(cfg.NoRound);
            if (!exists)
            {
                ConfigStore.Save(cfg);
                Logger.Write("已生成默认配置: " + ConfigStore.FilePath);
            }

            left = new DockWindow(true, cfg);
            right = new DockWindow(false, cfg);
            Hook(left);
            Hook(right);
            RegisterAppIdentity();

            left.FilesDropped += delegate (DockWindow dw, System.Collections.Generic.List<string> paths, int idx) { AddDroppedFiles(dw, paths, idx); };
            right.FilesDropped += delegate (DockWindow dw, System.Collections.Generic.List<string> paths, int idx) { AddDroppedFiles(dw, paths, idx); };

            left.CustomIconRequested += delegate (DockWindow dw, DockItem it) { PickCustomIcon(it); };
            right.CustomIconRequested += delegate (DockWindow dw, DockItem it) { PickCustomIcon(it); };
            left.CustomIconCleared += delegate (DockWindow dw, DockItem it) { SetPair(cfg.CustomIcons, it, null); };
            right.CustomIconCleared += delegate (DockWindow dw, DockItem it) { SetPair(cfg.CustomIcons, it, null); };
            left.IconScaleRequested += delegate (DockWindow dw, DockItem it, double s) { SetIconScale(it, s); };
            right.IconScaleRequested += delegate (DockWindow dw, DockItem it, double s) { SetIconScale(it, s); };
            left.Show();
            right.Show();

            CreateControlChannel();

            timer = new DispatcherTimer(DispatcherPriority.Normal);
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += OnTick;

            HighResTimer.Require(true);

            InitAnimFps();

            HookLayout();

            HookDisplayChanges();

            desktopWatcher = new DesktopWatcher(Dispatcher, new Action(SyncDesktopItems));
            desktopWatcher.Start();

            timer.Start();
            Logger.Write("心跳定时器已启动: " + timer.Interval.TotalMilliseconds +
                " ms（轮询鼠标/菜单/巡检动画状态；动画帧由专用线程按「动画刷新率」推进）");

            ReconcileDesktopIconsAtStartup();
            ApplyDesktopIconSetting(true);

            Logger.Write("初始化完成: 左 " + cfg.Left.Count + " 项, 右 " + cfg.Right.Count + " 项");

            if (Environment.GetEnvironmentVariable("SIDESTACK_SHOW_SETTINGS") == "1")
            {
                DispatcherTimer settingsProbe = new DispatcherTimer();
                settingsProbe.Interval = TimeSpan.FromSeconds(2);
                settingsProbe.Tick += delegate { settingsProbe.Stop(); ShowSettings(); };
                settingsProbe.Start();
            }

            if (cfg.Left.Count == 0 && cfg.Right.Count == 0)
            {
                MessageBox.Show(
                    "桌面快捷方式一个都没扫到，Dock 面板目前是空的。\r\n\r\n" +
                    "配置文件位置：\r\n" + ConfigStore.FilePath + "\r\n\r\n" +
                    "可以手工在文件里加 left.item=D:\\路径\\程序.lnk|显示名 这样的行，然后右键 Dock 选择\"重新加载\"。",
                    "SideStack", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CreateControlChannel()
        {
            try
            {
                tray = new TrayIcon();
                tray.SettingsRequested += delegate { ShowSettings(); };
                tray.TogglePanelRequested += delegate { TogglePanelPin(); };
                tray.OpenConfigFolderRequested += delegate
                {
                    ShellUtils.RevealInExplorer(Path.GetDirectoryName(ConfigStore.FilePath));
                };
                tray.ExitRequested += delegate { Quit(); };

                tray.CaptureRequested += delegate { AllowCaptureForAWhile(); };

                captureAllowed = Environment.GetEnvironmentVariable("SIDESTACK_CAPTURABLE") == "1";
                WindowCapture.CaptureAllowed = captureAllowed;
                if (captureAllowed)
                {
                    WindowCapture.Apply((uint)System.Diagnostics.Process.GetCurrentProcess().Id, false);
                    Logger.Write("SIDESTACK_CAPTURABLE=1：本进程所有窗口均允许截图");
                }

                captureWatch = new System.Windows.Threading.DispatcherTimer();
                captureWatch.Interval = TimeSpan.FromMilliseconds(150);
                captureWatch.Tick += delegate
                {
                    try
                    {
                        uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                        WindowCapture.Apply(pid, !captureAllowed);
                    }
                    catch (Exception ex) { Logger.Write("captureWatch", ex); }
                };
                captureWatch.Start();

                try
                {
                    System.Windows.EventManager.RegisterClassHandler(
                        typeof(Window), Window.LoadedEvent,
                        new EventHandler(OnWindowLoadedExcludeCapture), true);
                    Logger.Write("已挂接窗口即时排除（Window.Loaded）");
                }
                catch (Exception ex) { Logger.Write("RegisterClassHandler", ex); }

                RefreshHost();
                tray.ShowBalloon("SideStack", "已在系统托盘运行：单击托盘上的 SideStack 图标即可打开设置界面。");
                Logger.Write("系统托盘图标已就绪（无独立控制窗口）");
            }
            catch (Exception ex)
            {
                Logger.Write("CreateControlChannel", ex);
            }
        }

        private void RefreshHost()
        {
            if (tray != null)
            {
                try { tray.SetPanelPinned(panelPinned); }
                catch (Exception ex) { Logger.Write("RefreshHost.Tray", ex); }
            }
        }

        private void TogglePanelPin()
        {
            panelPinned = !panelPinned;
            try
            {
                left.PanelPinned = panelPinned;
                right.PanelPinned = panelPinned;
                if (!panelPinned)
                {
                    left.ForceCollapse();
                    right.ForceCollapse();
                }
                Logger.Write("面板常显开关 -> " + panelPinned);
            }
            catch (Exception ex) { Logger.Write("TogglePanelPin", ex); }
            RefreshHost();
        }

        private void Hook(DockWindow w)
        {
            w.ItemActivated += delegate (DockWindow dw, DockItem it)
            {
                if (!ShellUtils.Launch(it.Path))
                {
                    Logger.Write("启动失败: " + it.Path);
                }
            };
            w.RevealRequested += delegate (DockWindow dw, DockItem it)
            {
                ShellUtils.RevealInExplorer(it.Path);
            };
            w.TransferRequested += delegate (DockWindow dw, DockItem it)
            {
                TransferItem(dw, it);
            };
            w.NoRoundToggled += delegate (DockWindow dw, DockItem it, bool exempt)
            {
                SetNoRound(it, exempt);
            };
            w.RemoveRequested += delegate (DockWindow dw, DockItem it)
            {
                RemoveItem(dw, it);
            };
            w.SettingsRequested += delegate (DockWindow dw)
            {
                ShowSettings();
            };
            w.ReloadRequested += delegate (DockWindow dw)
            {
                ReloadAll();
            };
            w.OpenConfigFolderRequested += delegate (DockWindow dw)
            {
                ShellUtils.RevealInExplorer(Path.GetDirectoryName(ConfigStore.FilePath));
            };
            w.ExitRequested += delegate (DockWindow dw)
            {
                Quit();
            };
        }

        private void RemoveItem(DockWindow w, DockItem it)
        {
            try
            {
                List<DockItem> list = w.IsLeft ? cfg.Left : cfg.Right;
                list.Remove(it);

                if (!ConfigStore.ContainsCI(cfg.Ignored, it.Path)) { cfg.Ignored.Add(it.Path); }
                ConfigStore.Save(cfg);
                w.ApplyConfig(cfg);
                Logger.Write("已从 Dock 移除: " + it.Name);
            }
            catch (Exception ex) { Logger.Write("RemoveItem", ex); }
        }

        private void SetNoRound(DockItem it, bool exempt)
        {
            try
            {
                if (it == null || string.IsNullOrEmpty(it.Path)) { return; }

                bool now = ConfigStore.ContainsCI(cfg.NoRound, it.Path);
                if (now == exempt) { return; }
                if (exempt) { cfg.NoRound.Add(it.Path); }
                else
                {
                    for (int i = cfg.NoRound.Count - 1; i >= 0; i--)
                    {
                        if (string.Equals(cfg.NoRound[i], it.Path, StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.NoRound.RemoveAt(i);
                        }
                    }
                }

                ConfigStore.Save(cfg);

                IconLoader.SetNoRound(cfg.NoRound);
                left.ApplyConfig(cfg);
                right.ApplyConfig(cfg);
                Logger.Write("圆角豁免" + (exempt ? "已开启" : "已取消") + ": " + it.Name +
                    "（名单共 " + cfg.NoRound.Count + " 项）");
            }
            catch (Exception ex) { Logger.Write("SetNoRound", ex); }
        }

        private void TransferItem(DockWindow w, DockItem it)
        {
            try
            {
                List<DockItem> from = w.IsLeft ? cfg.Left : cfg.Right;
                List<DockItem> to = w.IsLeft ? cfg.Right : cfg.Left;

                int idx = from.IndexOf(it);
                if (idx < 0) { return; }
                if (it.IsEmpty) { return; }

                int order = 0;
                for (int i = 0; i < idx; i++)
                {
                    if (from[i] != null && !from[i].IsEmpty) { order++; }
                }

                from.RemoveAt(idx);

                int insertAt = to.Count;
                int seen = 0;
                for (int i = 0; i < to.Count; i++)
                {
                    if (to[i] != null && !to[i].IsEmpty)
                    {
                        if (seen == order) { insertAt = i; break; }
                        seen++;
                    }
                }

                if (insertAt >= to.Count) { insertAt = TrailingEmptyStart(to); }
                to.Insert(insertAt, it);

                DockConfig.TrimTrailingEmpty(from);
                DockConfig.TrimTrailingEmpty(to);

                ConfigStore.Save(cfg);
                left.ApplyConfig(cfg);
                right.ApplyConfig(cfg);
                Logger.Write("图标转移: " + it.Name + (w.IsLeft ? " 左 -> 右" : " 右 -> 左") +
                    "（目标位置 " + insertAt + "）");
            }
            catch (Exception ex) { Logger.Write("TransferItem", ex); }
        }

        private void SyncDesktopItems()
        {
            try
            {
                List<DockItem> desktop = ConfigStore.ScanDesktop();
                HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DockItem di in desktop) { present.Add(di.Path); }

                bool changed = false;
                RemoveMissing(cfg.Left, present, ref changed);
                RemoveMissing(cfg.Right, present, ref changed);

                HashSet<string> referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectPaths(cfg.Left, referenced);
                CollectPaths(cfg.Right, referenced);

                List<DockItem> add = new List<DockItem>();
                foreach (DockItem di in desktop)
                {
                    if (referenced.Contains(di.Path)) { continue; }
                    if (ConfigStore.ContainsCI(cfg.Ignored, di.Path)) { continue; }
                    add.Add(di);
                }

                if (add.Count > 0)
                {
                    foreach (DockItem di in add)
                    {
                        bool pickLeft = RealCount(cfg.Left) <= RealCount(cfg.Right);
                        List<DockItem> list = pickLeft ? cfg.Left : cfg.Right;
                        list.Insert(TrailingEmptyStart(list), di);
                    }
                    changed = true;
                }

                for (int i = cfg.Ignored.Count - 1; i >= 0; i--)
                {
                    try { if (!File.Exists(cfg.Ignored[i])) { cfg.Ignored.RemoveAt(i); changed = true; } }
                    catch { }
                }

                if (changed)
                {
                    ConfigStore.Save(cfg);
                    left.ApplyConfig(cfg);
                    right.ApplyConfig(cfg);
                    Logger.Write("桌面实时同步完成：新增 " + add.Count + " 个图标");
                }
            }
            catch (Exception ex) { Logger.Write("SyncDesktopItems", ex); }
        }

        private static void RemoveMissing(List<DockItem> list, HashSet<string> present, ref bool changed)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                DockItem it = list[i];
                if (it.IsEmpty) { continue; }
                if (!present.Contains(it.Path)) { list.RemoveAt(i); changed = true; }
            }
        }

        private static void CollectPaths(List<DockItem> list, HashSet<string> set)
        {
            foreach (DockItem it in list)
            {
                if (!it.IsEmpty && it.Path.Length > 0) { set.Add(it.Path); }
            }
        }

        private static int RealCount(List<DockItem> list)
        {
            return list.Count - DockConfig.CountEmpty(list);
        }

        private static int TrailingEmptyStart(List<DockItem> list)
        {
            int i = list.Count;
            while (i > 0 && list[i - 1] != null && list[i - 1].IsEmpty) { i--; }
            return i;
        }

        private void ShowSettings()
        {
            if (settingsWin != null)
            {
                settingsWin.Activate();
                return;
            }
            try
            {
                SettingsWindow w = new SettingsWindow(cfg, exePath, OnSettingsOk,
                    delegate { return panelPinned; },
                    delegate { TogglePanelPin(); },
                    delegate { ReloadAll(); },
                    delegate { ShellUtils.RevealInExplorer(Path.GetDirectoryName(ConfigStore.FilePath)); },
                    delegate { Quit(); });
                settingsWin = w;
                w.Closed += delegate
                {
                    settingsWin = null;

                    try { left.ReassertToolWindowStyle(); right.ReassertToolWindowStyle(); } catch { }
                };
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Write("ShowSettings", ex);
                settingsWin = null;
            }
        }

        private void OnSettingsOk(DockConfig nc)
        {
            ApplyNewConfig(nc);
        }

        private void ApplyNewConfig(DockConfig nc)
        {
            try
            {
                cfg = nc;
                ConfigStore.Save(cfg);

                IconLoader.RadiusRatio = cfg.IconRadiusRatio;
                IconLoader.SetNoRound(cfg.NoRound);
                IconLoader.Clear();
                left.ApplyConfig(cfg);
                right.ApplyConfig(cfg);
                ApplyDesktopIconSetting(false);
                Logger.Write("配置已应用并保存");
            }
            catch (Exception ex) { Logger.Write("ApplyNewConfig", ex); }
            RefreshHost();
        }

        private void PickCustomIcon(DockItem it)
        {
            try
            {
                if (it == null || string.IsNullOrEmpty(it.Path)) { return; }
                Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Title = "为该图标选择一张图片";
                dlg.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|所有文件|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog() != true) { return; }
                SetPair(cfg.CustomIcons, it, dlg.FileName);
            }
            catch (Exception ex) { Logger.Write("App.PickCustomIcon", ex); }
        }

        private void SetIconScale(DockItem it, double scale)
        {
            try
            {
                if (it == null || string.IsNullOrEmpty(it.Path)) { return; }
                double v = Math.Max(0.6, Math.Min(1.4, scale));
                SetPair(cfg.IconScales, it, Math.Abs(v - 1.0) < 0.001 ? null : v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Logger.Write("App.SetIconScale", ex); }
        }

        private void SetPair(System.Collections.Generic.List<string> list, DockItem it, string value)
        {
            if (list == null || it == null || string.IsNullOrEmpty(it.Path)) { return; }
            int found = -1;
            for (int i = 0; i < list.Count; i++)
            {
                int j = list[i].IndexOf('|');
                if (j > 0 && string.Equals(list[i].Substring(0, j), it.Path, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
            }
            if (value == null)
            {
                if (found >= 0) { list.RemoveAt(found); }
                else { return; }
                Logger.Write("清除单图标设置: " + it.Path);
            }
            else
            {
                string rec = it.Path + "|" + value;
                if (found >= 0) { list[found] = rec; } else { list.Add(rec); }
                Logger.Write("单图标设置: " + rec);
            }
            ConfigStore.Save(cfg);
            ReloadAll();
        }

        private const string AppUserModelId = "SideStack.Dock";

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        private static void RegisterAppIdentity()
        {
            try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); }
            catch (Exception ex) { Logger.Write("SetCurrentProcessExplicitAppUserModelID", ex); }

            try
            {
                string dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string ico = System.IO.Path.Combine(dir ?? "", "app.ico");
                if (!System.IO.File.Exists(ico)) { ico = System.Reflection.Assembly.GetExecutingAssembly().Location; }

                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                           @"Software\Classes\AppUserModelId\" + AppUserModelId))
                {
                    if (k != null)
                    {
                        k.SetValue("DisplayName", "SideStack", Microsoft.Win32.RegistryValueKind.String);
                        k.SetValue("IconUri", ico, Microsoft.Win32.RegistryValueKind.String);
                        k.SetValue("ShowInSettings", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
                Logger.Write("应用身份已注册: " + AppUserModelId + "，通知图标 = " + ico);
            }
            catch (Exception ex) { Logger.Write("RegisterAppIdentity", ex); }
        }

        private System.Windows.Threading.DispatcherTimer captureTimer;
        private System.Windows.Threading.DispatcherTimer captureWatch;
        private bool captureAllowed;

        private void OnWindowLoadedExcludeCapture(object sender, EventArgs e)
        {
            try
            {
                if (captureAllowed) { return; }
                if (sender is Window) { WindowCapture.Guard(sender as Window); return; }
                int n = WindowCapture.Reapply();
                Logger.Write("新窗口即时排除: 本进程窗口 " + n + " 个");
            }
            catch (Exception ex) { Logger.Write("OnWindowLoadedExcludeCapture", ex); }
        }

        private void AllowCaptureForAWhile()
        {
            try
            {
                captureAllowed = true;
                WindowCapture.CaptureAllowed = true;
                left.SetCapturable(true);
                right.SetCapturable(true);

                WindowCapture.Apply((uint)System.Diagnostics.Process.GetCurrentProcess().Id, false);
                if (captureTimer == null)
                {
                    captureTimer = new System.Windows.Threading.DispatcherTimer();
                    captureTimer.Interval = TimeSpan.FromSeconds(60);
                    captureTimer.Tick += delegate
                    {
                        captureTimer.Stop();
                        captureAllowed = false;
                        WindowCapture.CaptureAllowed = false;
                        WindowCapture.Reapply();
                        try { left.SetCapturable(false); right.SetCapturable(false); }
                        catch (Exception ex) { Logger.Write("captureTimer", ex); }
                        tray.ShowBalloon("SideStack", "已恢复：侧边栏重新从截图/录屏中排除。");
                    };
                }
                captureTimer.Stop();
                captureTimer.Start();
                tray.ShowBalloon("SideStack", "60 秒内可以截图/录屏侧边栏；到点自动恢复抓屏排除。");
            }
            catch (Exception ex) { Logger.Write("App.AllowCaptureForAWhile", ex); }
        }

        private void AddDroppedFiles(DockWindow dw, System.Collections.Generic.List<string> paths, int index)
        {
            try
            {
                if (dw == null || paths == null || paths.Count == 0) { return; }
                List<DockItem> list = dw.IsLeft ? cfg.Left : cfg.Right;
                int added = 0;
                foreach (string p in paths)
                {
                    if (string.IsNullOrEmpty(p)) { continue; }
                    if (!System.IO.File.Exists(p) && !System.IO.Directory.Exists(p)) { continue; }
                    bool dup = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        DockItem it = list[i];
                        if (it != null && string.Equals(it.Path, p, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                    }
                    if (dup) { Logger.Write("拖入已存在，跳过: " + p); continue; }

                    int at = (index >= 0 && index + added <= list.Count) ? index + added : list.Count;
                    list.Insert(at, new DockItem(p, System.IO.Path.GetFileNameWithoutExtension(p)));
                    for (int k = cfg.Ignored.Count - 1; k >= 0; k--)
                    {
                        if (string.Equals(cfg.Ignored[k], p, StringComparison.OrdinalIgnoreCase)) { cfg.Ignored.RemoveAt(k); }
                    }
                    Logger.Write("拖入添加: " + p + " → " + (dw.IsLeft ? "左" : "右") + " 第 " + at + " 位");
                    added++;
                }
                if (added == 0) { return; }
                ConfigStore.Save(cfg);
                ReloadAll();
            }
            catch (Exception ex) { Logger.Write("App.AddDroppedFiles", ex); }
        }

        private void ReloadAll()
        {
            try
            {
                cfg = ConfigStore.Load();
                IconLoader.RadiusRatio = cfg.IconRadiusRatio;
                IconLoader.SetNoRound(cfg.NoRound);
                IconLoader.Clear();
                left.ApplyConfig(cfg);
                right.ApplyConfig(cfg);
                ApplyDesktopIconSetting(false);
                Logger.Write("重新加载配置: 左 " + cfg.Left.Count + " 项, 右 " + cfg.Right.Count + " 项");
            }
            catch (Exception ex) { Logger.Write("ReloadAll", ex); }
            RefreshHost();
        }

        private void ReconcileDesktopIconsAtStartup()
        {
            try
            {
                bool want = cfg.HideDesktopIcons;
                bool nowHidden = ShellUtils.AreDesktopIconsActuallyHidden();
                if (want == nowHidden)
                {
                    if (want) { desktopIconsHiddenByUs = true; }
                    return;
                }
                ShellUtils.SetDesktopIconsHidden(want);
                desktopIconsHiddenByUs = want;
                Logger.Write("启动自愈：桌面图标 -> " + (want ? "隐藏" : "显示") +
                    "（上次可能未正常退出，状态未恢复）");
            }
            catch (Exception ex) { Logger.Write("ReconcileDesktopIconsAtStartup", ex); }
        }

        private void ApplyDesktopIconSetting(bool atStartup)
        {
            try
            {
                bool want = cfg.HideDesktopIcons;
                bool nowHidden = ShellUtils.AreDesktopIconsActuallyHidden();
                if (want == nowHidden)
                {
                    if (want) { desktopIconsHiddenByUs = true; }

                    if (ShellUtils.IsDesktopIconsHidden() != want) { ShellUtils.SetDesktopIconsHidden(want); }
                    if (atStartup) { Logger.Write("隐藏桌面图标: 已是目标状态（" + (want ? "隐藏" : "显示") + "）"); }
                    return;
                }

                if (!ShellUtils.SetDesktopIconsHidden(want))
                {
                    Logger.Write("隐藏桌面图标失败：未找到桌面列表视图（资源管理器可能未就绪）");
                    return;
                }
                desktopIconsHiddenByUs = want;
                Logger.Write("隐藏桌面图标 -> " + want + (atStartup ? "（启动时套用）" : "（设置变更）"));

                RequestLensRefreshDelayed(700);
            }
            catch (Exception ex) { Logger.Write("ApplyDesktopIconSetting", ex); }
        }

        private void RequestLensRefreshDelayed(int delayMs)
        {
            try
            {
                if (left != null) { left.RequestLensRefresh(delayMs); }
                if (right != null) { right.RequestLensRefresh(delayMs); }
            }
            catch (Exception ex) { Logger.Write("RequestLensRefreshDelayed", ex); }
        }

        private void RestoreDesktopIcons()
        {
            try
            {
                if (!desktopIconsHiddenByUs) { return; }
                desktopIconsHiddenByUs = false;
                if (ShellUtils.AreDesktopIconsActuallyHidden())
                {
                    ShellUtils.SetDesktopIconsHidden(false);
                    Logger.Write("退出前已恢复桌面图标显示");
                }
            }
            catch (Exception ex) { Logger.Write("RestoreDesktopIcons", ex); }
        }

        private void Quit()
        {
            if (quitting) { return; }
            quitting = true;
            try { if (timer != null) { timer.Stop(); } }
            catch { }
            Logger.Write("================ SideStack 退出 ================");
            try { Shutdown(); }
            catch { Environment.Exit(0); }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            RestoreDesktopIcons();
            try { if (desktopWatcher != null) { desktopWatcher.Dispose(); desktopWatcher = null; } }
            catch { }
            try { if (tray != null) { tray.Dispose(); tray = null; } }
            catch { }
            base.OnExit(e);
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                int now = NowMs();

                POINT cur;
                if (!ScreenUtils.TryGetCursorPx(out cur)) { return; }

                if (lastCoverCheck == 0 || (now - lastCoverCheck) >= 250)
                {
                    lastCoverCheck = now;
                    bool c = ScreenUtils.IsDesktopCovered();
                    if (c != desktopCovered)
                    {
                        Logger.Write("桌面遮挡状态 -> " + c);
                        desktopCovered = c;
                    }
                }

                TickDock(left, cur, now);
                TickDock(right, cur, now);

                left.PollMenuAutoClose();
                right.PollMenuAutoClose();

                SyncAnimTimer();
                RetuneHeartbeat();
            }
            catch (Exception ex)
            {
                Logger.Write("OnTick", ex);
            }
        }

        private void TickDock(DockWindow d, POINT cur, int now)
        {
            double sc = d.DpiScale;
            if (sc <= 0.1 || sc > 8) { sc = 1.0; }

            bool inTrigger = IsCursorInBand(d.IsLeft, cfg.TriggerWidth, sc, cur);
            bool inDock = d.ContainsCursorDip(cur.X / sc, cur.Y / sc);

            d.UpdateState(desktopCovered, inTrigger, inDock, now);
        }

        private static bool IsCursorInBand(bool isLeft, double triggerDip, double scale, POINT cur)
        {
            RECT mon = ScreenUtils.GetPrimaryMonitorPx();
            double band = triggerDip * scale;
            if (band < 1.0) { band = 1.0; }
            if (isLeft)
            {
                return cur.X <= mon.Left + band;
            }
            return cur.X >= mon.Right - band;
        }

        private void InitAnimFps()
        {
            try
            {
                int maxHz = DisplayInfo.GetMaxRefreshRate();
                if (maxHz < 1) { maxHz = 60; }
                Logger.Write("显示器能力: " + DisplayInfo.Describe());
                Logger.Write("动画刷新率上限 = " + maxHz + " Hz（自动读取显示器最高刷新率，可设 1 ~ " + maxHz + "）");

                bool changed = false;
                if (cfg.AnimFps < 1) { cfg.AnimFps = 1; changed = true; }
                if (cfg.AnimFps > maxHz) { cfg.AnimFps = maxHz; changed = true; }
                if (changed)
                {
                    Logger.Write("动画刷新率超出上限，已收敛为 " + cfg.AnimFps + " fps 并写回配置");
                    ConfigStore.Save(cfg);
                }
                else
                {
                    Logger.Write("动画刷新率 = " + cfg.AnimFps + " fps");
                }
            }
            catch (Exception ex) { Logger.Write("InitAnimFps", ex); }
        }

        private void HookLayout()
        {
            if (layoutHooked) { return; }
            layoutHooked = true;
            try
            {
                left.LayoutChanged += delegate (DockWindow dw) { OnLayoutChanged(dw); };
                right.LayoutChanged += delegate (DockWindow dw) { OnLayoutChanged(dw); };
                Logger.Write("已挂接面板布局变化事件（拖动换位/图标移动/改名后自动落盘）");
            }
            catch (Exception ex) { Logger.Write("HookLayout", ex); }
        }

        private bool displayHooked;
        private void HookDisplayChanges()
        {
            if (displayHooked) { return; }
            displayHooked = true;
            try
            {
                SystemEvents.DisplaySettingsChanged += delegate
                {
                    try
                    {
                        ScreenUtils.InvalidateMonitorCache();
                        Logger.Write("显示设置已变化：显示器矩形缓存已失效");
                    }
                    catch { }
                };
                SystemEvents.UserPreferenceChanged += delegate (object s, UserPreferenceChangedEventArgs e)
                {
                    try
                    {
                        ScreenUtils.InvalidateMonitorCache();
                        Logger.Write("系统外观设置已变化（" + e.Category + "）：显示器矩形缓存已失效");
                    }
                    catch { }
                };
                Logger.Write("已监听显示设置变化（分辨率 / 任务栏 / 缩放）");
            }
            catch (Exception ex) { Logger.Write("HookDisplayChanges", ex); }
        }

        private void OnLayoutChanged(DockWindow dw)
        {
            try
            {
                ConfigStore.Save(cfg);
                System.Collections.Generic.List<DockItem> list = dw.IsLeft ? cfg.Left : cfg.Right;
                Logger.Write("面板布局已更新: " + (dw.IsLeft ? "左" : "右") + "面板 " + list.Count +
                    " 项（其中空白位置 " + DockConfig.CountEmpty(list) + " 个）");
            }
            catch (Exception ex) { Logger.Write("OnLayoutChanged", ex); }
            RefreshHost();
        }

        private int AnimFpsSafe()
        {
            int fps = cfg.AnimFps;
            if (fps < 1) { fps = 1; }
            if (fps > 240) { fps = 240; }
            return fps;
        }

        private void AnimLoop()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double next = 0;
            while (animThreadRun)
            {
                double period = animThreadPeriodUs / 1000.0;
                next += period;
                double wait = next - sw.Elapsed.TotalMilliseconds;
                if (wait < -period)
                {
                    next = sw.Elapsed.TotalMilliseconds + period;
                    wait = period;
                }
                try
                {
                    if (wait > 2.5) { System.Threading.Thread.Sleep((int)(wait - 2.0)); }
                    while (animThreadRun && sw.Elapsed.TotalMilliseconds < next - 0.15)
                    {
                        System.Threading.Thread.SpinWait(120);
                    }
                }
                catch { break; }
                if (!animThreadRun) { break; }
                if (animTickPending) { continue; }
                animTickPending = true;
                try
                {
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                        new Action(AnimPump));
                }
                catch (Exception ex) { animTickPending = false; Logger.Write("动画帧投递失败", ex); break; }
            }
        }

        private void AnimPump()
        {
            animTickPending = false;
            if (!animThreadRun) { return; }
            try { StepAnimations(NowMs()); }
            catch (Exception ex) { Logger.Write("AnimPump", ex); }
        }

        private void SyncAnimTimer()
        {
            bool animL = left != null && left.IsAnimating;
            bool animR = right != null && right.IsAnimating;

            int fps = AnimFpsSafe();
            if (fps < 1) { fps = 1; }
            if (fps > 480) { fps = 480; }

            int periodUs = (int)Math.Round(1000000.0 / fps);
            if (periodUs < 4000) { periodUs = 4000; }
            if (periodUs > 1000000) { periodUs = 1000000; }
            animThreadPeriodUs = periodUs;

            if (!animL && !animR) { StopAnimThread(); return; }
            StartAnimThread();
        }

        private void StartAnimThread()
        {
            if (animThread != null && animThread.IsAlive) { return; }
            animThreadRun = true;
            animTickPending = false;
            animThread = new System.Threading.Thread(AnimLoop);
            animThread.IsBackground = true;
            animThread.Name = "SideStack.Anim";
            animThread.Start();
            Logger.Write("动画推进线程启动: 周期 " + (animThreadPeriodUs / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) +
                " ms（实际 " + (1000000.0 / animThreadPeriodUs).ToString("0.#", CultureInfo.InvariantCulture) +
                " fps；位移动画目标 " + cfg.AnimFps + " fps，流体风格的水按较低帧率推以免白烧 CPU）");
        }

        private void StopAnimThread()
        {
            if (animThread != null)
            {
                animThreadRun = false;
                try { animThread.Join(60); } catch { }
                animThread = null;
            }
            animTickPending = false;
        }

        private void StepAnimations(int now)
        {
            try { left.StepAnim(now); }
            catch (Exception ex) { Logger.Write("StepAnim.left", ex); }
            try { right.StepAnim(now); }
            catch (Exception ex) { Logger.Write("StepAnim.right", ex); }
        }

        private void RetuneHeartbeat()
        {
            if (timer == null) { return; }
            double want = 16.0;
            if (Math.Abs(timer.Interval.TotalMilliseconds - want) > 0.4)
            {
                timer.Interval = TimeSpan.FromMilliseconds(want);
            }

            HighResTimer.Require(true);
        }
    }
}
