using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace VramMonitor
{
    /// <summary>
    /// Cartões com o estado real de cada adaptador: VRAM dedicada, memória compartilhada
    /// (que faz parte do total de memória da GPU) e o total combinado.
    /// </summary>
    internal sealed class AdapterHeader : Control
    {
        private List<GpuAdapter> _adapters = new List<GpuAdapter>();
        private readonly Font _fTitle;
        private readonly Font _fLabel;
        private readonly Font _fValue;

        public AdapterHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Bg;
            _fTitle = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            _fLabel = new Font("Segoe UI", 8.25f);
            _fValue = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            Height = Dpi.S(112);
        }

        public void SetData(List<GpuAdapter> adapters)
        {
            _adapters = adapters ?? new List<GpuAdapter>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush bg = new SolidBrush(UiTheme.Bg))
                g.FillRectangle(bg, ClientRectangle);

            List<GpuAdapter> show = new List<GpuAdapter>();
            for (int i = 0; i < _adapters.Count; i++)
            {
                GpuAdapter a = _adapters[i];
                if (a.DedicatedTotal > 0 || a.DedicatedUsed > 0 || a.SharedUsed > 0)
                    show.Add(a);
            }

            if (show.Count == 0)
            {
                using (SolidBrush br = new SolidBrush(UiTheme.TextDim))
                    g.DrawString(I18n.T("adapter.none"), _fLabel, br, Dpi.S(12), Dpi.S(12));
                return;
            }

            int margin = Dpi.S(8);
            int gap = Dpi.S(8);
            int cards = Math.Min(show.Count, 3);
            int cardW = (ClientSize.Width - margin * 2 - gap * (cards - 1)) / cards;
            int cardH = ClientSize.Height - margin * 2;

            for (int i = 0; i < cards; i++)
            {
                Rectangle r = new Rectangle(margin + i * (cardW + gap), margin, cardW, cardH);
                DrawCard(g, r, show[i]);
            }
        }

        private void DrawCard(Graphics g, Rectangle r, GpuAdapter a)
        {
            UiTheme.FillRounded(g, r, UiTheme.Panel, Dpi.S(6));

            long total = a.DedicatedTotal + a.SharedTotal;
            long used = a.DedicatedUsed + a.SharedUsed;
            double fDed = a.DedicatedTotal > 0 ? (double)a.DedicatedUsed / a.DedicatedTotal : 0;
            double fShr = a.SharedTotal > 0 ? (double)a.SharedUsed / a.SharedTotal : 0;

            int pad = Dpi.S(11);
            int x = r.X + pad;
            int right = r.Right - pad;

            using (SolidBrush br = new SolidBrush(UiTheme.Text))
                g.DrawString(a.Label, _fTitle, br, x, r.Y + Dpi.S(6));

            if (total > 0)
            {
                string tot = I18n.T("adapter.gpuMemory") + "  " +
                             Fmt.Gb(used) + " / " + Fmt.Gb(total) + " GB";
                SizeF sz = g.MeasureString(tot, _fValue);
                using (SolidBrush br = new SolidBrush(UiTheme.Accent))
                    g.DrawString(tot, _fValue, br, right - sz.Width, r.Y + Dpi.S(8));
            }

            int y = r.Y + Dpi.S(36);
            DrawRow(g, x, right, y, I18n.T("adapter.dedicated"),
                    a.DedicatedUsed, a.DedicatedTotal, fDed, UiTheme.Accent, UiTheme.AccentDim);
            y += Dpi.S(30);
            DrawRow(g, x, right, y, I18n.T("adapter.shared"),
                    a.SharedUsed, a.SharedTotal, fShr, UiTheme.SharedClr, UiTheme.SharedDim);
        }

        private void DrawRow(Graphics g, int x, int right, int y, string label,
                             long used, long total, double frac, Color fill, Color track)
        {
            using (SolidBrush br = new SolidBrush(UiTheme.TextDim))
                g.DrawString(label, _fLabel, br, x, y);

            string val = total > 0
                ? Fmt.Bytes(used) + "  /  " + Fmt.Gb(total) + " GB   " +
                  (frac * 100.0).ToString("N0", CultureInfo.CurrentCulture) + "%"
                : Fmt.Bytes(used);

            SizeF sz = g.MeasureString(val, _fValue);
            using (SolidBrush br = new SolidBrush(frac >= 0.9 ? UiTheme.Warn : UiTheme.Text))
                g.DrawString(val, _fValue, br, right - sz.Width, y);

            int labelW = Dpi.S(96);
            Rectangle bar = new Rectangle(x + labelW, y + Dpi.S(19),
                                          right - x - labelW, Dpi.S(5));
            if (bar.Width > Dpi.S(20))
                UiTheme.DrawBar(g, bar, frac, fill, track);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fTitle != null) _fTitle.Dispose();
                if (_fLabel != null) _fLabel.Dispose();
                if (_fValue != null) _fValue.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
