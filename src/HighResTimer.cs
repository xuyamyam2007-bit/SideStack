using System;
using System.Runtime.InteropServices;

namespace SideStack
{
    internal static class HighResTimer
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uPeriod);

        private static bool held;

        public static void Require(bool on)
        {
            if (on == held) { return; }
            try
            {
                if (on)
                {
                    uint r = TimeBeginPeriod(1);
                    held = true;
                    Logger.Write("系统计时器精度 -> 1 ms（动画刷新率调度需要更细粒度，进程内常驻，返回 " + r + "）");
                }
                else
                {
                    uint r = TimeEndPeriod(1);
                    held = false;
                    Logger.Write("系统计时器精度 -> 恢复系统默认（返回 " + r + "）");
                }
            }
            catch (Exception ex)
            {
                held = false;
                Logger.Write("HighResTimer.Require", ex);
            }
        }

        public static bool Held { get { return held; } }
    }
}
