using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VramMonitor
{
    /// <summary>
    /// Gera o ícone da bandeja: número da % de VRAM dedicada + barrinha de preenchimento,
    /// colorido conforme a pressão de memória.
    /// </summary>
    internal static class TrayGauge
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Color ColorFor(int pct)
        {
            if (pct >= 95) return UiTheme.Critical;
            if (pct >= 85) return UiTheme.Warn;
            if (pct >= 60) return UiTheme.Accent;
            return UiTheme.Ok;
        }

        public static Icon Create(int pct, out IntPtr handle)
        {
            if (pct < 0) pct = 0;
            int shown = pct > 99 ? 99 : pct;
            Color c = ColorFor(pct);

            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    UiTheme.FillRounded(g, new Rectangle(0, 0, 32, 32),
                                        Color.FromArgb(225, 22, 23, 26), 7);
                    using (Pen p = new Pen(Color.FromArgb(90, c)))
                        g.DrawRectangle(p, 0, 0, 31, 31);

                    string txt = shown.ToString(CultureInfo.InvariantCulture);
                    using (Font f = new Font("Segoe UI", txt.Length > 1 ? 17f : 20f,
                                             FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush br = new SolidBrush(c))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(txt, f, br, new RectangleF(0, 0, 32, 26), sf);
                    }

                    // barra inferior
                    UiTheme.FillRounded(g, new Rectangle(3, 26, 26, 4),
                                        Color.FromArgb(70, 255, 255, 255), 2);
                    int w = (int)Math.Round(26.0 * Math.Min(100, pct) / 100.0);
                    if (w > 0)
                        UiTheme.FillRounded(g, new Rectangle(3, 26, Math.Max(2, w), 4), c, 2);
                }

                handle = bmp.GetHicon();
                return Icon.FromHandle(handle);
            }
        }

        public static void Destroy(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                try { DestroyIcon(handle); }
                catch (Exception) { }
            }
        }
    }
}
