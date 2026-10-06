using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace SideStack
{
    public static class AutoRebuild
    {
        private const string TempExeName = "SideStack.exe.new";
        private const string MarkerName = "autorebuild.marker";
        private const int CompileTimeoutMs = 180000;
        private const int MaxErrorChars = 900;

        public static bool TryUpdateBeforeStart()
        {
            string exePath;
            string dir;
            try
            {
                exePath = Process.GetCurrentProcess().MainModule.FileName;
                dir = Path.GetDirectoryName(exePath);
            }
            catch { return false; }
            if (string.IsNullOrEmpty(dir)) { return false; }

            ReportPreviousUpdate(exePath);

            string srcDir = Path.Combine(dir, "src");
            if (!Directory.Exists(srcDir)) { return false; }
            string changed;
            try { changed = FindChangedSource(dir, srcDir, exePath); }
            catch (Exception ex) { Log("源码时间比对失败: " + ex.Message); return false; }
            if (changed == null) { return false; }
            Log("检测到源码更新（比 exe 新）: " + changed);

            string err;
            if (!Compile(dir, srcDir, out err))
            {
                Log("自动重新编译失败: " + err);
                Warn("检测到 src 下的源码已修改，但自动重新编译失败：\r\n\r\n" + err +
                     "\r\n\r\n将改为按原有方式启动现有版本的 SideStack.exe（现有程序文件未被修改）。");
                return false;
            }
            Log("自动重新编译成功 -> " + TempExeName);

            string tempExe = Path.Combine(dir, TempExeName);
            try
            {
                WriteMarker(tempExe);
                DispatchReplacement(dir, exePath, tempExe);
                Log("已派发替换脚本（等本进程退出 -> 结束托盘旧实例 -> 覆盖 exe -> 启动新版本）");
            }
            catch (Exception ex)
            {
                Log("派发替换失败: " + ex.Message);
                try { if (File.Exists(tempExe)) { File.Delete(tempExe); } }
                catch { }
                Warn("自动更新失败：" + ex.Message + "\r\n\r\n将按原有方式启动现有版本。");
                return false;
            }
            return true;
        }

        private static string FindChangedSource(string dir, string srcDir, string exePath)
        {
            DateTime newest = File.GetLastWriteTimeUtc(exePath);
            string newestPath = null;

            string[] cs;
            try { cs = Directory.GetFiles(srcDir, "*.cs"); }
            catch { cs = new string[0]; }
            foreach (string f in cs)
            {
                DateTime t = File.GetLastWriteTimeUtc(f);
                if (t > newest) { newest = t; newestPath = f; }
            }

            string[] extra = new string[] { "app.ico", "app.manifest" };
            foreach (string n in extra)
            {
                string p = Path.Combine(dir, n);
                if (!File.Exists(p)) { continue; }
                DateTime t = File.GetLastWriteTimeUtc(p);
                if (t > newest) { newest = t; newestPath = p; }
            }
            return newestPath;
        }

        private static bool Compile(string dir, string srcDir, out string err)
        {
            err = null;
            string csc = FindCsc();
            if (csc == null)
            {
                err = "未找到 C# 编译器（csc.exe），需要 .NET Framework 4.x。";
                return false;
            }
            string fw = Path.GetDirectoryName(csc);
            string outExe = Path.Combine(dir, TempExeName);

            try { if (File.Exists(outExe)) { File.Delete(outExe); } }
            catch (Exception ex) { err = "无法清理上一次的临时编译产物：" + ex.Message; return false; }

            List<string> a = new List<string>();
            a.Add("/nologo");
            a.Add("/target:winexe");
            a.Add("/platform:anycpu");
            a.Add("/optimize+");
            a.Add("/out:\"" + outExe + "\"");

            string ico = Path.Combine(dir, "app.ico");
            if (File.Exists(ico)) { a.Add("/win32icon:\"" + ico + "\""); }
            string man = Path.Combine(dir, "app.manifest");
            if (File.Exists(man)) { a.Add("/win32manifest:\"" + man + "\""); }

            string[] cs = Directory.GetFiles(srcDir, "*.cs");
            foreach (string f in cs) { a.Add("\"" + f + "\""); }

            string[] refs = new string[] { "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Xaml.dll" };
            foreach (string r in refs)
            {
                string p = Path.Combine(fw, r);
                if (File.Exists(p)) { a.Add("/r:\"" + p + "\""); }
            }
            string wpf = Path.Combine(fw, "WPF");
            string[] wrefs = new string[] { "PresentationFramework.dll", "PresentationCore.dll", "WindowsBase.dll" };
            foreach (string r in wrefs)
            {
                string p = Path.Combine(wpf, r);
                if (File.Exists(p)) { a.Add("/r:\"" + p + "\""); }
            }

            ProcessStartInfo psi = new ProcessStartInfo(csc, string.Join(" ", a.ToArray()));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            try
            {
                using (Process p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(CompileTimeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        err = "编译超时（超过 " + (CompileTimeoutMs / 1000) + " 秒）。";
                        return false;
                    }
                    if (p.ExitCode != 0 || !File.Exists(outExe))
                    {
                        string msg = (so + "\r\n" + se).Trim();
                        if (msg.Length > MaxErrorChars) { msg = msg.Substring(0, MaxErrorChars) + "\r\n...(错误信息过长已截断)"; }
                        if (msg.Length == 0) { msg = "编译器返回码 " + p.ExitCode + "，且未生成输出文件。"; }
                        err = msg;
                        return false;
                    }
                }
            }
            catch (Exception ex) { err = ex.Message; return false; }

            return true;
        }

        private static string FindCsc()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(win)) { win = "C:\\Windows"; }
            string[] cands = new string[] {
                Path.Combine(win, @"Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
                Path.Combine(win, @"Microsoft.NET\Framework\v4.0.30319\csc.exe")
            };
            foreach (string c in cands) { if (File.Exists(c)) { return c; } }
            return null;
        }

        private static void DispatchReplacement(string dir, string exePath, string tempExe)
        {
            int pid = Process.GetCurrentProcess().Id;
            string script = Path.Combine(Path.GetTempPath(), "sidestack_autorebuild_" + pid + ".cmd");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal");
            sb.AppendLine("cd /d \"" + dir + "\"");
            sb.AppendLine(":waitdie");
            sb.AppendLine("tasklist /FI \"PID eq " + pid + "\" | find \"" + pid + "\" >nul");
            sb.AppendLine("if not errorlevel 1 (");
            sb.AppendLine("  ping -n 2 127.0.0.1 >nul");
            sb.AppendLine("  goto waitdie");
            sb.AppendLine(")");
            sb.AppendLine("taskkill /F /IM \"SideStack.exe\" >nul 2>&1");
            sb.AppendLine("ping -n 3 127.0.0.1 >nul");
            sb.AppendLine("move /Y \"" + tempExe + "\" \"" + exePath + "\" >nul 2>&1");
            sb.AppendLine("if errorlevel 1 goto fail");
            sb.AppendLine("start \"\" \"" + exePath + "\"");
            sb.AppendLine("goto done");
            sb.AppendLine(":fail");
            sb.AppendLine("if exist \"" + tempExe + "\" del /q \"" + tempExe + "\" >nul 2>&1");
            sb.AppendLine("start \"\" \"" + exePath + "\"");
            sb.AppendLine(":done");
            sb.AppendLine("del \"%~f0\" >nul 2>&1");

            File.WriteAllText(script, sb.ToString(), Encoding.Default);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }

        private static void WriteMarker(string tempExe)
        {
            try
            {
                FileInfo fi = new FileInfo(tempExe);
                File.WriteAllText(Path.Combine(DataDir(), MarkerName),
                    fi.LastWriteTimeUtc.Ticks.ToString() + "|" + fi.Length.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static void ReportPreviousUpdate(string exePath)
        {
            try
            {
                string marker = Path.Combine(DataDir(), MarkerName);
                if (!File.Exists(marker)) { return; }
                string expect = File.ReadAllText(marker).Trim();
                try { File.Delete(marker); } catch { }

                string actual = "";
                try
                {
                    FileInfo fi = new FileInfo(exePath);
                    actual = fi.LastWriteTimeUtc.Ticks.ToString() + "|" + fi.Length.ToString();
                }
                catch { }

                if (expect == actual) { Log("自动更新已生效：当前运行的是按新源码重新编译的版本"); }
                else { Log("上一次自动更新未完成（exe 未被替换），本次启动的仍是现有版本"); }
            }
            catch { }
        }

        private static string DataDir()
        {
            string d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SideStack");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }

        private static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(DataDir(), "sidestack.log"),
                    DateTime.Now.ToString("MM-dd HH:mm:ss.fff") + "  [自检] " + msg + "\r\n",
                    Encoding.UTF8);
            }
            catch { }
        }

        private static void Warn(string text)
        {
            try { MessageBox.Show(text, "SideStack 自动更新", MessageBoxButton.OK, MessageBoxImage.Warning); }
            catch { }
        }
    }
}
