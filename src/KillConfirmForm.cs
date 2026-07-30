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
    /// Confirmação de encerramento com classificação de risco.
    /// Processos críticos do Windows são bloqueados; sistema/elevado exigem ciência explícita.
    /// </summary>
    internal sealed class KillConfirmForm : Form
    {
        private readonly ProcInfo _pi;
        private readonly GpuProcess _gp;
        private readonly bool _monitorElevated;
        private readonly List<string[]> _facts = new List<string[]>();

        private CheckBox _ack;
        private Button _kill;
        private readonly Font _fBold;
        private readonly Font _fNormal;
        private readonly Font _fMono;
        private readonly Font _fSmall;

        public KillConfirmForm(ProcInfo pi, GpuProcess gp, bool monitorElevated)
        {
            _pi = pi;
            _gp = gp;
            _monitorElevated = monitorElevated;

            _fBold = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
            _fNormal = new Font("Segoe UI", 9f);
            _fSmall = new Font("Segoe UI", 8.25f);
            _fMono = new Font("Consolas", 9.5f);

            BuildFacts();
            BuildUi();
        }

        public string TaskkillCommand
        {
            get { return "taskkill /F /PID " + _pi.Pid.ToString(CultureInfo.InvariantCulture); }
        }

        private void BuildFacts()
        {
            _facts.Add(new string[] { I18n.T("kill.path"), _pi.ExePath.Length > 0
                ? _pi.ExePath : I18n.T("kill.pathUnavailable") });
            if (_pi.FileDescription.Length > 0)
                _facts.Add(new string[] { I18n.T("kill.description"), _pi.FileDescription });
            if (_pi.Company.Length > 0)
                _facts.Add(new string[] { I18n.T("kill.vendor"), _pi.Company });
            _facts.Add(new string[] { I18n.T("kill.user"),
                _pi.User.Length > 0 ? _pi.User : I18n.T("kill.userUnknown") });
            _facts.Add(new string[] { I18n.T("kill.session"), _pi.SessionId == 0
                ? I18n.T("kill.session0")
                : _pi.SessionId.ToString(CultureInfo.InvariantCulture) });
            _facts.Add(new string[] { I18n.T("kill.elevation"), _pi.Elevated.HasValue
                ? (_pi.Elevated.Value ? I18n.T("kill.elevationYes") : I18n.T("kill.elevationNo"))
                : I18n.T("kill.elevationUnknown") });
            if (_pi.Services.Count > 0)
                _facts.Add(new string[] { I18n.T("kill.services"), _pi.ServicesText });

            if (_gp != null)
            {
                string vram = I18n.F("kill.freesValue", Fmt.Bytes(_gp.Local));
                if (_gp.NonLocal > 0)
                    vram += I18n.F("kill.freesShared", Fmt.Bytes(_gp.NonLocal));
                vram += I18n.F("kill.freesTotal", Fmt.Bytes(_gp.TotalResident));
                _facts.Add(new string[] { I18n.T("kill.frees"), vram });
                if (_gp.Committed > _gp.TotalResident)
                    _facts.Add(new string[] { I18n.T("kill.committed"),
                        I18n.F("kill.committedValue", Fmt.Bytes(_gp.Committed)) });
            }
        }

        private void BuildUi()
        {
            Text = I18n.F("kill.title", _pi.Pid);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UiTheme.Bg;
            ForeColor = UiTheme.Text;
            Font = _fNormal;
            ClientSize = new Size(Dpi.S(640), Dpi.S(100));

            bool blocked = _pi.Risk == RiskLevel.Critical;
            bool needsAck = _pi.Risk == RiskLevel.System || _pi.Risk == RiskLevel.Elevated;

            Panel info = new Panel();
            info.Location = new Point(0, 0);
            info.Width = ClientSize.Width;
            info.BackColor = UiTheme.Bg;
            info.Paint += InfoPaint;
            Controls.Add(info);

            int infoH = Dpi.S(96) + _facts.Count * Dpi.S(20) + Dpi.S(14);
            using (Graphics g = CreateGraphics())
            {
                SizeF sz = g.MeasureString(_pi.RiskNote, _fSmall, ClientSize.Width - Dpi.S(100));
                infoH += (int)Math.Ceiling(sz.Height) + Dpi.S(12);
            }
            info.Height = infoH;

            int y = infoH;
            int pad = Dpi.S(20);

            Label cmdLbl = new Label();
            cmdLbl.AutoSize = true;
            cmdLbl.ForeColor = UiTheme.TextDim;
            cmdLbl.Font = _fSmall;
            cmdLbl.Text = I18n.T("kill.command");
            cmdLbl.Location = new Point(pad, y);
            Controls.Add(cmdLbl);

            TextBox cmd = new TextBox();
            cmd.ReadOnly = true;
            cmd.Font = _fMono;
            cmd.BackColor = UiTheme.HeaderBg;
            cmd.ForeColor = UiTheme.Accent;
            cmd.BorderStyle = BorderStyle.FixedSingle;
            cmd.Text = TaskkillCommand;
            cmd.Location = new Point(pad, y + Dpi.S(20));
            cmd.Size = new Size(Dpi.S(300), Dpi.S(26));
            Controls.Add(cmd);

            Button copy = MakeButton(I18n.T("kill.copy"), pad + Dpi.S(310), y + Dpi.S(19), Dpi.S(88));
            copy.Click += delegate(object s, EventArgs e)
            {
                try { Clipboard.SetText(TaskkillCommand); } catch (Exception) { }
                copy.Text = I18n.T("kill.copied");
            };
            Controls.Add(copy);

            y += Dpi.S(56);

            if (blocked)
            {
                Label warn = new Label();
                warn.AutoSize = false;
                warn.Size = new Size(ClientSize.Width - pad * 2, Dpi.S(40));
                warn.Location = new Point(pad, y);
                warn.ForeColor = UiTheme.Critical;
                warn.Font = _fSmall;
                warn.Text = I18n.T("kill.blocked");
                Controls.Add(warn);
                y += Dpi.S(44);
            }
            else if (needsAck)
            {
                _ack = new CheckBox();
                _ack.AutoSize = false;
                _ack.Size = new Size(ClientSize.Width - pad * 2, Dpi.S(26));
                _ack.Location = new Point(pad, y);
                _ack.ForeColor = UiTheme.Warn;
                _ack.BackColor = UiTheme.Bg;
                _ack.FlatStyle = FlatStyle.Flat;
                _ack.Font = _fSmall;
                _ack.Text = I18n.T(_pi.Risk == RiskLevel.System ? "kill.ackSystem" : "kill.ackElevated");
                _ack.CheckedChanged += delegate(object s, EventArgs e)
                {
                    _kill.Enabled = _ack.Checked;
                    _kill.BackColor = _ack.Checked ? UiTheme.Danger : UiTheme.PanelHi;
                    _kill.ForeColor = _ack.Checked ? Color.White : UiTheme.TextDim;
                };
                Controls.Add(_ack);
                y += Dpi.S(32);
            }

            if (!_monitorElevated && needsAck && !blocked)
            {
                Label uac = new Label();
                uac.AutoSize = false;
                uac.Size = new Size(ClientSize.Width - pad * 2, Dpi.S(34));
                uac.Location = new Point(pad, y);
                uac.ForeColor = UiTheme.TextDim;
                uac.Font = _fSmall;
                uac.Text = I18n.T("kill.uacNote");
                Controls.Add(uac);
                y += Dpi.S(38);
            }

            y += Dpi.S(6);

            int btnW = Dpi.S(112);
            Button cancel = MakeButton(I18n.T(blocked ? "kill.close" : "kill.cancel"),
                                       ClientSize.Width - pad - btnW, y, btnW);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;

            if (!blocked)
            {
                int killW = Dpi.S(178);
                _kill = MakeButton(I18n.F("kill.doKill", _pi.Pid),
                                   ClientSize.Width - pad - btnW - Dpi.S(8) - killW, y, killW);
                _kill.DialogResult = DialogResult.OK;
                _kill.Enabled = !needsAck;
                _kill.BackColor = needsAck ? UiTheme.PanelHi : UiTheme.Danger;
                _kill.ForeColor = needsAck ? UiTheme.TextDim : Color.White;
                Controls.Add(_kill);
                if (!needsAck) AcceptButton = _kill;
            }

            ClientSize = new Size(ClientSize.Width, y + Dpi.S(48));
            info.Width = ClientSize.Width;
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, Dpi.S(32));
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = UiTheme.PanelHi;
            b.ForeColor = UiTheme.Text;
            b.FlatAppearance.BorderColor = UiTheme.Grid;
            b.FlatAppearance.MouseOverBackColor = UiTheme.Panel;
            b.UseVisualStyleBackColor = false;
            b.Font = _fSmall;
            return b;
        }

        private void InfoPaint(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color risk = UiTheme.RiskColor(_pi.Risk);

            using (SolidBrush br = new SolidBrush(risk))
                g.FillRectangle(br, 0, 0, Dpi.S(5), p.Height);

            int x = Dpi.S(20);
            using (SolidBrush br = new SolidBrush(UiTheme.Text))
                g.DrawString(_pi.Name, _fBold, br, x, Dpi.S(14));

            string tag;
            switch (_pi.Risk)
            {
                case RiskLevel.Critical: tag = I18n.T("kill.tagCritical"); break;
                case RiskLevel.System: tag = I18n.T("kill.tagSystem"); break;
                case RiskLevel.Elevated: tag = I18n.T("kill.tagElevated"); break;
                default: tag = I18n.T("kill.tagUser"); break;
            }
            using (SolidBrush br = new SolidBrush(risk))
            {
                SizeF tz = g.MeasureString(tag, _fSmall);
                Rectangle chip = new Rectangle(p.Width - Dpi.S(20) - (int)tz.Width - Dpi.S(18),
                                               Dpi.S(17), (int)tz.Width + Dpi.S(18), Dpi.S(22));
                UiTheme.FillRounded(g, chip, Color.FromArgb(46, risk), Dpi.S(11));
                g.DrawString(tag, _fSmall, br, chip.X + Dpi.S(9), chip.Y + Dpi.S(3));
            }

            using (SolidBrush br = new SolidBrush(UiTheme.TextDim))
                g.DrawString(I18n.F("kill.pid", _pi.Pid), _fNormal, br, x, Dpi.S(42));

            int y = Dpi.S(70);
            using (SolidBrush lb = new SolidBrush(UiTheme.TextDim))
            using (SolidBrush vb = new SolidBrush(UiTheme.Text))
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.Trimming = StringTrimming.EllipsisPath;
                for (int i = 0; i < _facts.Count; i++)
                {
                    g.DrawString(_facts[i][0], _fSmall, lb, x, y + Dpi.S(2));
                    RectangleF rv = new RectangleF(x + Dpi.S(100), y,
                                                   p.Width - x - Dpi.S(120), Dpi.S(19));
                    g.DrawString(_facts[i][1], _fSmall, vb, rv, sf);
                    y += Dpi.S(20);
                }
            }

            y += Dpi.S(8);
            using (SolidBrush br = new SolidBrush(risk))
                g.DrawString(_pi.RiskNote, _fSmall, br,
                             new RectangleF(x, y, p.Width - x - Dpi.S(20), p.Height - y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fBold != null) _fBold.Dispose();
                if (_fNormal != null) _fNormal.Dispose();
                if (_fMono != null) _fMono.Dispose();
                if (_fSmall != null) _fSmall.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
