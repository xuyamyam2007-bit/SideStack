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

        // Windows 任务计划 <Priority>：0 最高、10 最低。
        // 2/3 = ABOVE_NORMAL_PRIORITY_CLASS，4/5/6 = NORMAL_PRIORITY_CLASS，
        // 7/8 = BELOW_NORMAL，9/10 = IDLE（0=REALTIME，1=HIGH）。
        public const int PriorityBoostOn = 3;     // ABOVE_NORMAL
        public const int PriorityBoostOff = 6;    // NORMAL

        private static string StateFile
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SideStack");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "startup-state.txt");
            }
        }

        private static bool? recordedTask;
        private static bool? recordedPriority;

        private static void LoadRecorded()
        {
            if (recordedTask.HasValue && recordedPriority.HasValue) { return; }
            try
            {
                string f = StateFile;
                if (!File.Exists(f)) { return; }
                foreach (string line in File.ReadAllLines(f))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) { continue; }
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "task") { recordedTask = (v == "1"); }
                    else if (k == "priority") { recordedPriority = (v == "1"); }
                }
            }
            catch (Exception ex) { Logger.Write("StartupTask.LoadRecorded", ex); }
        }

        private static void SaveRecorded()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                if (recordedTask.HasValue) { sb.AppendLine("task=" + (recordedTask.Value ? "1" : "0")); }
                if (recordedPriority.HasValue) { sb.AppendLine("priority=" + (recordedPriority.Value ? "1" : "0")); }
                File.WriteAllText(StateFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Logger.Write("StartupTask.SaveRecorded", ex); }
        }

        public static void RememberTask(bool on)
        {
            LoadRecorded();
            if (recordedTask.HasValue && recordedTask.Value == on) { return; }
            recordedTask = on;
            SaveRecorded();
        }

        public static void RememberPriority(bool on)
        {
            LoadRecorded();
            InvalidatePriorityCache();
            if (recordedPriority.HasValue && recordedPriority.Value == on) { return; }
            recordedPriority = on;
            SaveRecorded();
        }

        public static bool SetProcessPriority(bool high)
        {
            try
            {
                System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess();
                System.Diagnostics.ProcessPriorityClass want = high
                    ? System.Diagnostics.ProcessPriorityClass.AboveNormal
                    : System.Diagnostics.ProcessPriorityClass.Normal;
                if (p.PriorityClass == want) { return true; }
                p.PriorityClass = want;
                Logger.Write("进程优先级 -> " + p.PriorityClass + "（BasePriority " + p.BasePriority + "）");
                return true;
            }
            catch (Exception ex) { Logger.Write("StartupTask.SetProcessPriority", ex); return false; }
        }

        public static bool BoostProcess()
        {
            return SetProcessPriority(true);
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

        private static int existsCacheTick = -1;
        private static bool existsCacheValue;

        private static void InvalidateExistsCache()
        {
            existsCacheTick = -1;
        }

        public static bool Exists()
        {
            int now = Environment.TickCount;
            if (existsCacheTick >= 0 && unchecked(now - existsCacheTick) >= 0 && unchecked(now - existsCacheTick) < 2000)
            {
                return existsCacheValue;
            }

            bool result;
            try
            {
                int code;
                string output = Run("schtasks.exe", new string[] { "/Query", "/TN", FullPath }, out code, 8000);
                if (code == 0) { RememberTask(true); result = true; }
                else if (code == 1) { RememberTask(false); result = false; }
                else
                {
                    Logger.Write("查询计划任务失败（退出码 " + code + "）：" + (output ?? "").Trim());
                    LoadRecorded();
                    result = recordedTask.HasValue && recordedTask.Value;
                }
            }
            catch (Exception ex)
            {
                Logger.Write("StartupTask.Exists", ex);
                LoadRecorded();
                result = recordedTask.HasValue && recordedTask.Value;
            }

            existsCacheTick = now;
            existsCacheValue = result;
            return result;
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
            LoadRecorded();
            if (recordedPriority.HasValue) { return recordedPriority.Value; }

            int now = Environment.TickCount;
            if (priorityCacheTick >= 0 && unchecked(now - priorityCacheTick) >= 0 && unchecked(now - priorityCacheTick) < 2000)
            {
                return priorityCacheValue;
            }

            bool v;
            int p = GetPriority();
            if (p >= 1 && p <= 3) { RememberPriority(true); v = true; }        // HIGH / ABOVE_NORMAL
            else if (p > 3) { RememberPriority(false); v = false; }            // NORMAL / BELOW / IDLE
            else { v = IsProcessBoosted(); }                                   // 0 或读不到：按进程现状判断

            priorityCacheTick = now;
            priorityCacheValue = v;
            return v;
        }

        // 旧版本把“已提升”写成 <Priority>5</Priority>，而 5 实际是 NORMAL。
        // 升级后一次性改成 3，保住用户当初勾选的那一档。
        public static void MigrateLegacyPriority()
        {
            try
            {
                if (!Exists()) { return; }
                if (GetPriority() != 5) { return; }
                if (SetPriorityBoostIntent(true))
                {
                    Logger.Write("启动优先级：旧任务 <Priority>5 -> 3（ABOVE_NORMAL）");
                }
            }
            catch (Exception ex) { Logger.Write("StartupTask.MigrateLegacyPriority", ex); }
        }

        private static int priorityCacheTick = -1;
        private static bool priorityCacheValue;

        private static void InvalidatePriorityCache()
        {
            priorityCacheTick = -1;
        }

        public static bool SetPriorityBoostIntent(bool on)
        {
            RememberPriority(on);
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
                        Logger.Write("启动优先级：任务 XML 里找不到 <Settings>，只记录意图");
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
                InvalidateExistsCache();
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
                // 删除就是删除：以前这里会先 Create(false, exePath) 把任务重建一遍，
                // 结果是「取消开机自启」反而把任务装了回来。
                int code;
                string output = Run("schtasks.exe", new string[] { "/Delete", "/TN", FullPath, "/F" }, out code, 30000);
                Logger.Write("删除计划任务 -> 退出码 " + code + (code == 0 ? "" : "：" + output));
                InvalidateExistsCache();
                if (code == 0) { RememberTask(false); }
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
                InvalidateExistsCache();
                if (code == 0)
                {
                    RememberTask(true);
                    RememberPriority(raised);
                }
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
                // stdout / stderr 必须并发读：先读完 stdout 再读 stderr 会在任一侧写满管道时
                // 互相等待，WaitForExit(timeoutMs) 也就永远等不到。
                byte[][] so = new byte[1][];
                byte[][] se = new byte[1][];
                System.Threading.Thread tOut = new System.Threading.Thread(delegate () { so[0] = ReadAll(p.StandardOutput.BaseStream); });
                System.Threading.Thread tErr = new System.Threading.Thread(delegate () { se[0] = ReadAll(p.StandardError.BaseStream); });
                tOut.IsBackground = true;
                tErr.IsBackground = true;
                tOut.Start();
                tErr.Start();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    Logger.Write("子进程超时已终止: " + exe);
                    tOut.Join(2000);
                    tErr.Join(2000);
                    exitCode = -1;
                    return DecodeText(so[0]) + DecodeText(se[0]);
                }
                tOut.Join(2000);
                tErr.Join(2000);
                exitCode = p.ExitCode;
                sb.Append(DecodeText(so[0]));
                sb.Append(DecodeText(se[0]));
            }
            return sb.ToString();
        }

        private static byte[] ReadAll(Stream s)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); }
                return ms.ToArray();
            }
        }

        private static string DecodeText(byte[] b)
        {
            if (b == null || b.Length == 0) { return ""; }
            string s;
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { s = Encoding.Unicode.GetString(b, 2, b.Length - 2); }
            else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { s = Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); }
            else if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { s = Encoding.UTF8.GetString(b, 3, b.Length - 3); }
            else
            {
                int zeros = 0;
                for (int i = 0; i < b.Length; i++) { if (b[i] == 0) { zeros++; } }
                s = (zeros > b.Length / 8) ? Encoding.Unicode.GetString(b) : Encoding.UTF8.GetString(b);
            }
            return s.Replace("\0", "");
        }

        private static string Quote(string s)
        {
            if (s.IndexOf(' ') < 0 && s.IndexOf('\t') < 0) { return s; }
            return "\"" + s + "\"";
        }
    }
}
