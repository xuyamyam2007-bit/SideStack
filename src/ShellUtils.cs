using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SideStack
{
    public static class ShellUtils
    {
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string AdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        public const string RunValueName = "SideStack";

        public static bool Launch(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) { return false; }
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    Logger.Write("Launch: 目标不存在 -> " + path);
                    return false;
                }
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = path;
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Path.GetDirectoryName(path);
                Process.Start(psi);
                Logger.Write("Launch ok -> " + path);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("Launch(" + path + ")", ex);
                return false;
            }
        }

        public static void RevealInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) { return; }
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                    return;
                }
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex) { Logger.Write("Reveal", ex); }
        }

        public static string ResolveShortcut(string lnkPath)
        {
            try
            {
                if (string.IsNullOrEmpty(lnkPath)) { return null; }
                if (!lnkPath.ToLowerInvariant().EndsWith(".lnk")) { return lnkPath; }
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) { return null; }
                object shell = Activator.CreateInstance(t);
                try
                {
                    object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                    if (sc == null) { return null; }
                    object target = sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                    return target as string;
                }
                finally
                {
                    if (shell != null && Marshal.IsComObject(shell)) { Marshal.ReleaseComObject(shell); }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("ResolveShortcut(" + lnkPath + ")", ex);
                return null;
            }
        }

        public static void MigrateLegacyAutoStart(string exePath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) { return; }
                    object legacy = k.GetValue("DeskDock");
                    if (legacy == null) { return; }
                    k.DeleteValue("DeskDock", false);
                    k.SetValue(RunValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                    Logger.Write("SideStack");
                }
            }
            catch { }
        }

        public static bool IsAutoStartOn()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (k == null) { return false; }
                    object v = k.GetValue(RunValueName);
                    return v != null;
                }
            }
            catch { return false; }
        }

        public static bool SetAutoStart(bool on, string exePath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) { return false; }
                    if (on)
                    {
                        k.SetValue(RunValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                    }
                    else
                    {
                        k.DeleteValue(RunValueName, false);
                    }
                }
                Logger.Write("SetAutoStart -> " + on);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("SetAutoStart", ex);
                return false;
            }
        }

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const uint LVM_GETITEMCOUNT = 0x1004;
        private const uint LVM_GETSELECTEDCOUNT = 0x100C;
        private const uint LVM_GETITEMSPACING = 0x1033;

        public static bool IsDesktopIconsHidden()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(AdvancedKeyPath, false))
                {
                    if (k == null) { return false; }
                    object v = k.GetValue("HideIcons");
                    if (v == null) { return false; }
                    return Convert.ToInt32(v) != 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Write("IsDesktopIconsHidden", ex);
                return false;
            }
        }

        public static bool AreDesktopIconsActuallyHidden()
        {
            try
            {
                IntPtr lv = FindDesktopListView();
                if (lv == IntPtr.Zero) { return false; }
                return !NativeMethods.IsWindowVisible(lv);
            }
            catch (Exception ex)
            {
                Logger.Write("AreDesktopIconsActuallyHidden", ex);
                return false;
            }
        }

        public static bool SetDesktopIconsHidden(bool hidden)
        {
            bool ok = ApplyToDesktop(hidden);
            WriteHideIcons(hidden);
            Logger.Write("隐藏桌面图标 -> " + hidden + (ok ? "（已作用到桌面）" : "（未找到桌面列表视图，仅写了配置）"));
            return ok;
        }

        private static bool ApplyToDesktop(bool hidden)
        {
            try
            {
                IntPtr lv = FindDesktopListView();
                if (lv == IntPtr.Zero)
                {
                    Logger.Write("ApplyToDesktop: 找不到桌面 SysListView32（资源管理器可能尚未就绪）");
                    return false;
                }
                Logger.Write("隐藏桌面图标: 桌面列表视图 = 0x" + lv.ToInt64().ToString("X") +
                    "，当前可见 = " + NativeMethods.IsWindowVisible(lv));
                NativeMethods.ShowWindow(lv, hidden ? SW_HIDE : SW_SHOW);
                if (!hidden)
                {
                    NativeMethods.InvalidateRect(lv, IntPtr.Zero, true);
                    NativeMethods.RedrawWindow(lv, IntPtr.Zero, IntPtr.Zero,
                        NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_UPDATENOW | NativeMethods.RDW_ALLCHILDREN);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("ApplyToDesktop", ex);
                return false;
            }
        }

        private static IntPtr FindDesktopListView()
        {
            IntPtr progman = NativeMethods.FindWindow("Progman", null);
            IntPtr defView = progman == IntPtr.Zero
                ? IntPtr.Zero
                : NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defView == IntPtr.Zero)
            {
                IntPtr worker = IntPtr.Zero;
                for (int i = 0; i < 32; i++)
                {
                    worker = NativeMethods.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                    if (worker == IntPtr.Zero) { break; }
                    IntPtr dv = NativeMethods.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (dv != IntPtr.Zero) { defView = dv; break; }
                }
            }
            if (defView == IntPtr.Zero) { return IntPtr.Zero; }
            IntPtr lv = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (lv != IntPtr.Zero) { return lv; }

            IntPtr inner = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "WorkerW", null);
            while (inner != IntPtr.Zero)
            {
                IntPtr found = NativeMethods.FindWindowEx(inner, IntPtr.Zero, "SysListView32", null);
                if (found != IntPtr.Zero) { return found; }
                inner = NativeMethods.FindWindowEx(defView, inner, "WorkerW", null);
            }
            return IntPtr.Zero;
        }

        private static bool WriteHideIcons(bool hidden)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(AdvancedKeyPath, true))
                {
                    if (k == null)
                    {
                        Logger.Write("WriteHideIcons: 打不开注册表项 " + AdvancedKeyPath);
                        return false;
                    }
                    k.SetValue("HideIcons", hidden ? 1 : 0, RegistryValueKind.DWord);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("WriteHideIcons", ex);
                return false;
            }
        }
    }
}
