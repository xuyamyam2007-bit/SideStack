using System;
using System.IO;
using System.Text;

namespace SideStack
{
    public static class Logger
    {
        public static string Path = "";
        private static readonly object Gate = new object();

        public static void Init(string p)
        {
            Path = p;
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

        public static void Write(string tag, Exception ex)
        {
            Write(tag + " EX " + ex.GetType().Name + ": " + ex.Message + "\r\n" + ex.StackTrace);
        }
    }
}
