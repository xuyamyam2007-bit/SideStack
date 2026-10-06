using System;
using System.Runtime.InteropServices;

namespace SideStack
{
    public static class DisplayInfo
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int MAX_MODE_SCAN = 512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        private static DEVMODE NewMode()
        {
            DEVMODE dm = new DEVMODE();
            dm.dmDeviceName = new string(new char[32]);
            dm.dmFormName = new string(new char[32]);
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return dm;
        }

        private static bool Valid(int hz)
        {
            return hz >= 23 && hz <= 480;
        }

        public static int GetCurrentRefreshRate()
        {
            try
            {
                DEVMODE dm = NewMode();
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) && Valid(dm.dmDisplayFrequency))
                {
                    return dm.dmDisplayFrequency;
                }
            }
            catch { }
            return 0;
        }

        public static int GetMaxRefreshRate()
        {
            int best = 0;
            int atRes = 0;
            int curW = 0;
            int curH = 0;
            int curHz = 0;
            try
            {
                DEVMODE cur = NewMode();
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref cur))
                {
                    curW = cur.dmPelsWidth;
                    curH = cur.dmPelsHeight;
                    if (Valid(cur.dmDisplayFrequency)) { curHz = cur.dmDisplayFrequency; }
                }

                for (int i = 0; i < MAX_MODE_SCAN; i++)
                {
                    DEVMODE m = NewMode();
                    if (!EnumDisplaySettings(null, i, ref m)) { break; }
                    int hz = m.dmDisplayFrequency;
                    if (!Valid(hz)) { continue; }
                    if (curW > 0 && curH > 0 && m.dmPelsWidth == curW && m.dmPelsHeight == curH && hz > atRes)
                    {
                        atRes = hz;
                    }
                    if (hz > best) { best = hz; }
                }
            }
            catch { }

            if (atRes > best) { best = atRes; }
            if (curHz > best) { best = curHz; }
            if (best < 1) { best = 60; }
            return best;
        }

        public static string Describe()
        {
            string res = "";
            int curHz = 0;
            try
            {
                DEVMODE dm = NewMode();
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    res = dm.dmPelsWidth + "×" + dm.dmPelsHeight;
                    if (Valid(dm.dmDisplayFrequency)) { curHz = dm.dmDisplayFrequency; }
                }
            }
            catch { }

            int max = GetMaxRefreshRate();
            string s = "主显示器";
            if (res.Length > 0) { s += " " + res; }
            if (curHz > 0) { s += "，当前 " + curHz + " Hz"; }
            s += "，最高 " + max + " Hz";
            return s;
        }
    }
}
