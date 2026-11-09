using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SideStack
{
    // 计划任务方式的开机自启与启动优先级。
    internal static class StartupTask
    {
        public const string Folder = @"\SideStack\";
        public const string Name = "SideStack";
        private static string FullPath { get { return Folder + Name; } }

        public const int PriorityBoostOn = 5;
        public const int PriorityBoostOff = 4;

        public static bool BoostProcess()
        {
            try
            {
                System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess();
                if (p.PriorityClass == System.Diagnostics.ProcessPriorityClass.AboveNormal
                    || p.PriorityClass == System.Diagnostics.ProcessPriorityClass.High)
                {
                    return true;
                }
                p.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
                Logger.Write("进程优先级 -> " + p.PriorityClass + "（BasePriority " + p.BasePriority + "）");
                return true;
            }
            catch (Exception ex) { Logger.Write("StartupTask.BoostProcess", ex); return false; }
        }

        public static bool IsProcessBoosted()
        {
            try
            {
                System.Diagnostics.ProcessPriorityClass c = System.Diagnostics.Process.GetCurrentProcess().PriorityClass;
                return c == System.Diagnostics.ProcessPriorityClass.AboveNormal
                    || c == System.Diagnostics.ProcessPriorityClass.High
                    || c == System.Diagnostics.ProcessPriorityClass.RealTime;
            }
            catch { return false; }
        }

        public static bool Exists()
        {
            try
            {
                int code;
                Run("schtasks.exe", new string[] { "/Query", "/TN", FullPath }, out code, 8000);
                return code == 0;
            }
            catch { return false; }
        }

        public static int GetPriority()
        {
            try
            {
                string xml;
                if (!Export(out xml)) { return 0; }
                Match m = Regex.Match(xml, @"<Priority>(\d+)</Priority>");
                if (!m.Success) { return 0; }
                int v;
                if (!int.TryParse(m.Groups[1].Value, out v)) { return 0; }
                return v;
            }
            catch (Exception ex) { Logger.Write("StartupTask.GetPriority", ex); return 0; }
        }

        public static bool IsPriorityBoostWanted()
        {
            int p = GetPriority();
            if (p >= PriorityBoostOn) { return true; }
            return IsProcessBoosted();
        }

        public static bool SetPriorityBoostIntent(bool on)
        {
            try
            {
                string xml;
                if (!Export(out xml)) { return false; }
                int want = on ? PriorityBoostOn : PriorityBoostOff;
                string patched;
                if (Regex.IsMatch(xml, @"<Priority>\d+</Priority>"))
                {
                    patched = Regex.Replace(xml, @"<Priority>\d+</Priority>", "<Priority>" + want + "</Priority>");
                }
                else
                {
                    patched = Regex.Replace(xml, @"(<Settings[^>]*>)", "$1<Priority>" + want + "</Priority>", RegexOptions.None);
                    if (patched == xml)
                    {
                        Logger.Write("启动优先级：任务 XML 里找不到 <Settings>，无法写入");
                        return false;
                    }
                }

                string tmp = Path.Combine(Path.GetTempPath(), "sidestack-task-priority.xml");

                File.WriteAllText(tmp, patched, new UnicodeEncoding(false, true));

                int code;
                string output = Run("schtasks.exe", new string[] { "/Create", "/TN", FullPath, "/XML", tmp, "/F" }, out code, 30000);
                try { File.Delete(tmp); } catch { }

                Logger.Write("启动优先级意图 -> " + (on ? "要提升" : "不提升") + "（schtasks 退出码 " + code + "）"
                             + (code == 0 ? "" : "：" + output));
                return code == 0;
            }
            catch (Exception ex) { Logger.Write("StartupTask.SetPriorityBoostIntent", ex); return false; }
        }

        public static string CommandFor(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) { return ""; }
            return (exePath.IndexOf(' ') >= 0) ? ("\"" + exePath + "\"") : exePath;
        }

        public static bool Delete(string exePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(exePath)) { Create(false, exePath); }
                int code;
                string output = Run("schtasks.exe", new string[] { "/Delete", "/TN", FullPath, "/F" }, out code, 30000);
                Logger.Write("删除计划任务 -> 退出码 " + code + (code == 0 ? "" : "：" + output));
                return code == 0;
            }
            catch (Exception ex) { Logger.Write("StartupTask.Delete", ex); return false; }
        }

        public static bool Delete()
        {
            return Delete(null);
        }

        public static bool Create(bool raised, string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath)) { return false; }
                string user = Environment.UserName;
                string workDir = Path.GetDirectoryName(exePath);
                int prio = raised ? PriorityBoostOn : PriorityBoostOff;

                string xml = Template
                    .Replace("@USER@", Escape(user))
                    .Replace("@PRIORITY@", prio.ToString())
                    .Replace("@CMD@", Escape(exePath))
                    .Replace("@WORKDIR@", Escape(workDir));

                string tmp = Path.Combine(Path.GetTempPath(), "sidestack-task-create.xml");
                File.WriteAllText(tmp, xml, new UnicodeEncoding(false, true));

                int code;
                string output = Run("schtasks.exe", new string[] { "/Create", "/TN", FullPath, "/XML", tmp, "/F" }, out code, 30000);
                try { File.Delete(tmp); } catch { }

                Logger.Write("创建计划任务（优先级 " + prio + "）-> 退出码 " + code
                             + (code == 0 ? "" : "：" + output));
                return code == 0;
            }
            catch (Exception ex) { Logger.Write("StartupTask.Create", ex); return false; }
        }

        private static string Escape(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private const string Template =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <RegistrationInfo>\r\n" +
            "    <Description>SideStack 登录自启（由程序内「启动优先级」开关管理：高于正常 = 更早拿到 CPU）</Description>\r\n" +
            "  </RegistrationInfo>\r\n" +
            "  <Triggers>\r\n" +
            "    <LogonTrigger>\r\n" +
            "      <Enabled>true</Enabled>\r\n" +
            "      <UserId>@USER@</UserId>\r\n" +
            "    </LogonTrigger>\r\n" +
            "  </Triggers>\r\n" +
            "  <Principals>\r\n" +
            "    <Principal id=\"Author\">\r\n" +
            "      <UserId>@USER@</UserId>\r\n" +
            "      <LogonType>InteractiveToken</LogonType>\r\n" +
            "      <RunLevel>LeastPrivilege</RunLevel>\r\n" +
            "    </Principal>\r\n" +
            "  </Principals>\r\n" +
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
            "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
            "    <IdleSettings>\r\n" +
            "      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n" +
            "      <RestartOnIdle>false</RestartOnIdle>\r\n" +
            "    </IdleSettings>\r\n" +
            "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
            "    <Enabled>true</Enabled>\r\n" +
            "    <Hidden>false</Hidden>\r\n" +
            "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
            "    <WakeToRun>false</WakeToRun>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "    <Priority>@PRIORITY@</Priority>\r\n" +
            "    <RestartOnFailure>\r\n" +
            "      <Interval>PT1M</Interval>\r\n" +
            "      <Count>3</Count>\r\n" +
            "    </RestartOnFailure>\r\n" +
            "  </Settings>\r\n" +
            "  <Actions Context=\"Author\">\r\n" +
            "    <Exec>\r\n" +
            "      <Command>@CMD@</Command>\r\n" +
            "      <WorkingDirectory>@WORKDIR@</WorkingDirectory>\r\n" +
            "    </Exec>\r\n" +
            "  </Actions>\r\n" +
            "</Task>\r\n";

        private static bool Export(out string xml)
        {
            xml = null;
            int code;
            string output = Run("schtasks.exe", new string[] { "/Query", "/TN", FullPath, "/XML" }, out code, 8000);
            if (code != 0) { return false; }
            if (string.IsNullOrEmpty(output)) { return false; }
            xml = output;
            return true;
        }

        private static string Run(string exe, string[] args, out int exitCode, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++) { psi.Arguments += (i > 0 ? " " : "") + Quote(args[i]); }

            using (Process p = Process.Start(psi))
            {
                string so = p.StandardOutput.ReadToEnd();
                string se = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } exitCode = -1; return so + se; }
                exitCode = p.ExitCode;
                sb.Append(so);
                if (!string.IsNullOrEmpty(se)) { sb.Append(se); }
            }
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            if (s.IndexOf(' ') < 0 && s.IndexOf('\t') < 0) { return s; }
            return "\"" + s + "\"";
        }
    }
}
