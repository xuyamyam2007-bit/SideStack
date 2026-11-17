using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SideStack
{
    // 追加写 %APPDATA%\SideStack\sidestack.log。
    public static class Logger
    {
        public static string Path = "";
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, long> lastTick = new Dictionary<string, long>();

        // 单文件上限；超过就改名成 .1（只保留一份历史）。
        private const long RotateBytes = 2 * 1024 * 1024;

        public static void Init(string p)
        {
            Path = p;
            try
            {
                FileInfo fi = new FileInfo(p);
                if (fi.Exists && fi.Length > RotateBytes)
                {
                    string old = p + ".1";
                    try { if (File.Exists(old)) { File.Delete(old); } } catch { }
                    File.Move(p, old);
                }
            }
            catch { }
        }

        public static void Write(string msg)
        {
            if (string.IsNullOrEmpty(Path)) { return; }
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path, DateTime.Now.ToString("MM-dd HH:mm:ss.fff") + "  " + msg + "\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }

        // 高频日志按 key 节流：同一个 key 在 minMs 内只记一条。
        public static void WriteThrottled(string key, int minMs, string msg)
        {
            if (string.IsNullOrEmpty(Path)) { return; }
            lock (Gate)
            {
                long now = Clock.NowMs(), last;
                if (lastTick.TryGetValue(key, out last) && now - last >= 0 && now - last < minMs) { return; }
                lastTick[key] = now;
            }
            Write(msg);
        }

        public static void Write(string tag, Exception ex)
        {
            if (ex == null) { Write(tag); return; }

            // 把 InnerException 链也打出来，否则 TargetInvocationException 之类会把真正原因吞掉。
            StringBuilder sb = new StringBuilder();
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message).Append("\r\n");
            }
            sb.Append(ex.StackTrace);
            Write(tag + " EX " + sb);
        }
    }
}
