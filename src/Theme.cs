using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VramMonitor
{
    internal static class UiTheme
    {
        public static readonly Color Bg = Color.FromArgb(0x1A, 0x1B, 0x1E);
        public static readonly Color Panel = Color.FromArgb(0x21, 0x22, 0x26);
        public static readonly Color PanelHi = Color.FromArgb(0x28, 0x2A, 0x2F);
        public static readonly Color RowAlt = Color.FromArgb(0x1F, 0x20, 0x24);
        public static readonly Color HeaderBg = Color.FromArgb(0x15, 0x16, 0x1A);
        public static readonly Color Grid = Color.FromArgb(0x2A, 0x2C, 0x31);
        public static readonly Color Text = Color.FromArgb(0xE7, 0xE8, 0xEA);
        public static readonly Color TextDim = Color.FromArgb(0x9A, 0xA0, 0xA6);
        public static readonly Color Accent = Color.FromArgb(0x9A, 0x7B, 0xFF);
        public static readonly Color AccentDim = Color.FromArgb(0x4A, 0x3B, 0x80);
        public static readonly Color SharedClr = Color.FromArgb(0x4F, 0xC3, 0xF7);
        public static readonly Color SharedDim = Color.FromArgb(0x25, 0x5E, 0x78);
        public static readonly Color Ok = Color.FromArgb(0x66, 0xD1, 0x9E);
        public static readonly Color Warn = Color.FromArgb(0xF2, 0xC1, 0x4E);
        public static readonly Color Danger = Color.FromArgb(0xE5, 0x64, 0x6E);
        public static readonly Color Critical = Color.FromArgb(0xFF, 0x4D, 0x57);
        public static readonly Color Donate = Color.FromArgb(0xFF, 0x6B, 0x9D);
        public static readonly Color Selection = Color.FromArgb(0x33, 0x2F, 0x55);
        public static readonly Color SelectionEdge = Color.FromArgb(0x6E, 0x5A, 0xC8);

        public static Color RiskColor(RiskLevel r)
        {
            switch (r)
            {
                case RiskLevel.Critical: return Critical;
                case RiskLevel.System: return Warn;
                case RiskLevel.Elevated: return Color.FromArgb(0xF0, 0x9A, 0x4E);
                default: return Ok;
            }
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        /// <summary>Barras de rolagem escuras em ListView (Win10 1809+).</summary>
        public static void ApplyDarkScrollbars(Control c)
        {
            try
            {
                if (c != null && c.IsHandleCreated)
                    SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            }
            catch (Exception) { }
        }

        public static void FillRounded(Graphics g, Rectangle r, Color c, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (radius * 2 > r.Height) radius = r.Height / 2;
            if (radius <= 0)
            {
                using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, r);
                return;
            }
            using (GraphicsPath p = new GraphicsPath())
            {
                int d = radius * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
            }
        }

        /// <summary>Barra de progresso simples (trilha + preenchimento).</summary>
        public static void DrawBar(Graphics g, Rectangle r, double fraction, Color fill, Color track)
        {
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            FillRounded(g, r, track, 3);
            int w = (int)Math.Round(r.Width * fraction);
            if (w > 0)
            {
                if (w < 3) w = 3;
                FillRounded(g, new Rectangle(r.X, r.Y, w, r.Height), fill, 3);
            }
        }

        /// <summary>Barra com dois segmentos empilhados (dedicada + compartilhada).</summary>
        public static void DrawStackedBar(Graphics g, Rectangle r, double f1, double f2,
                                          Color c1, Color c2, Color track)
        {
            if (f1 < 0) f1 = 0;
            if (f2 < 0) f2 = 0;
            double total = f1 + f2;
            if (total > 1)
            {
                f1 /= total;
                f2 /= total;
            }
            FillRounded(g, r, track, 3);
            int w1 = (int)Math.Round(r.Width * f1);
            int w2 = (int)Math.Round(r.Width * f2);
            if (w1 + w2 > r.Width) w2 = r.Width - w1;
            if (w1 > 0) FillRounded(g, new Rectangle(r.X, r.Y, Math.Max(w1, 2), r.Height), c1, 3);
            if (w2 > 0) FillRounded(g, new Rectangle(r.X + w1, r.Y, Math.Max(w2, 2), r.Height), c2, 3);
        }
    }

    /// <summary>
    /// Escala de DPI. O app e system-DPI-aware (ver app.manifest), portanto todo literal
    /// em pixels precisa passar por S(). Fontes ficam em pontos e o GDI ja as escala.
    /// </summary>
    internal static class Dpi
    {
        private static float _scale;

        public static float Scale
        {
            get
            {
                if (_scale <= 0f)
                {
                    try
                    {
                        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                            _scale = g.DpiX / 96f;
                    }
                    catch (Exception)
                    {
                        _scale = 1f;
                    }
                    if (_scale <= 0f) _scale = 1f;
                }
                return _scale;
            }
        }

        public static int S(int v)
        {
            return (int)Math.Round(v * Scale);
        }

        public static int S(double v)
        {
            return (int)Math.Round(v * Scale);
        }
    }

    internal static class Fmt
    {
        private const double KB = 1024.0;
        private const double MB = 1024.0 * 1024.0;
        private const double GB = 1024.0 * 1024.0 * 1024.0;

        public static string Bytes(long b)
        {
            if (b <= 0) return "—";
            if (b >= GB) return (b / GB).ToString("N2", CultureInfo.CurrentCulture) + " GB";
            if (b >= MB) return (b / MB).ToString("N1", CultureInfo.CurrentCulture) + " MB";
            if (b >= KB) return (b / KB).ToString("N0", CultureInfo.CurrentCulture) + " KB";
            return b.ToString(CultureInfo.CurrentCulture) + " B";
        }

        public static string Gb(long b)
        {
            return (b / GB).ToString("N1", CultureInfo.CurrentCulture);
        }

        public static string Percent(double v)
        {
            if (v <= 0.05) return "—";
            return v.ToString("N1", CultureInfo.CurrentCulture) + "%";
        }
    }

    /// <summary>ListView com double-buffer para desenho customizado sem tremida.</summary>
    internal sealed class DarkListView : ListView
    {
        public DarkListView()
        {
            // Apenas estes dois: UserPaint impediria o controle nativo de se desenhar.
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            View = View.Details;
            FullRowSelect = true;
            MultiSelect = false;
            HideSelection = false;
            OwnerDraw = true;
            BorderStyle = BorderStyle.None;
            HeaderStyle = ColumnHeaderStyle.Clickable;
            BackColor = UiTheme.Bg;
            ForeColor = UiTheme.Text;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UiTheme.ApplyDarkScrollbars(this);
        }
    }
}
