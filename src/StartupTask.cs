using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SideStack
{
    /// <summary>
    /// 管理"计划任务版"的自启通道（登录时启动，可设置进程优先级）。
    ///
    /// 背景：HKCU\...\Run 由 Explorer 在登录后较晚才处理，面板因此出现得晚；任务计划程序
    /// 的"登录时"触发器由计划服务启动、并且可以设置进程优先级（Run 项没有这个能力）。
    ///
    /// 实现选择：不引 COM（TaskScheduler 接口签名长、易错），也不依赖 PowerShell 模块，
    /// 而是走 schtasks.exe 的官方 XML 通道：
    ///   导出 schtasks /Query /TN <路径> /XML  ->  改 <Priority>  ->  schtasks /Create /XML 覆盖
    /// 往返已实测：动作、触发器、运行身份、其余设置都保持不变，只有优先级变。
    /// </summary>
    internal static class StartupTask
    {
        /// <summary>任务所在文件夹与名字（任务计划程序里显示为 SideStack\SideStack）</summary>
        public const string Folder = @"\SideStack\";
        public const string Name = "SideStack";
        private static string FullPath { get { return Folder + Name; } }

        /// <summary>任务 XML 里的优先级：4 = 高于正常，5 = 正常（Windows 的 0~10 优先级刻度）</summary>
        public const int PriorityHigh = 4;
        public const int PriorityNormal = 5;

        /// <summary>任务是否存在（= 已由计划任务接管登录自启）</summary>
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

        /// <summary>读当前优先级；任务不存在或读取失败返回 0</summary>
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

        /// <summary>优先级是否处于"高于正常"</summary>
        public static bool IsPriorityRaised()
        {
            int p = GetPriority();
            return p > 0 && p <= PriorityHigh;
        }

        /// <summary>把任务的优先级设为 高于正常(true) / 正常(false)</summary>
        public static bool SetPriorityRaised(bool raised)
        {
            try
            {
                string xml;
                if (!Export(out xml)) { return false; }
                int want = raised ? PriorityHigh : PriorityNormal;
                string patched;
                if (Regex.IsMatch(xml, @"<Priority>\d+</Priority>"))
                {
                    patched = Regex.Replace(xml, @"<Priority>\d+</Priority>", "<Priority>" + want + "</Priority>");
                }
                else
                {
                    // 老版本任务可能没有该节点：补在 <Settings> 之后，Windows 允许这个顺序
                    patched = Regex.Replace(xml, @"(<Settings[^>]*>)", "$1<Priority>" + want + "</Priority>", RegexOptions.None);
                    if (patched == xml)
                    {
                        Logger.Write("启动优先级：任务 XML 里找不到 <Settings>，无法写入");
                        return false;
                    }
                }

                string tmp = Path.Combine(Path.GetTempPath(), "sidestack-task-priority.xml");
                // schtasks 需要 Unicode（UTF-16）XML 文件，且声明里带 encoding="UTF-16"
                File.WriteAllText(tmp, patched, new UnicodeEncoding(false, true));

                int code;
                string output = Run("schtasks.exe", new string[] { "/Create", "/TN", FullPath, "/XML", tmp, "/F" }, out code, 30000);
                try { File.Delete(tmp); } catch { }

                Logger.Write("启动优先级 -> " + (raised ? "高于正常" : "正常") + "（schtasks 退出码 " + code + "）"
                             + (code == 0 ? "" : "：" + output));
                return code == 0;
            }
            catch (Exception ex) { Logger.Write("StartupTask.SetPriorityRaised", ex); return false; }
        }

        /// <summary>
        /// 供 /TR 使用的"命令"文本：exe 全路径（含空格时加引号）。
        /// 用固定模板建任务时也能带上它来固定 exe 路径。
        /// </summary>
        public static string CommandFor(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) { return ""; }
            return (exePath.IndexOf(' ') >= 0) ? ("\"" + exePath + "\"") : exePath;
        }

        /// <summary>
        /// 删除任务。传入 exePath 时会先用固定模板把任务覆盖成"指向该 exe"的形式，
        /// 避免任务里记着旧路径；随后删除。删除失败（例如被安全软件拦住）返回 false。
        /// </summary>
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

        /// <summary>删除任务（不指定 exe）</summary>
        public static bool Delete()
        {
            return Delete(null);
        }

        /// <summary>
        /// 按给定优先级创建（或覆盖）登录自启任务：当前用户、交互式、不需要管理员权限，
        /// 允许电池供电时启动、不因空闲停止、不限执行时长（常驻程序）、失败重启 3 次。
        /// 用固定模板而不是改现有任务，保证用户机器上无论之前是什么状态都能得到一致结果。
        /// </summary>
        public static bool Create(bool raised, string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath)) { return false; }
                string user = Environment.UserName;
                string workDir = Path.GetDirectoryName(exePath);
                int prio = raised ? PriorityHigh : PriorityNormal;

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

        /// <summary>
        /// 任务定义模板。Priority：4 = 高于正常、5 = 正常（Windows 的 0~10 刻度）。
        /// 其余按"礼貌但可靠"取值：电池也启动、不因空闲停止、不限时长、失败重启 3 次。
        /// </summary>
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

        // ------------------------------------------------------------------

        private static bool Export(out string xml)
        {
            xml = null;
            int code;
            string output = Run("schtasks.exe", new string[] { "/Query", "/TN", FullPath, "/XML" }, out code, 8000);
            if (code != 0) { return false; }          // 任务不存在（或没有权限）
            if (string.IsNullOrEmpty(output)) { return false; }
            xml = output;
            return true;
        }

        /// <summary>跑一个控制台程序并取回输出（不弹窗口、不经过 shell）</summary>
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
