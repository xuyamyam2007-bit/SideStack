using System;
using System.Threading.Tasks;

namespace SideStack
{
    public sealed class LensGlassOptions
    {
        public int Width;
        public int Height;

        public double DpiScale = 1.0;

        public double CornerRadius;

        public double Magnification = 1.08;

        public double RefractionHeight;

        public double RefractionAmount;

        public int SrcOffsetX;
        public int SrcOffsetY;

        public int SrcPanelW;
        public int SrcPanelH;

        public double MarginLeft;
        public double MarginRight;
        public double MarginTop;
        public double MarginBottom;

        public const int ScreenSideLeft = 0;

        public const int ScreenSideRight = 1;

        public const int ScreenSideNone = -1;

        public int ScreenSide = ScreenSideNone;

        public bool DepthEffect = true;

        public bool ChromaticAberration = true;

        public double InnerShadowRadius;

        public double InnerShadowAlpha = 0.15;

        public double HighlightWidth;

        public double HighlightAlpha = 0.12;

        public double HighlightAngle = 0.0;

        public double HighlightFalloff = 0.0;

        public double TintAlpha = 0.08;

        public static LensGlassOptions Sidebar(int w, int h, double dpiScale)
        {
            double shortSide = Math.Min(w, h);
            return new LensGlassOptions
            {
                Width = w,
                Height = h,
                DpiScale = dpiScale,
                CornerRadius = Math.Min(16.0 * dpiScale, shortSide * 0.5),

                Magnification = 1.08,
                RefractionHeight = Math.Max(2.0, shortSide / 12.0),
                RefractionAmount = Math.Max(4.0, shortSide / 4.0),
                DepthEffect = true,
                ChromaticAberration = true,
                InnerShadowRadius = 16.0 * dpiScale,
                HighlightWidth = 0.5 * dpiScale,

                TintAlpha = 0.0,
            };
        }

        public static LensGlassOptions SidebarStrong(int w, int h, double dpiScale)
        {
            LensGlassOptions o = Sidebar(w, h, dpiScale);
            double ss = Math.Min(w, h);

            o.HighlightAlpha = 0.20;
            o.HighlightAngle = Math.PI / 4.0;
            o.HighlightFalloff = 1.0;

            o.HighlightWidth = 1.6 * dpiScale;
            o.Magnification = 1.0;
            o.DepthEffect = false;
            o.RefractionHeight = Math.Max(2.0, 8.0 * dpiScale);
            o.RefractionAmount = Math.Max(4.0, Math.Min(24.0 * dpiScale, ss / 4.0));
            return o;
        }
    }

    public static class LensGlassRenderer
    {
        public static byte[] Render(byte[] src, int srcW, int srcH, LensGlassOptions o, int maxDop = 0,
                                    byte[] reuseDst = null)
        {
            int w = o.Width, h = o.Height;
            if (src == null || w <= 0 || h <= 0) { return null; }
            if (srcW <= 0) { srcW = w; }
            if (srcH <= 0) { srcH = h; }

            int panelSrcW = (o.SrcPanelW > 0) ? Math.Min(o.SrcPanelW, srcW) : srcW;
            int panelSrcH = (o.SrcPanelH > 0) ? Math.Min(o.SrcPanelH, srcH) : srcH;
            double offX = o.SrcOffsetX, offY = o.SrcOffsetY;
            double toSrcX = (double)panelSrcW / w;
            double toSrcY = (double)panelSrcH / h;

            byte[] dst = (reuseDst != null && reuseDst.Length == w * h * 4) ? reuseDst : new byte[w * h * 4];
            double hw = w * 0.5, hh = h * 0.5;
            double radius = Math.Max(0.0, Math.Min(o.CornerRadius, Math.Min(hw, hh)));
            double refrH = Math.Max(0.001, o.RefractionHeight);

            double amount = -o.RefractionAmount;
            double gradRadius = Math.Min(radius * 1.5, Math.Min(hw, hh));
            double m = (o.Magnification > 0.01) ? o.Magnification : 1.0;
            double invM = 1.0 / m;

            double shadowR = o.InnerShadowRadius;
            double shadowA = o.InnerShadowAlpha;
            double hiW = o.HighlightWidth;
            double hiWMin = Math.Max(0.5, hiW);
            double hiA = o.HighlightAlpha;
            double hiDir = o.HighlightFalloff;
            double hiNx = Math.Cos(o.HighlightAngle), hiNy = Math.Sin(o.HighlightAngle);
            double tintA = Math.Max(0.0, Math.Min(0.9, o.TintAlpha));

            Action<int> body = delegate (int y)
            {
                int row = y * w * 4;
                double cy = y + 0.5 - hh;
                for (int x = 0; x < w; x++)
                {
                    double cx = x + 0.5 - hw;
                    double sd = SdRoundedRect(cx, cy, hw, hh, radius);

                    float b, g, r, a;

                    if (-sd >= refrH || (o.ScreenSide != LensGlassOptions.ScreenSideNone
                                         && RefractBandOnScreenSide(o.ScreenSide, cx, cy, hw, hh, refrH)))
                    {
                        SampleMagnified(src, srcW, srcH, x + 0.5, y + 0.5, hw, hh, invM, toSrcX, toSrcY, offX, offY, out b, out g, out r, out a);
                    }
                    else
                    {
                        double sdc = Math.Min(sd, 0.0);
                        double t = 1.0 - (-sdc) / refrH;
                        double d = CircleMap(t) * amount;

                        double gx, gy;
                        GradSdRoundedRect(cx, cy, hw, hh, gradRadius, out gx, out gy);
                        if (o.DepthEffect)
                        {
                            double len = Math.Sqrt(cx * cx + cy * cy);
                            if (len > 1e-6) { gx += cx / len; gy += cy / len; }
                        }
                        double glen = Math.Sqrt(gx * gx + gy * gy);
                        if (glen > 1e-6) { gx /= glen; gy /= glen; }
                        else { gx = 0; gy = 0; }

                        double ddx = d * gx, ddy = d * gy;

                        double rx = (x + 0.5) + ddx;
                        double ry = (y + 0.5) + ddy;

                        if (!o.ChromaticAberration)
                        {
                            SampleMagnified(src, srcW, srcH, rx, ry, hw, hh, invM, toSrcX, toSrcY, offX, offY, out b, out g, out r, out a);
                        }
                        else
                        {
                            double disp = (cx * cy) / (hw * hh);
                            double dx = ddx * disp, dy = ddy * disp;
                            DispersionSample(src, srcW, srcH, rx, ry, dx, dy, hw, hh, invM, toSrcX, toSrcY, offX, offY,
                                             out b, out g, out r, out a);
                        }
                    }

                    if (shadowR > 0.5 && shadowA > 0.0 && sd < 0.0)
                    {
                        double k = Math.Min(1.0, -sd / shadowR);
                        double f = (1.0 - k) * (1.0 - k) * shadowA;
                        b = (float)(b * (1.0 - f));
                        g = (float)(g * (1.0 - f));
                        r = (float)(r * (1.0 - f));
                    }

                    if (tintA > 0.001)
                    {
                        b = (float)(b + (255.0 - b) * tintA);
                        g = (float)(g + (255.0 - g) * tintA);
                        r = (float)(r + (255.0 - r) * tintA);
                    }

                    if (hiW > 0.01 && hiA > 0.0 && sd <= 0.0)
                    {
                        double k = -sd / hiWMin;
                        if (k < 1.0)
                        {
                            double f = (1.0 - k) * hiA;

                            if (hiDir > 0.01)
                            {
                                double hgx, hgy;
                                GradSdRoundedRect(cx, cy, hw, hh, gradRadius, out hgx, out hgy);
                                double dd = hgx * hiNx + hgy * hiNy;
                                double ad = Math.Abs(dd);
                                f *= (hiDir == 1.0) ? ad : Math.Pow(ad, hiDir);
                            }
                            b = (float)(b + (255.0 - b) * f);
                            g = (float)(g + (255.0 - g) * f);
                            r = (float)(r + (255.0 - r) * f);
                        }
                    }

                    int i = row + x * 4;
                    dst[i] = ClampByte(b);
                    dst[i + 1] = ClampByte(g);
                    dst[i + 2] = ClampByte(r);
                    dst[i + 3] = 255;
                }
            };

            if (maxDop > 0)
            {
                System.Threading.Tasks.ParallelOptions po = new System.Threading.Tasks.ParallelOptions();
                po.MaxDegreeOfParallelism = maxDop;
                Parallel.For(0, h, po, body);
            }
            else
            {
                Parallel.For(0, h, body);
            }

            return dst;
        }

        private static bool RefractBandOnScreenSide(int screenSide, double cx, double cy,
                                                    double hw, double hh, double refrH)
        {
            double ax = Math.Abs(cx), ay = Math.Abs(cy);
            bool xBand = ax > hw - refrH;
            bool yBand = ay > hh - refrH;
            if (!xBand && !yBand) { return false; }

            return (xBand && (!yBand || ax >= ay))
                && ((screenSide == LensGlassOptions.ScreenSideLeft) ? (cx < 0.0) : (cx > 0.0));
        }

        private static double SdRoundedRect(double cx, double cy, double hw, double hh, double radius)
        {
            double qx = Math.Abs(cx) - (hw - radius);
            double qy = Math.Abs(cy) - (hh - radius);
            double outside = Math.Sqrt(Math.Max(qx, 0.0) * Math.Max(qx, 0.0) +
                                       Math.Max(qy, 0.0) * Math.Max(qy, 0.0)) - radius;
            double inside = Math.Min(Math.Max(qx, qy), 0.0);
            return outside + inside;
        }

        private static void GradSdRoundedRect(double cx, double cy, double hw, double hh, double radius,
                                              out double gx, out double gy)
        {
            double qx = Math.Abs(cx) - (hw - radius);
            double qy = Math.Abs(cy) - (hh - radius);
            double sx = cx >= 0 ? 1.0 : -1.0;
            double sy = cy >= 0 ? 1.0 : -1.0;
            if (qx >= 0.0 || qy >= 0.0)
            {
                double mx = Math.Max(qx, 0.0), my = Math.Max(qy, 0.0);
                double len = Math.Sqrt(mx * mx + my * my);
                if (len > 1e-6) { gx = sx * mx / len; gy = sy * my / len; }
                else { gx = 0; gy = 0; }
            }
            else
            {
                double gradX = (qx >= qy) ? 1.0 : 0.0;
                gx = sx * gradX;
                gy = sy * (1.0 - gradX);
            }
        }

        private static double CircleMap(double x)
        {
            if (x <= 0.0) { return 0.0; }
            if (x >= 1.0) { return 1.0; }
            return 1.0 - Math.Sqrt(1.0 - x * x);
        }

        private static void SampleMagnified(byte[] src, int w, int h, double x, double y,
                                            double hw, double hh, double invM,
                                            double toSrcX, double toSrcY, double offX, double offY,
                                            out float b, out float g, out float r, out float a)
        {
            double sx = offX + (hw + (x - hw) * invM) * toSrcX;
            double sy = offY + (hh + (y - hh) * invM) * toSrcY;
            BilinearClamp(src, w, h, sx, sy, out b, out g, out r, out a);
        }

        private static void DispersionSample(byte[] src, int w, int h, double rx, double ry,
                                             double dx, double dy, double hw, double hh, double invM,
                                             double toSrcX, double toSrcY, double offX, double offY,
                                             out float b, out float g, out float r, out float a)
        {
            float r0, g0, b0, a0;

            float rr = 0f, gg = 0f, bb = 0f, aa = 0f;

            SampleMagnified(src, w, h, rx + dx, ry + dy, hw, hh, invM, toSrcX, toSrcY, offX, offY, out b0, out g0, out r0, out a0);
            rr += r0 / 3.5f; aa += a0 / 7.0f;

            SampleMagnified(src, w, h, rx + dx * (2.0 / 3.0), ry + dy * (2.0 / 3.0), hw, hh, invM, toSrcX, toSrcY, offX, offY,
                            out b0, out g0, out r0, out a0);
            rr += r0 / 3.5f; gg += g0 / 7.0f; aa += a0 / 7.0f;

            SampleMagnified(src, w, h, rx + dx / 3.0, ry + dy / 3.0, hw, hh, invM, toSrcX, toSrcY, offX, offY,
                            out b0, out g0, out r0, out a0);
            rr += r0 / 3.5f; gg += g0 / 3.5f; aa += a0 / 7.0f;

            SampleMagnified(src, w, h, rx, ry, hw, hh, invM, toSrcX, toSrcY, offX, offY, out b0, out g0, out r0, out a0);
            gg += g0 / 3.5f; aa += a0 / 7.0f;

            SampleMagnified(src, w, h, rx - dx / 3.0, ry - dy / 3.0, hw, hh, invM, toSrcX, toSrcY, offX, offY,
                            out b0, out g0, out r0, out a0);
            gg += g0 / 3.5f; bb += b0 / 3.0f; aa += a0 / 7.0f;

            SampleMagnified(src, w, h, rx - dx * (2.0 / 3.0), ry - dy * (2.0 / 3.0), hw, hh, invM, toSrcX, toSrcY, offX, offY,
                            out b0, out g0, out r0, out a0);
            bb += b0 / 3.0f; aa += a0 / 7.0f;

            SampleMagnified(src, w, h, rx - dx, ry - dy, hw, hh, invM, toSrcX, toSrcY, offX, offY, out b0, out g0, out r0, out a0);
            rr += r0 / 7.0f; bb += b0 / 3.0f; aa += a0 / 7.0f;

            r = rr; g = gg; b = bb; a = aa;
        }

        private static void BilinearClamp(byte[] src, int w, int h, double fx, double fy,
                                          out float b, out float g, out float r, out float a)
        {
            if (fx < 0.5) { fx = 0.5; } else if (fx > w - 0.5) { fx = w - 0.5; }
            if (fy < 0.5) { fy = 0.5; } else if (fy > h - 0.5) { fy = h - 0.5; }

            double xf = fx - 0.5, yf = fy - 0.5;
            int x0 = (int)xf, y0 = (int)yf;
            if (x0 < 0) { x0 = 0; } else if (x0 > w - 1) { x0 = w - 1; }
            if (y0 < 0) { y0 = 0; } else if (y0 > h - 1) { y0 = h - 1; }
            int x1 = (x0 + 1 < w) ? x0 + 1 : x0;
            int y1 = (y0 + 1 < h) ? y0 + 1 : y0;
            double tx = xf - x0, ty = yf - y0;
            if (tx < 0.0) { tx = 0.0; } else if (tx > 1.0) { tx = 1.0; }
            if (ty < 0.0) { ty = 0.0; } else if (ty > 1.0) { ty = 1.0; }

            int i00 = (y0 * w + x0) * 4, i10 = (y0 * w + x1) * 4;
            int i01 = (y1 * w + x0) * 4, i11 = (y1 * w + x1) * 4;

            double w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty);
            double w01 = (1 - tx) * ty, w11 = tx * ty;

            b = (float)(src[i00] * w00 + src[i10] * w10 + src[i01] * w01 + src[i11] * w11);
            g = (float)(src[i00 + 1] * w00 + src[i10 + 1] * w10 + src[i01 + 1] * w01 + src[i11 + 1] * w11);
            r = (float)(src[i00 + 2] * w00 + src[i10 + 2] * w10 + src[i01 + 2] * w01 + src[i11 + 2] * w11);
            a = (float)(src[i00 + 3] * w00 + src[i10 + 3] * w10 + src[i01 + 3] * w01 + src[i11 + 3] * w11);
        }

        private static byte ClampByte(float v)
        {
            if (v <= 0f) { return 0; }
            if (v >= 255f) { return 255; }
            return (byte)(v + 0.5f);
        }
    }
}
