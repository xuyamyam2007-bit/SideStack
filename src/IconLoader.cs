using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SideStack
{
    public static class IconLoader
    {
        private static double radiusRatio = 0.22;
        public static double RadiusRatio
        {
            get { return radiusRatio; }
            set
            {
                double v = value;
                if (v < 0) { v = 0; }
                if (v > 0.5) { v = 0.5; }
                if (Math.Abs(v - radiusRatio) > 1e-6)
                {
                    radiusRatio = v;
                    Cache.Clear();
                }
            }
        }

        private static List<string> noRound = new List<string>();

        public static void SetNoRound(List<string> paths)
        {
            List<string> next = (paths == null) ? new List<string>() : new List<string>(paths);
            if (SameSet(noRound, next)) { return; }
            noRound = next;
            Cache.Clear();
        }

        private static bool SameSet(List<string> a, List<string> b)
        {
            if (ReferenceEquals(a, b)) { return true; }
            if (a == null || b == null) { return false; }
            if (a.Count != b.Count) { return false; }
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) { return false; }
            }
            return true;
        }

        private static bool IsNoRound(string path)
        {
            if (noRound == null || noRound.Count == 0) { return false; }
            for (int i = 0; i < noRound.Count; i++)
            {
                if (string.Equals(noRound[i], path, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private sealed class Entry
        {
            public ImageSource Image;
            public double Ratio;
            public bool NoRound;
        }

        private static readonly Dictionary<string, Entry> Cache =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static void Clear()
        {
            Cache.Clear();
        }

        private static ImageSource appIcon;
        private static bool appIconTried;

        public static ImageSource AppIcon()
        {
            if (!appIconTried)
            {
                appIconTried = true;
                try
                {
                    System.Reflection.Assembly asm = System.Reflection.Assembly.GetEntryAssembly();
                    string exe = asm != null ? asm.Location : null;
                    if (string.IsNullOrEmpty(exe))
                    {
                        exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    }
                    using (Icon ic = Icon.ExtractAssociatedIcon(exe))
                    {
                        if (ic != null) { appIcon = FromHIcon(ic.Handle, true); }
                    }
                }
                catch (Exception ex) { Logger.Write("IconLoader.AppIcon", ex); }
            }
            return appIcon;
        }

        public static ImageSource GetImage(string path, int decodeWidth)
        {
            if (string.IsNullOrEmpty(path)) { return null; }
            try
            {
                if (!System.IO.File.Exists(path)) { return null; }
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodeWidth > 8) { bi.DecodePixelWidth = decodeWidth; }
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex) { Logger.Write("IconLoader.GetImage", ex); return null; }
        }

        public static ImageSource Get(string path)
        {
            if (string.IsNullOrEmpty(path)) { return null; }
            bool skip = IsNoRound(path);
            Entry cached;
            if (Cache.TryGetValue(path, out cached))
            {
                if (Math.Abs(cached.Ratio - radiusRatio) < 1e-6 && cached.NoRound == skip)
                {
                    return cached.Image;
                }
            }

            ImageSource img = null;
            try
            {
                img = Load(path, skip);
            }
            catch (Exception ex) { Logger.Write("IconLoader.Get(" + path + ")", ex); }

            Entry e = new Entry();
            e.Image = img;
            e.Ratio = radiusRatio;
            e.NoRound = skip;
            Cache[path] = e;
            return img;
        }

        private static ImageSource Load(string path, bool skipRound)
        {
            ImageSource img = FromImageList(path, NativeMethods.SHIL_JUMBO, skipRound);
            if (img != null) { return img; }

            img = FromImageList(path, NativeMethods.SHIL_EXTRALARGE, skipRound);
            if (img != null) { return img; }

            try
            {
                Icon ic = Icon.ExtractAssociatedIcon(path);
                if (ic != null)
                {
                    using (ic)
                    {
                        return FromHIcon(ic.Handle, skipRound);
                    }
                }
            }
            catch { }

            return null;
        }

        private static ImageSource FromImageList(string path, int listSize, bool skipRound)
        {
            try
            {
                SHFILEINFO shfi = new SHFILEINFO();
                int cb = Marshal.SizeOf(typeof(SHFILEINFO));
                IntPtr res = NativeMethods.SHGetFileInfo(
                    path, 0, ref shfi, cb,
                    NativeMethods.SHGFI_SYSICONINDEX | NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

                if (shfi.hIcon != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(shfi.hIcon);
                }
                if (res == IntPtr.Zero) { return null; }

                int index = shfi.iIcon;

                Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                IImageList list = null;
                int hr = NativeMethods.SHGetImageList(listSize, ref iid, out list);
                if (hr != 0 || list == null) { return null; }

                IntPtr hicon = IntPtr.Zero;
                hr = list.GetIcon(index, 1, out hicon);
                if (hr != 0 || hicon == IntPtr.Zero) { return null; }

                try { return FromHIcon(hicon, skipRound); }
                finally { NativeMethods.DestroyIcon(hicon); }
            }
            catch (Exception ex)
            {
                Logger.Write("FromImageList(size=" + listSize + "," + path + ")", ex);
                return null;
            }
        }

        private static ImageSource FromHIcon(IntPtr hicon, bool skipRound)
        {
            BitmapSource bs = Imaging.CreateBitmapSourceFromHIcon(
                hicon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return Finish(bs, skipRound);
        }

        private static ImageSource Finish(BitmapSource src, bool skipRound)
        {
            ImageSource trimmed = TrimTransparent(src);
            ImageSource rounded = skipRound ? trimmed : RoundCorners(trimmed);
            rounded.Freeze();
            return rounded;
        }

        private static ImageSource RoundCorners(ImageSource src)
        {
            if (radiusRatio <= 1e-6) { return src; }
            try
            {
                BitmapSource bs = src as BitmapSource;
                if (bs == null || bs.PixelWidth < 8 || bs.PixelHeight < 8) { return src; }

                int w = bs.PixelWidth;
                int h = bs.PixelHeight;
                int stride = w * 4;
                byte[] px = new byte[stride * h];
                BitmapSource work = bs;
                if (work.Format != PixelFormats.Bgra32 && work.Format != PixelFormats.Pbgra32)
                {
                    work = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                }
                work.CopyPixels(px, stride, 0);

                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        if (px[row + x * 4 + 3] <= 10) { continue; }
                        if (x < minX) { minX = x; }
                        if (x > maxX) { maxX = x; }
                        if (y < minY) { minY = y; }
                        if (y > maxY) { maxY = y; }
                    }
                }
                if (maxX < minX || maxY < minY) { return src; }

                double cw = maxX - minX + 1;
                double ch = maxY - minY + 1;
                double radius = radiusRatio * Math.Min(cw, ch);

                radius = Math.Max(0.0, Math.Min(radius, Math.Min(cw, ch) / 2.0 - 0.5));
                if (radius < 0.6) { return src; }
                double r2 = radius * radius;

                int left = minX, right = maxX, top = minY, bottom = maxY;
                for (int y = top; y <= bottom; y++)
                {
                    double dy = 0;
                    if (y < top + radius) { dy = (top + radius) - y; }
                    else if (y > bottom - radius) { dy = y - (bottom - radius); }
                    if (dy <= 0) { continue; }

                    int row = y * stride;
                    for (int x = left; x <= right; x++)
                    {
                        double dx = 0;
                        if (x < left + radius) { dx = (left + radius) - x; }
                        else if (x > right - radius) { dx = x - (right - radius); }
                        else { continue; }

                        int i = row + x * 4;
                        byte a = px[i + 3];
                        if (a == 0) { continue; }

                        double d2 = dx * dx + dy * dy;
                        if (d2 > r2) { px[i + 3] = 0; continue; }

                        if (d2 > (radius - 1.0) * (radius - 1.0))
                        {
                            double d = Math.Sqrt(d2);
                            if (d > radius - 1.0)
                            {
                                px[i + 3] = (byte)Math.Round(a * Math.Max(0.0, radius - d));
                            }
                        }
                    }
                }

                WriteableBitmap outBmp = new WriteableBitmap(w, h, bs.DpiX, bs.DpiY, PixelFormats.Bgra32, null);
                outBmp.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);
                return outBmp;
            }
            catch (Exception ex)
            {
                Logger.Write("IconLoader.RoundCorners", ex);
                return src;
            }
        }

        private static ImageSource TrimTransparent(BitmapSource src)
        {
            try
            {
                int w = src.PixelWidth;
                int h = src.PixelHeight;
                if (w < 8 || h < 8) { return src; }

                BitmapSource probe = src;
                if (w > 256 || h > 256)
                {
                    double k = 256.0 / Math.Max(w, h);
                    probe = new TransformedBitmap(src, new ScaleTransform(k, k));
                }

                FormatConvertedBitmap fcb = new FormatConvertedBitmap(probe, PixelFormats.Bgra32, null, 0);
                int pw = fcb.PixelWidth;
                int ph = fcb.PixelHeight;
                int stride = pw * 4;
                byte[] px = new byte[stride * ph];
                fcb.CopyPixels(px, stride, 0);

                int minX = pw, minY = ph, maxX = -1, maxY = -1;
                for (int y = 0; y < ph; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < pw; x++)
                    {
                        if (px[row + x * 4 + 3] <= 10) { continue; }
                        if (x < minX) { minX = x; }
                        if (x > maxX) { maxX = x; }
                        if (y < minY) { minY = y; }
                        if (y > maxY) { maxY = y; }
                    }
                }
                if (maxX < 0 || maxY < 0) { return src; }

                double sx = (double)w / pw;
                double sy = (double)h / ph;

                int cwRaw = (int)Math.Ceiling((maxX - minX + 1) * sx);
                int chRaw = (int)Math.Ceiling((maxY - minY + 1) * sy);

                int padX = (int)Math.Ceiling(cwRaw * 0.02);
                int padY = (int)Math.Ceiling(chRaw * 0.02);
                if (padX < 1) { padX = 1; }
                if (padY < 1) { padY = 1; }
                if (padX > 6) { padX = 6; }
                if (padY > 6) { padY = 6; }

                int cx = (int)Math.Floor(minX * sx) - padX;
                int cy = (int)Math.Floor(minY * sy) - padY;
                int cw = cwRaw + padX * 2;
                int ch = chRaw + padY * 2;

                if (cx < 0) { cw += cx; cx = 0; }
                if (cy < 0) { ch += cy; cy = 0; }
                if (cx + cw > w) { cw = w - cx; }
                if (cy + ch > h) { ch = h - cy; }
                if (cw < 4 || ch < 4) { return src; }

                if (cw >= w - 2 && ch >= h - 2) { return src; }

                return new CroppedBitmap(src, new Int32Rect(cx, cy, cw, ch));
            }
            catch (Exception ex)
            {
                Logger.Write("TrimTransparent", ex);
                return src;
            }
        }
    }
}
