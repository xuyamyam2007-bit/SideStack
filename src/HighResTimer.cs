using System;
using System.Runtime.InteropServices;

namespace SideStack
{
    // 持有/释放 timeBeginPeriod(1)，动画期间需要 1ms 计时精度。
    // 用引用计数：只有真正在跑的动画/光感线程才持有，空闲时把精度还给系统。
    internal static class HighResTimer
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uPeriod);

        private static readonly object Gate = new object();
        private static int refs;
        private static bool held;

        public static void Acquire()
        {
            lock (Gate)
            {
                refs++;
                if (held) { return; }
                try
                {
                    uint r = TimeBeginPeriod(1);
                    held = true;
                    Logger.Write("系统计时器精度 -> 1 ms（引用 " + refs + "，返回 " + r + "）");
                }
                catch (Exception ex)
                {
                    held = false;
                    Logger.Write("HighResTimer.Acquire", ex);
                }
            }
        }

        public static void Release()
        {
            lock (Gate)
            {
                if (refs > 0) { refs--; }
                if (refs > 0 || !held) { return; }
                try
                {
                    uint r = TimeEndPeriod(1);
                    held = false;
                    Logger.Write("系统计时器精度 -> 恢复系统默认（返回 " + r + "）");
                }
                catch (Exception ex)
                {
                    held = false;
                    Logger.Write("HighResTimer.Release", ex);
                }
            }
        }

        public static bool Held { get { lock (Gate) { return held; } } }
    }
}
