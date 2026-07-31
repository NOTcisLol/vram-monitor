using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VramMonitor
{
    internal sealed class Row
    {
        public GpuProcess Gp;
        public ProcInfo Pi;
        public long Local;
        public long NonLocal;
        public long Dedicated;
        public long Shared;
        public long Committed;
        public long Total;
    }

    internal sealed class MainForm : Form
    {
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private readonly GpuSampler _sampler = new GpuSampler();
        private readonly ProcessCatalog _catalog = new ProcessCatalog();
        private readonly bool _elevated = ProcessCatalog.IsCurrentProcessElevated();

        private readonly Timer _timer = new Timer();
        private Panel _bar;
        private AdapterHeader _header;
        private DarkListView _list;
        private DarkListView _segList;
        private Panel _detail;
        private Panel _status;
        private ComboBox _cbAdapter;
        private ComboBox _cbInterval;
        private TextBox _txtFilter;
        private CheckBox _chkOnlyGpu;
        private Button _btnPause;
        private Button _btnKill;
        private Button _btnElevate;
        private Button _btnHelp;
        private Button _btnDonate;
        private Button _btnLang;
        private Label _lblAdapter;
        private Label _lblFilter;
        private Label _lblInterval;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miKill;
        private ToolStripMenuItem _miCopyCmd;
        private ToolStripMenuItem _miCopyPid;
        private ToolStripMenuItem _miOpenLocation;
        private ToolStripMenuItem _miCopyPath;
        private ToolStripMenuItem _miTrayOpen;
        private ToolStripMenuItem _miTrayCopyJson;
        private ToolStripMenuItem _miTrayStartup;
        private ToolStripMenuItem _miTrayDonate;
        private ToolStripMenuItem _miTrayExit;
        private ContextMenuStrip _langMenu;
        private readonly Settings _settings = Settings.Load();

        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _miTrayDed;
        private ToolStripMenuItem _miTrayShr;
        private ToolStripMenuItem _miTrayTop;
        private ToolStripMenuItem _miTrayPause;
        private ToolStripMenuItem _miTrayJson;
        private IntPtr _trayHIcon = IntPtr.Zero;
        private int _lastTrayPct = -1;
        private bool _formIconSet;
        private bool _reallyExit;
        private bool _balloonShown;

        private GpuSnapshot _snap;
        private List<Row> _rows = new List<Row>();
        private List<int> _orderPids = new List<int>();
        private Row _selected;
        private bool _mouseOverList;
        private bool _orderFrozen;
        private bool _autoSelected;
        private bool _adapterComboDirty;
        private bool _exportJson = true;
        private string _jsonPath = SnapshotJson.DefaultPath;
        private int _sortColumn = 2;
        private bool _sortAsc;
        private bool _paused;
        private string _flash = "";
        private DateTime _flashUntil = DateTime.MinValue;
        private readonly Font _fSmall;
        private readonly Font _fBold;
        private readonly Font _fMono;

        public MainForm()
        {
            // Idioma antes de qualquer texto: sem preferência salva, o padrão é inglês.
            I18n.Init(I18n.Resolve(_settings.Language));
            _exportJson = _settings.ExportJson;

            _fSmall = new Font("Segoe UI", 8.25f);
            _fBold = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _fMono = new Font("Consolas", 8.5f);
            BuildUi();

            _timer.Tick += delegate(object s, EventArgs e) { Refresh_(); };
            _timer.Start();
            Refresh_();
        }

        // ------------------------------------------------------------------- UI
        private void BuildUi()
        {
            ClientSize = new Size(Dpi.S(1360), Dpi.S(830));
            MinimumSize = new Size(Dpi.S(1060), Dpi.S(640));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.Bg;
            ForeColor = UiTheme.Text;
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;

            // ---------------- barra de ferramentas
            // Só criação aqui: as posições saem de LayoutToolbar(), porque a largura dos
            // rótulos muda com o idioma e um layout fixo em pixels quebraria ao trocar.
            // Os controles são adicionados ao form no fim de BuildUi: no WinForms o último
            // Controls.Add fica na borda externa e o primeiro recebe o espaço restante.
            _bar = new Panel();
            _bar.Dock = DockStyle.Top;
            _bar.Height = Dpi.S(44);
            _bar.BackColor = UiTheme.HeaderBg;

            _lblAdapter = MakeLabel();
            _bar.Controls.Add(_lblAdapter);

            _cbAdapter = MakeCombo(Dpi.S(196));
            _cbAdapter.Items.Add(I18n.T("toolbar.allAdapters"));
            _cbAdapter.SelectedIndex = 0;
            _cbAdapter.SelectedIndexChanged += delegate(object s, EventArgs e) { Rebuild(); };
            _bar.Controls.Add(_cbAdapter);

            _lblFilter = MakeLabel();
            _bar.Controls.Add(_lblFilter);

            _txtFilter = new TextBox();
            _txtFilter.Width = Dpi.S(168);
            _txtFilter.BackColor = UiTheme.PanelHi;
            _txtFilter.ForeColor = UiTheme.Text;
            _txtFilter.BorderStyle = BorderStyle.FixedSingle;
            _txtFilter.Font = _fSmall;
            _txtFilter.TextChanged += delegate(object s, EventArgs e) { Rebuild(); };
            _bar.Controls.Add(_txtFilter);

            _chkOnlyGpu = new CheckBox();
            _chkOnlyGpu.Checked = _settings.OnlyGpu;
            _chkOnlyGpu.AutoSize = true;
            _chkOnlyGpu.ForeColor = UiTheme.TextDim;
            _chkOnlyGpu.FlatStyle = FlatStyle.Flat;
            _chkOnlyGpu.Font = _fSmall;
            _chkOnlyGpu.CheckedChanged += delegate(object s, EventArgs e)
            {
                _settings.OnlyGpu = _chkOnlyGpu.Checked;
                _settings.Save();
                Rebuild();
            };
            _bar.Controls.Add(_chkOnlyGpu);

            _lblInterval = MakeLabel();
            _bar.Controls.Add(_lblInterval);

            _cbInterval = MakeCombo(Dpi.S(76));
            _cbInterval.Items.AddRange(new object[] { "0,5 s", "1 s", "2 s", "5 s" });
            _cbInterval.SelectedIndex = IntervalIndex(_settings.IntervalMs);
            _timer.Interval = IntervalMs[_cbInterval.SelectedIndex];
            _cbInterval.SelectedIndexChanged += delegate(object s, EventArgs e)
            {
                _timer.Interval = IntervalMs[_cbInterval.SelectedIndex];
                _settings.IntervalMs = _timer.Interval;
                _settings.Save();
            };
            _bar.Controls.Add(_cbInterval);

            _btnPause = MakeButton("", 0, Dpi.S(9), Dpi.S(78));
            _btnPause.Click += delegate(object s, EventArgs e) { TogglePause(); };
            _bar.Controls.Add(_btnPause);

            _btnKill = MakeButton("", 0, Dpi.S(9), Dpi.S(150));
            _btnKill.Enabled = false;
            _btnKill.Click += delegate(object s, EventArgs e) { KillSelected(); };
            _bar.Controls.Add(_btnKill);

            _btnHelp = MakeButton("?", 0, Dpi.S(9), Dpi.S(30));
            _btnHelp.Click += delegate(object s, EventArgs e) { ShowHelp(); };
            _bar.Controls.Add(_btnHelp);

            _btnDonate = MakeButton("", 0, Dpi.S(9), Dpi.S(84));
            _btnDonate.ForeColor = UiTheme.Donate;
            _btnDonate.FlatAppearance.BorderColor = Color.FromArgb(90, UiTheme.Donate);
            _btnDonate.Click += delegate(object s, EventArgs e) { OpenDonate(); };
            _bar.Controls.Add(_btnDonate);

            // Botão compacto com o código do idioma ("EN ▾") em vez de uma combo larga.
            _btnLang = MakeButton("", 0, Dpi.S(9), Dpi.S(54));
            _btnLang.Click += delegate(object s, EventArgs e) { ShowLanguageMenu(); };
            _bar.Controls.Add(_btnLang);


            if (!_elevated)
            {
                _btnElevate = MakeButton("", 0, Dpi.S(9), Dpi.S(78));
                _btnElevate.ForeColor = UiTheme.Warn;
                _btnElevate.Click += delegate(object s, EventArgs e) { RelaunchElevated(); };
                _bar.Controls.Add(_btnElevate);
            }

            _bar.Resize += delegate(object s, EventArgs e) { LayoutToolbar(); };

            // ---------------- cartões de adaptador
            _header = new AdapterHeader();
            _header.Dock = DockStyle.Top;

            // ---------------- rodapé
            _status = new Panel();
            _status.Dock = DockStyle.Bottom;
            _status.Height = Dpi.S(26);
            _status.BackColor = UiTheme.HeaderBg;
            _status.Paint += StatusPaint;

            // ---------------- painel de detalhes
            _detail = new Panel();
            _detail.Dock = DockStyle.Bottom;
            _detail.Height = Dpi.S(200);
            _detail.BackColor = UiTheme.Bg;
            _detail.Paint += DetailPaint;

            _segList = new DarkListView();
            _segList.Font = _fSmall;
            _segList.SmallImageList = MakeRowSizer(20);
            _segList.Columns.Add("", Dpi.S(180), HorizontalAlignment.Left);
            _segList.Columns.Add("", Dpi.S(42), HorizontalAlignment.Center);
            _segList.Columns.Add("", Dpi.S(132), HorizontalAlignment.Right);
            _segList.Columns.Add("", Dpi.S(110), HorizontalAlignment.Right);
            _segList.Columns.Add("", Dpi.S(138), HorizontalAlignment.Right);
            _segList.Columns.Add("", Dpi.S(134), HorizontalAlignment.Right);
            _segList.DrawColumnHeader += HeaderDraw;
            _segList.DrawItem += RowBgDraw;
            _segList.DrawSubItem += SegSubDraw;
            _detail.Controls.Add(_segList);
            _detail.Resize += delegate(object s, EventArgs e) { LayoutDetail(); };

            // ---------------- lista principal
            _list = new DarkListView();
            _list.Font = _fSmall;
            _list.Dock = DockStyle.Fill;
            _list.SmallImageList = MakeRowSizer(22);
            _list.Columns.Add("", Dpi.S(60), HorizontalAlignment.Right);
            _list.Columns.Add("", Dpi.S(166), HorizontalAlignment.Left);
            _list.Columns.Add("", Dpi.S(136), HorizontalAlignment.Right);
            _list.Columns.Add("", Dpi.S(110), HorizontalAlignment.Right);
            _list.Columns.Add("", Dpi.S(100), HorizontalAlignment.Right);
            _list.Columns.Add("", Dpi.S(112), HorizontalAlignment.Right);
            _list.Columns.Add("", Dpi.S(62), HorizontalAlignment.Right);
            _list.Columns.Add("", Dpi.S(92), HorizontalAlignment.Left);
            _list.Columns.Add("", Dpi.S(96), HorizontalAlignment.Left);
            _list.Columns.Add("", Dpi.S(110), HorizontalAlignment.Left);
            _list.Columns.Add("", Dpi.S(250), HorizontalAlignment.Left);
            _list.DrawColumnHeader += HeaderDraw;
            _list.DrawItem += RowBgDraw;
            _list.DrawSubItem += MainSubDraw;
            _list.ColumnClick += delegate(object s, ColumnClickEventArgs e)
            {
                if (_sortColumn == e.Column) _sortAsc = !_sortAsc;
                else
                {
                    _sortColumn = e.Column;
                    _sortAsc = (e.Column == 1 || e.Column == 7 || e.Column == 9 || e.Column == 10);
                }
                Rebuild();
            };
            _list.SelectedIndexChanged += delegate(object s, EventArgs e) { OnSelectionChanged(); };
            _list.MouseDoubleClick += delegate(object s, MouseEventArgs e) { KillSelected(); };
            // Enquanto o ponteiro está sobre a lista (ou ela está rolada), a ordem congela:
            // sem reordenar, a posição do scroll e a linha sob o cursor param de fugir.
            _list.Resize += delegate(object s, EventArgs e) { StretchLastColumn(_list); };
            _segList.Resize += delegate(object s, EventArgs e) { StretchLastColumn(_segList); };
            _list.MouseEnter += delegate(object s, EventArgs e) { _mouseOverList = true; };
            _list.MouseLeave += delegate(object s, EventArgs e) { _mouseOverList = false; };

            // ordem de docking: Fill primeiro, bordas externas por último
            Controls.Add(_list);
            Controls.Add(_detail);
            Controls.Add(_status);
            Controls.Add(_header);
            Controls.Add(_bar);

            BuildMenu();
            _list.ContextMenuStrip = _menu;
            BuildTray();

            Resize += delegate(object s, EventArgs e)
            {
                if (WindowState == FormWindowState.Minimized) HideToTray(false);
            };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    HideToTray(true);
                }
            };

            ApplyTexts();

            Shown += delegate(object s, EventArgs e)
            {
                ApplyTexts();   // agora com handle criado: o cue banner do filtro precisa dele
                LayoutDetail();
                StretchLastColumn(_list);
                StretchLastColumn(_segList);
                if (WindowState == FormWindowState.Minimized) HideToTray(false);
                // Arquivo de idioma da comunidade com erro não pode falhar em silêncio.
                if (I18n.LoadErrors.Count > 0)
                {
                    MessageBox.Show(this,
                        I18n.F("dialog.langErrors", string.Join("\r\n", I18n.LoadErrors.ToArray())),
                        I18n.T("dialog.langErrorsTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    I18n.LoadErrors.Clear();
                }
                if (!_sampler.Ready)
                    MessageBox.Show(this, _sampler.InitError ?? I18n.T("dialog.countersUnavailable"),
                                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
        }

        /// <summary>ImageList só para fixar a altura das linhas de forma consistente com o DPI.</summary>
        private static ImageList MakeRowSizer(int logicalHeight)
        {
            ImageList il = new ImageList();
            il.ImageSize = new Size(1, Dpi.S(logicalHeight));
            return il;
        }

        private static readonly int[] IntervalMs = new int[] { 500, 1000, 2000, 5000 };

        private static int IntervalIndex(int ms)
        {
            for (int i = 0; i < IntervalMs.Length; i++)
                if (IntervalMs[i] == ms) return i;
            return 1;
        }

        /// <summary>
        /// Posiciona a barra inteira a partir das larguras atuais. Chamado ao construir, ao
        /// redimensionar e ao trocar de idioma — os rótulos mudam de tamanho em cada língua.
        /// </summary>
        private void LayoutToolbar()
        {
            if (_bar == null || _lblAdapter == null || _btnHelp == null) return;

            int gap = Dpi.S(8);
            int lblY = Dpi.S(14);
            int ctlY = Dpi.S(10);
            int btnY = Dpi.S(9);
            int x = Dpi.S(10);

            x = PlaceLabel(_lblAdapter, x, lblY) + Dpi.S(6);
            _cbAdapter.Location = new Point(x, ctlY);
            x += _cbAdapter.Width + Dpi.S(12);

            x = PlaceLabel(_lblFilter, x, lblY) + Dpi.S(6);
            _txtFilter.Location = new Point(x, ctlY);
            x += _txtFilter.Width + Dpi.S(12);

            _chkOnlyGpu.Location = new Point(x, Dpi.S(13));
            x += _chkOnlyGpu.PreferredSize.Width + Dpi.S(14);

            x = PlaceLabel(_lblInterval, x, lblY) + Dpi.S(6);
            _cbInterval.Location = new Point(x, ctlY);
            x += _cbInterval.Width + Dpi.S(12);

            _btnPause.Location = new Point(x, btnY);
            x += _btnPause.Width + gap;
            _btnKill.Location = new Point(x, btnY);
            x += _btnKill.Width + gap;
            int leftEnd = x;

            int right = _bar.ClientSize.Width - Dpi.S(12);
            _btnHelp.Left = right - _btnHelp.Width;
            _btnHelp.Top = btnY;
            right -= _btnHelp.Width + gap;

            _btnLang.Left = right - _btnLang.Width;
            _btnLang.Top = btnY;
            right -= _btnLang.Width + gap;

            _btnDonate.Left = right - _btnDonate.Width;
            _btnDonate.Top = btnY;
            right -= _btnDonate.Width + Dpi.S(10);

            if (_btnElevate != null)
            {
                _btnElevate.Left = right - _btnElevate.Width;
                _btnElevate.Top = btnY;
                right -= _btnElevate.Width + gap;
            }

            // Sobrou espaço para os botões da direita? Se não, some com "Matar" primeiro:
            // é o único da esquerda que tem rótulo variável e longo em alguns idiomas.
            _btnKill.Visible = leftEnd <= right;
        }

        /// <summary>
        /// Faz a última coluna preencher a largura restante. Sem isso, a faixa depois da
        /// última coluna fica com o cabeçalho branco do tema nativo no meio da UI escura.
        /// </summary>
        private static void StretchLastColumn(ListView lv)
        {
            if (lv == null || lv.Columns.Count == 0 || !lv.IsHandleCreated) return;
            int used = 0;
            for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
            int avail = lv.ClientSize.Width - used - 1;
            int min = Dpi.S(120);
            lv.Columns[lv.Columns.Count - 1].Width = avail > min ? avail : min;
        }

        private static int PlaceLabel(Label l, int x, int y)
        {
            l.Location = new Point(x, y);
            return x + l.PreferredWidth;
        }

        private Label MakeLabel()
        {
            Label l = new Label();
            l.AutoSize = true;
            l.ForeColor = UiTheme.TextDim;
            l.Font = _fSmall;
            return l;
        }

        private ComboBox MakeCombo(int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = UiTheme.PanelHi;
            c.ForeColor = UiTheme.Text;
            c.Font = _fSmall;
            c.Width = w;
            return c;
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, Dpi.S(27));
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = UiTheme.PanelHi;
            b.ForeColor = UiTheme.Text;
            b.FlatAppearance.BorderColor = UiTheme.Grid;
            b.FlatAppearance.MouseOverBackColor = UiTheme.Panel;
            b.UseVisualStyleBackColor = false;
            b.Font = _fSmall;
            return b;
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.BackColor = UiTheme.Panel;
            _menu.ForeColor = UiTheme.Text;
            _menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());

            _miKill = new ToolStripMenuItem("", null,
                delegate(object s, EventArgs e) { KillSelected(); });
            _miKill.ShortcutKeyDisplayString = "Del";
            _menu.Items.Add(_miKill);

            _miCopyCmd = new ToolStripMenuItem("", null,
                delegate(object s, EventArgs e) { CopyTaskkill(); });
            _miCopyCmd.ShortcutKeyDisplayString = "Ctrl+C";
            _menu.Items.Add(_miCopyCmd);

            _miCopyPid = new ToolStripMenuItem("", null, delegate(object s, EventArgs e)
            {
                if (_selected != null)
                    TrySetClipboard(_selected.Gp.Pid.ToString(CultureInfo.InvariantCulture));
            });
            _menu.Items.Add(_miCopyPid);

            _menu.Items.Add(new ToolStripSeparator());

            _miOpenLocation = new ToolStripMenuItem("", null, delegate(object s, EventArgs e)
            {
                if (_selected != null && _selected.Pi != null && _selected.Pi.ExePath.Length > 0)
                {
                    try { Process.Start("explorer.exe", "/select,\"" + _selected.Pi.ExePath + "\""); }
                    catch (Exception) { }
                }
            });
            _menu.Items.Add(_miOpenLocation);

            _miCopyPath = new ToolStripMenuItem("", null, delegate(object s, EventArgs e)
            {
                if (_selected != null && _selected.Pi != null)
                    TrySetClipboard(_selected.Pi.ExePath);
            });
            _menu.Items.Add(_miCopyPath);

            _menu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (_selected == null) { e.Cancel = true; return; }
                bool blocked = _selected.Pi != null && _selected.Pi.Risk == RiskLevel.Critical;
                _miKill.Text = I18n.F(blocked ? "menu.killBlocked" : "menu.killPid", _selected.Gp.Pid);
            };
        }

        // ---------------------------------------------------------------- idiomas
        /// <summary>
        /// Reaplica todo o texto da interface. Chamado ao construir e a cada troca de idioma;
        /// os painéis desenhados à mão leem o i18n no próprio Paint, então só precisam invalidar.
        /// </summary>
        private void ApplyTexts()
        {
            Text = AppInfo.NameWithVersion + " — " + I18n.T("app.subtitle");

            _lblAdapter.Text = I18n.T("toolbar.adapter");
            _lblFilter.Text = I18n.T("toolbar.filter");
            _lblInterval.Text = I18n.T("toolbar.interval");
            _chkOnlyGpu.Text = I18n.T("toolbar.onlyGpu");
            _btnPause.Text = I18n.T(_paused ? "toolbar.resume" : "toolbar.pause");
            _btnDonate.Text = I18n.T("toolbar.donate");
            _btnLang.Text = LangButtonText();
            if (_btnElevate != null) _btnElevate.Text = I18n.T("toolbar.elevate");
            UpdateKillButton();

            string[] cols = new string[] {
                "col.pid", "col.process", "col.dedicated", "col.shared", "col.totalGpu",
                "col.committed", "col.gpu", "col.engine", "col.type", "col.user", "col.detail" };
            for (int i = 0; i < cols.Length && i < _list.Columns.Count; i++)
                _list.Columns[i].Text = I18n.T(cols[i]);

            string[] segs = new string[] {
                "seg.adapter", "seg.segment", "seg.resident", "seg.shared",
                "seg.dedicatedCommitted", "seg.totalCommitted" };
            for (int i = 0; i < segs.Length && i < _segList.Columns.Count; i++)
                _segList.Columns[i].Text = I18n.T(segs[i]);

            _miKill.Text = I18n.T("menu.kill");
            _miCopyCmd.Text = I18n.T("menu.copyTaskkill");
            _miCopyPid.Text = I18n.T("menu.copyPid");
            _miOpenLocation.Text = I18n.T("menu.openLocation");
            _miCopyPath.Text = I18n.T("menu.copyPath");

            if (_miTrayOpen != null)
            {
                _miTrayOpen.Text = I18n.T("tray.open");
                _miTrayPause.Text = I18n.T(_paused ? "toolbar.resume" : "toolbar.pause");
                _miTrayJson.Text = I18n.T("tray.exportJson");
                RefreshStartupItem();
                _miTrayCopyJson.Text = I18n.T("tray.copyJsonPath");
                _miTrayDonate.Text = I18n.T("tray.donate");
                _miTrayExit.Text = I18n.T("tray.exit");
            }

            if (_txtFilter.IsHandleCreated)
            {
                try
                {
                    SendMessage(_txtFilter.Handle, EM_SETCUEBANNER, new IntPtr(1),
                                I18n.T("toolbar.filterHint"));
                }
                catch (Exception) { }
            }

            _adapterComboDirty = true;
            LayoutToolbar();
            if (_snap != null) Rebuild();      // RiskText e o combo de adaptadores mudam de idioma
            UpdateTray();
            _header.Invalidate();
            _detail.Invalidate();
            _status.Invalidate();
        }

        private static string LangButtonText()
        {
            string code = I18n.CurrentCode;
            string two = code.Length >= 2 ? code.Substring(0, 2).ToUpperInvariant() : code.ToUpperInvariant();
            return two + " ▾";
        }

        private void ShowLanguageMenu()
        {
            if (_langMenu == null)
            {
                _langMenu = new ContextMenuStrip();
                _langMenu.BackColor = UiTheme.Panel;
                _langMenu.ForeColor = UiTheme.Text;
                _langMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            }

            _langMenu.Items.Clear();

            ToolStripMenuItem auto = new ToolStripMenuItem(I18n.T("toolbar.languageAuto"), null,
                delegate(object s, EventArgs e) { ChangeLanguage(I18n.AutoValue); });
            auto.Checked = string.Equals(_settings.Language, I18n.AutoValue,
                                         StringComparison.OrdinalIgnoreCase);
            _langMenu.Items.Add(auto);
            _langMenu.Items.Add(new ToolStripSeparator());

            List<Language> langs = I18n.Available;
            for (int i = 0; i < langs.Count; i++)
            {
                Language lang = langs[i];
                string label = lang.Label + "   (" + lang.Code + ")";
                if (lang.IsExternal) label += "  •";      // veio de arquivo solto
                ToolStripMenuItem item = new ToolStripMenuItem(label, null,
                    delegate(object s, EventArgs e) { ChangeLanguage(lang.Code); });
                item.Checked = string.Equals(lang.Code, I18n.CurrentCode, StringComparison.OrdinalIgnoreCase);
                _langMenu.Items.Add(item);
            }

            _langMenu.Show(_btnLang, new Point(0, _btnLang.Height));
        }

        private void ChangeLanguage(string code)
        {
            if (!I18n.Select(I18n.Resolve(code)) && !I18n.Select(I18n.DefaultCode)) return;

            _settings.Language = code;   // "auto" = seguir o Windows
            _settings.Save();
            ApplyTexts();
            Flash(I18n.F("status.flashLanguage", I18n.Current.Label));
        }

        // ------------------------------------------------------- iniciar com o Windows
        /// <summary>
        /// Liga/desliga o início automático. Ao ligar, tenta primeiro em
        /// shell:common startup (todos os usuários, via UAC); se a elevação for recusada
        /// ou falhar, oferece a instalação só para o usuário atual em shell:startup.
        /// </summary>
        private void ToggleStartup()
        {
            StartupScope current = Startup.Current;

            if (current != StartupScope.None)
            {
                RemoveStartup(current);
                RefreshStartupItem();
                return;
            }

            // Já elevado: grava direto em "todos os usuários", sem prompt nenhum.
            if (_elevated)
            {
                try
                {
                    Startup.Install(true);
                    Flash(I18n.T("status.flashStartupAll"));
                }
                catch (Exception ex)
                {
                    ShowStartupError(ex.Message);
                }
                RefreshStartupItem();
                return;
            }

            string failure = RunElevatedStartup(true, true);
            if (failure == null)
            {
                Flash(I18n.T("status.flashStartupAll"));
                RefreshStartupItem();
                return;
            }

            // UAC recusado ou gravação falhou: oferece o escopo que não precisa de nada.
            DialogResult dr = MessageBox.Show(this,
                I18n.F("startup.askUserFolder", failure),
                I18n.T("startup.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr == DialogResult.Yes)
            {
                try
                {
                    Startup.Install(false);
                    Flash(I18n.T("status.flashStartupUser"));
                }
                catch (Exception ex)
                {
                    ShowStartupError(ex.Message);
                }
            }
            RefreshStartupItem();
        }

        private void RemoveStartup(StartupScope scope)
        {
            try
            {
                if (scope == StartupScope.AllUsers && !_elevated)
                {
                    string failure = RunElevatedStartup(false, true);
                    if (failure != null)
                    {
                        ShowStartupError(failure);
                        return;
                    }
                }
                else
                {
                    Startup.Uninstall(scope == StartupScope.AllUsers);
                }
                Startup.UninstallUserSilently();   // limpa o do usuário se os dois existirem
                Flash(I18n.T("status.flashStartupOff"));
            }
            catch (Exception ex)
            {
                ShowStartupError(ex.Message);
            }
        }

        /// <summary>Relança o próprio .exe elevado. Retorna null em sucesso, ou o motivo.</summary>
        private string RunElevatedStartup(bool install, bool allUsers)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = (install ? "--install-startup" : "--uninstall-startup") +
                                (allUsers ? " --all-users" : "");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;

                Process p = Process.Start(psi);
                if (p == null) return I18n.T("startup.uacRefused");
                p.WaitForExit(15000);
                int code = p.HasExited ? p.ExitCode : -1;
                p.Dispose();
                if (code == 0) return null;
                return I18n.F("startup.exitCode", code);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return ex.NativeErrorCode == 1223
                    ? I18n.T("startup.uacRefused")
                    : ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private void ShowStartupError(string message)
        {
            MessageBox.Show(this, I18n.F("startup.failed", message), I18n.T("startup.title"),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void RefreshStartupItem()
        {
            if (_miTrayStartup == null) return;
            StartupScope scope = Startup.Current;
            _miTrayStartup.Checked = scope != StartupScope.None;
            string suffix = "";
            if (scope == StartupScope.AllUsers) suffix = "   (" + I18n.T("startup.allUsers") + ")";
            else if (scope == StartupScope.CurrentUser) suffix = "   (" + I18n.T("startup.thisUser") + ")";
            _miTrayStartup.Text = I18n.T("tray.startup") + suffix;
        }

        private void UpdateKillButton()
        {
            if (_selected == null)
            {
                _btnKill.Enabled = false;
                _btnKill.Text = I18n.T("toolbar.kill");
                _btnKill.BackColor = UiTheme.PanelHi;
                _btnKill.ForeColor = UiTheme.Text;
                return;
            }
            bool blocked = _selected.Pi != null && _selected.Pi.Risk == RiskLevel.Critical;
            _btnKill.Enabled = true;
            _btnKill.Text = I18n.F(blocked ? "toolbar.blockedPid" : "toolbar.killPid", _selected.Gp.Pid);
            _btnKill.BackColor = blocked ? UiTheme.PanelHi : UiTheme.Danger;
            _btnKill.ForeColor = blocked ? UiTheme.TextDim : Color.White;
        }

        // ---------------------------------------------------------------- bandeja
        private void BuildTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.BackColor = UiTheme.Panel;
            _trayMenu.ForeColor = UiTheme.Text;
            _trayMenu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());

            _miTrayOpen = new ToolStripMenuItem("", null,
                delegate(object s, EventArgs e) { ShowFromTray(); });
            _miTrayOpen.Font = new Font(_trayMenu.Font, FontStyle.Bold);
            _trayMenu.Items.Add(_miTrayOpen);
            _trayMenu.Items.Add(new ToolStripSeparator());

            _miTrayDed = new ToolStripMenuItem("");
            _miTrayDed.Enabled = false;
            _trayMenu.Items.Add(_miTrayDed);

            _miTrayShr = new ToolStripMenuItem("");
            _miTrayShr.Enabled = false;
            _trayMenu.Items.Add(_miTrayShr);

            _miTrayTop = new ToolStripMenuItem("");
            _miTrayTop.Enabled = false;
            _trayMenu.Items.Add(_miTrayTop);

            _trayMenu.Items.Add(new ToolStripSeparator());

            _miTrayPause = new ToolStripMenuItem("", null,
                delegate(object s, EventArgs e) { TogglePause(); });
            _trayMenu.Items.Add(_miTrayPause);

            _miTrayJson = new ToolStripMenuItem("", null,
                delegate(object s, EventArgs e)
                {
                    _exportJson = !_exportJson;
                    _miTrayJson.Checked = _exportJson;
                    _settings.ExportJson = _exportJson;
                    _settings.Save();
                    Flash(I18n.T(_exportJson ? "status.flashJsonOn" : "status.flashJsonOff"));
                });
            _miTrayJson.Checked = _exportJson;
            _trayMenu.Items.Add(_miTrayJson);

            _miTrayStartup = new ToolStripMenuItem("", null,
                delegate(object s, EventArgs e) { ToggleStartup(); });
            _trayMenu.Items.Add(_miTrayStartup);

            _miTrayCopyJson = new ToolStripMenuItem("", null,
                delegate(object s, EventArgs e)
                {
                    TrySetClipboard(_jsonPath);
                    Flash(I18n.T("status.flashPathCopied"));
                });
            _trayMenu.Items.Add(_miTrayCopyJson);

            _trayMenu.Items.Add(new ToolStripSeparator());

            _miTrayDonate = new ToolStripMenuItem("", null,
                delegate(object s, EventArgs e) { OpenDonate(); });
            _miTrayDonate.ForeColor = UiTheme.Donate;
            _trayMenu.Items.Add(_miTrayDonate);

            _miTrayExit = new ToolStripMenuItem("", null, delegate(object s, EventArgs e)
            {
                _reallyExit = true;
                Close();
            });
            _trayMenu.Items.Add(_miTrayExit);

            _tray = new NotifyIcon();
            _tray.Text = "Monitor de VRAM";
            _tray.ContextMenuStrip = _trayMenu;
            _tray.Visible = true;
            _tray.MouseDoubleClick += delegate(object s, MouseEventArgs e) { ShowFromTray(); };

            UpdateTrayIcon(0);
        }

        private void HideToTray(bool fromCloseButton)
        {
            Hide();
            ShowInTaskbar = false;
            if (fromCloseButton && !_balloonShown)
            {
                _balloonShown = true;
                try
                {
                    _tray.BalloonTipTitle = AppInfo.Name;
                    _tray.BalloonTipText = I18n.T("tray.balloonText");
                    _tray.ShowBalloonTip(4000);
                }
                catch (Exception) { }
            }
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void UpdateTrayIcon(int pct)
        {
            if (pct == _lastTrayPct) return;
            _lastTrayPct = pct;
            IntPtr old = _trayHIcon;
            Icon oldIcon = _tray.Icon;
            IntPtr h;
            Icon ic = TrayGauge.Create(pct, out h);
            _trayHIcon = h;
            _tray.Icon = ic;

            // O ícone da janela é definido uma única vez, com um clone independente: o handle
            // do ícone da bandeja é destruído a cada mudança de percentual.
            if (!_formIconSet && pct > 0)
            {
                _formIconSet = true;
                try { Icon = (Icon)ic.Clone(); }
                catch (Exception) { }
            }

            if (oldIcon != null) oldIcon.Dispose();
            TrayGauge.Destroy(old);
        }

        private void UpdateTray()
        {
            if (_tray == null || _snap == null) return;

            long ded = 0, dedTotal = 0, shr = 0, shrTotal = 0;
            for (int i = 0; i < _snap.Adapters.Count; i++)
            {
                GpuAdapter a = _snap.Adapters[i];
                ded += a.DedicatedUsed;
                dedTotal += a.DedicatedTotal;
                shr += a.SharedUsed;
                shrTotal += a.SharedTotal;
            }
            int pct = dedTotal > 0 ? (int)Math.Round(100.0 * ded / dedTotal) : 0;
            UpdateTrayIcon(pct);

            string top = "—";
            long topVal = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Local > topVal)
                {
                    topVal = _rows[i].Local;
                    top = ProcName(_rows[i]) + " " + Fmt.Bytes(_rows[i].Local);
                }
            }

            string tip = "VRAM " + Fmt.Gb(ded) + "/" + Fmt.Gb(dedTotal) + " GB (" + pct + "%)";
            if (topVal > 0) tip += "\n" + top;
            if (_paused) tip += "\n" + I18n.T("tray.paused");
            if (tip.Length > 60) tip = tip.Substring(0, 60);
            try { _tray.Text = tip; }
            catch (Exception) { }

            _miTrayDed.Text = I18n.F("tray.dedicated", Fmt.Bytes(ded), Fmt.Gb(dedTotal), pct);
            _miTrayShr.Text = I18n.F("tray.shared", Fmt.Bytes(shr), Fmt.Gb(shrTotal));
            _miTrayTop.Text = I18n.F("tray.top", top);
            _miTrayPause.Text = I18n.T(_paused ? "toolbar.resume" : "toolbar.pause");
        }

        private void ExportJson()
        {
            if (!_exportJson || _snap == null) return;
            try
            {
                string js = SnapshotJson.Build(_snap, _catalog, "gui", true, 0, 0);
                SnapshotJson.WriteFile(_jsonPath, js);
            }
            catch (Exception)
            {
                // a ponte nunca deve derrubar a UI
            }
        }

        private void LayoutDetail()
        {
            int infoW = Math.Max(Dpi.S(330), (int)(_detail.ClientSize.Width * 0.42));
            _segList.Location = new Point(infoW, Dpi.S(32));
            _segList.Size = new Size(Math.Max(Dpi.S(200), _detail.ClientSize.Width - infoW - Dpi.S(10)),
                                     Math.Max(Dpi.S(60), _detail.ClientSize.Height - Dpi.S(42)));
            _detail.Invalidate();
        }

        // ------------------------------------------------------------------ ciclo
        private void Refresh_()
        {
            if (_paused) return;
            _snap = _sampler.Sample();

            List<int> pids = new List<int>(_snap.Processes.Count);
            for (int i = 0; i < _snap.Processes.Count; i++)
                pids.Add(_snap.Processes[i].Pid);
            _catalog.Sync(pids);

            SyncAdapterCombo();
            _header.SetData(_snap.Adapters);
            Rebuild();
            UpdateTray();
            ExportJson();
            _status.Invalidate();
        }

        private void SyncAdapterCombo()
        {
            List<string> want = new List<string>();
            want.Add(I18n.T("toolbar.allAdapters"));
            for (int i = 0; i < _snap.Adapters.Count; i++)
            {
                GpuAdapter a = _snap.Adapters[i];
                if (a.DedicatedTotal > 0 || a.DedicatedUsed > 0 || a.SharedUsed > 0)
                    want.Add(a.Label);
            }

            bool same = !_adapterComboDirty && want.Count == _cbAdapter.Items.Count;
            if (same)
            {
                for (int i = 0; i < want.Count; i++)
                {
                    if (!string.Equals(want[i], Convert.ToString(_cbAdapter.Items[i]), StringComparison.Ordinal))
                    {
                        same = false;
                        break;
                    }
                }
            }
            if (same) return;
            _adapterComboDirty = false;

            string cur = Convert.ToString(_cbAdapter.SelectedItem);
            _cbAdapter.BeginUpdate();
            _cbAdapter.Items.Clear();
            for (int i = 0; i < want.Count; i++) _cbAdapter.Items.Add(want[i]);
            int idx = cur == null ? 0 : want.IndexOf(cur);
            _cbAdapter.SelectedIndex = idx < 0 ? 0 : idx;
            _cbAdapter.EndUpdate();
        }

        private string SelectedAdapterLuid()
        {
            if (_snap == null || _cbAdapter.SelectedIndex <= 0) return null;
            string label = Convert.ToString(_cbAdapter.SelectedItem);
            for (int i = 0; i < _snap.Adapters.Count; i++)
                if (string.Equals(_snap.Adapters[i].Label, label, StringComparison.Ordinal))
                    return _snap.Adapters[i].LuidKey;
            return null;
        }

        private void Rebuild()
        {
            if (_snap == null) return;

            string luid = SelectedAdapterLuid();
            string filter = _txtFilter.Text.Trim();
            bool onlyGpu = _chkOnlyGpu.Checked;

            List<Row> rows = new List<Row>();
            for (int i = 0; i < _snap.Processes.Count; i++)
            {
                GpuProcess gp = _snap.Processes[i];
                Row r = new Row();
                r.Gp = gp;
                r.Pi = _catalog.Get(gp.Pid);

                if (luid == null)
                {
                    r.Local = gp.Local;
                    r.NonLocal = gp.NonLocal;
                    r.Dedicated = gp.Dedicated;
                    r.Shared = gp.Shared;
                    r.Committed = gp.Committed;
                }
                else
                {
                    bool hit = false;
                    for (int k = 0; k < gp.Segments.Count; k++)
                    {
                        GpuSegment s = gp.Segments[k];
                        if (!string.Equals(s.LuidKey, luid, StringComparison.OrdinalIgnoreCase)) continue;
                        hit = true;
                        r.Local += s.Local;
                        r.NonLocal += s.NonLocal;
                        r.Dedicated += s.Dedicated;
                        r.Shared += s.Shared;
                        r.Committed += s.Committed;
                    }
                    if (!hit && gp.EnginePercent <= 0.05) continue;
                }
                r.Total = r.Local + r.NonLocal;

                if (onlyGpu && r.Total == 0 && r.Committed == 0 && gp.EnginePercent <= 0.05)
                    continue;
                if (filter.Length > 0 && !Matches(r, filter))
                    continue;

                rows.Add(r);
            }

            rows.Sort(CompareRows);

            // Congela a ordem exibida enquanto o usuário está lendo a lista (ponteiro sobre ela
            // ou scroll fora do topo). Os valores continuam atualizando; só o ranking espera.
            _orderFrozen = _mouseOverList ||
                           (_list.Items.Count > 0 && _list.TopItem != null && _list.TopItem.Index > 0);

            if (_orderFrozen && _orderPids.Count > 0)
                rows = ApplyFrozenOrder(rows);
            else
                _orderPids = PidsOf(rows);

            _rows = rows;
            FillList();
        }

        private static List<int> PidsOf(List<Row> rows)
        {
            List<int> pids = new List<int>(rows.Count);
            for (int i = 0; i < rows.Count; i++) pids.Add(rows[i].Gp.Pid);
            return pids;
        }

        /// <summary>Reordena as linhas para seguir a ordem já exibida; novidades vão para o fim.</summary>
        private List<Row> ApplyFrozenOrder(List<Row> rows)
        {
            Dictionary<int, int> pos = new Dictionary<int, int>(_orderPids.Count);
            for (int i = 0; i < _orderPids.Count; i++)
                if (!pos.ContainsKey(_orderPids[i])) pos[_orderPids[i]] = i;

            List<Row> known = new List<Row>(rows.Count);
            List<Row> fresh = new List<Row>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (pos.ContainsKey(rows[i].Gp.Pid)) known.Add(rows[i]);
                else fresh.Add(rows[i]);
            }
            known.Sort(delegate(Row a, Row b) { return pos[a.Gp.Pid].CompareTo(pos[b.Gp.Pid]); });
            known.AddRange(fresh);
            _orderPids = PidsOf(known);
            return known;
        }

        private static bool Matches(Row r, string filter)
        {
            if (r.Gp.Pid.ToString(CultureInfo.InvariantCulture).IndexOf(filter, StringComparison.Ordinal) >= 0)
                return true;
            if (r.Pi == null) return false;
            if (r.Pi.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (r.Pi.ExePath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (r.Pi.User.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (r.Pi.FileDescription.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (r.Pi.ServicesText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private int CompareRows(Row a, Row b)
        {
            int c;
            switch (_sortColumn)
            {
                case 0: c = a.Gp.Pid.CompareTo(b.Gp.Pid); break;
                case 1: c = string.Compare(ProcName(a), ProcName(b), StringComparison.OrdinalIgnoreCase); break;
                case 3: c = a.NonLocal.CompareTo(b.NonLocal); break;
                case 4: c = a.Total.CompareTo(b.Total); break;
                case 5: c = a.Committed.CompareTo(b.Committed); break;
                case 6: c = a.Gp.EnginePercent.CompareTo(b.Gp.EnginePercent); break;
                case 7: c = string.Compare(a.Gp.TopEngine, b.Gp.TopEngine, StringComparison.OrdinalIgnoreCase); break;
                case 8: c = ((int)Risk(a)).CompareTo((int)Risk(b)); break;
                case 9: c = string.Compare(User(a), User(b), StringComparison.OrdinalIgnoreCase); break;
                case 10: c = string.Compare(Detail(a), Detail(b), StringComparison.OrdinalIgnoreCase); break;
                default: c = a.Local.CompareTo(b.Local); break;
            }
            if (c == 0) c = a.Local.CompareTo(b.Local);
            if (c == 0) c = a.Gp.Pid.CompareTo(b.Gp.Pid);
            return _sortAsc ? c : -c;
        }

        private static string ProcName(Row r) { return r.Pi != null ? r.Pi.Name : "(pid " + r.Gp.Pid + ")"; }
        private static string User(Row r) { return r.Pi != null ? r.Pi.User : ""; }
        private static string Detail(Row r) { return r.Pi != null ? r.Pi.Detail : ""; }
        private static RiskLevel Risk(Row r) { return r.Pi != null ? r.Pi.Risk : RiskLevel.Normal; }

        private static string[] Cells(Row r)
        {
            return new string[]
            {
                r.Gp.Pid.ToString(CultureInfo.InvariantCulture),
                ProcName(r),
                Fmt.Bytes(r.Local),
                Fmt.Bytes(r.NonLocal),
                Fmt.Bytes(r.Total),
                Fmt.Bytes(r.Committed),
                Fmt.Percent(r.Gp.EnginePercent),
                r.Gp.TopEngine,
                r.Pi != null ? r.Pi.RiskText : "?",
                ShortUser(User(r)),
                Detail(r)
            };
        }

        private bool SameSequence()
        {
            if (_list.Items.Count != _rows.Count) return false;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row cur = _list.Items[i].Tag as Row;
                if (cur == null || cur.Gp.Pid != _rows[i].Gp.Pid) return false;
            }
            return true;
        }

        private void FillList()
        {
            int selPid = _selected != null ? _selected.Gp.Pid : -1;

            // Caminho rápido: a sequência de PIDs não mudou, então só os textos são atualizados.
            // Nada de Items.Clear() -> o scroll e a seleção ficam exatamente onde estavam.
            if (SameSequence())
            {
                // BeginUpdate/EndUpdate é obrigatório: alterar SubItems[k].Text invalida só o
                // retângulo daquela célula, e com OwnerDraw isso deixa a linha meio desenhada.
                _list.BeginUpdate();
                try
                {
                    for (int i = 0; i < _rows.Count; i++)
                    {
                        ListViewItem it = _list.Items[i];
                        string[] cells = Cells(_rows[i]);
                        for (int k = 0; k < cells.Length && k < it.SubItems.Count; k++)
                        {
                            if (!string.Equals(it.SubItems[k].Text, cells[k], StringComparison.Ordinal))
                                it.SubItems[k].Text = cells[k];
                        }
                        it.Tag = _rows[i];
                        if (_rows[i].Gp.Pid == selPid) _selected = _rows[i];
                    }
                }
                finally
                {
                    _list.EndUpdate();
                }
                if (_selected != null)
                {
                    FillSegments();
                    _detail.Invalidate();
                }
                return;
            }

            int topIdx = _list.TopItem != null ? _list.TopItem.Index : 0;
            bool found = false;

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                for (int i = 0; i < _rows.Count; i++)
                {
                    string[] cells = Cells(_rows[i]);
                    ListViewItem it = new ListViewItem(cells[0]);
                    for (int k = 1; k < cells.Length; k++) it.SubItems.Add(cells[k]);
                    it.Tag = _rows[i];
                    _list.Items.Add(it);
                }

                if (selPid >= 0)
                {
                    for (int i = 0; i < _list.Items.Count; i++)
                    {
                        Row r = (Row)_list.Items[i].Tag;
                        if (r.Gp.Pid == selPid)
                        {
                            _list.Items[i].Selected = true;
                            _selected = r;
                            found = true;
                            break;
                        }
                    }
                }

                // Restaura o scroll com clamp: se a lista encurtou, ancora na última linha
                // possível em vez de saltar para o topo.
                if (topIdx > 0 && _list.Items.Count > 0)
                {
                    int t = Math.Min(topIdx, _list.Items.Count - 1);
                    if (t > 0)
                    {
                        try { _list.TopItem = _list.Items[t]; }
                        catch (Exception) { }
                    }
                }

                // Na primeira carga, seleciona o maior consumidor para o painel não nascer vazio.
                if (!_autoSelected && selPid < 0 && _list.Items.Count > 0)
                {
                    _autoSelected = true;
                    _list.Items[0].Selected = true;
                    _selected = (Row)_list.Items[0].Tag;
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            if (selPid >= 0 && !found)
            {
                _selected = null;
                UpdateKillButton();
                _segList.Items.Clear();
                _detail.Invalidate();
            }
            else if (_selected != null)
            {
                FillSegments();
                _detail.Invalidate();
            }
        }

        private static string ShortUser(string user)
        {
            if (string.IsNullOrEmpty(user)) return "";
            int at = user.IndexOf('\\');
            return at < 0 ? user : user.Substring(at + 1);
        }

        private void OnSelectionChanged()
        {
            if (_list.SelectedIndices.Count == 0)
            {
                _selected = null;
                UpdateKillButton();
                _segList.Items.Clear();
                _detail.Invalidate();
                return;
            }
            _selected = (Row)_list.Items[_list.SelectedIndices[0]].Tag;
            UpdateKillButton();
            FillSegments();
            _detail.Invalidate();
        }

        private void FillSegments()
        {
            if (_selected == null) return;
            _segList.BeginUpdate();
            try
            {
                _segList.Items.Clear();
                List<GpuSegment> segs = _selected.Gp.Segments;
                for (int i = 0; i < segs.Count; i++)
                {
                    GpuSegment s = segs[i];
                    ListViewItem it = new ListViewItem(AdapterLabel(s.LuidKey));
                    it.SubItems.Add(s.PhysIndex.ToString(CultureInfo.InvariantCulture));
                    it.SubItems.Add(Fmt.Bytes(s.Local));
                    it.SubItems.Add(Fmt.Bytes(s.NonLocal));
                    it.SubItems.Add(Fmt.Bytes(s.Dedicated));
                    it.SubItems.Add(Fmt.Bytes(s.Committed));
                    it.Tag = s;
                    _segList.Items.Add(it);
                }
            }
            finally
            {
                _segList.EndUpdate();
            }
        }

        private string AdapterLabel(string luidKey)
        {
            if (_snap != null)
            {
                for (int k = 0; k < _snap.Adapters.Count; k++)
                    if (string.Equals(_snap.Adapters[k].LuidKey, luidKey, StringComparison.OrdinalIgnoreCase))
                        return _snap.Adapters[k].Label;
            }
            return luidKey;
        }

        // ---------------------------------------------------------------- desenho
        private void HeaderDraw(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            ListView lv = (ListView)sender;
            Graphics g = e.Graphics;
            using (SolidBrush br = new SolidBrush(UiTheme.HeaderBg))
                g.FillRectangle(br, e.Bounds);
            using (Pen p = new Pen(UiTheme.Grid))
            {
                g.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Y + Dpi.S(5),
                              e.Bounds.Right - 1, e.Bounds.Bottom - Dpi.S(6));
                g.DrawLine(p, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            bool sorted = lv == _list && e.ColumnIndex == _sortColumn;
            int glyph = sorted ? Dpi.S(14) : 0;
            Rectangle tr = new Rectangle(e.Bounds.X + Dpi.S(6), e.Bounds.Y,
                                         e.Bounds.Width - Dpi.S(12) - glyph, e.Bounds.Height);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            HorizontalAlignment al = lv.Columns[e.ColumnIndex].TextAlign;
            if (al == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            else if (al == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;

            TextRenderer.DrawText(g, e.Header.Text, _fSmall, tr,
                                  sorted ? UiTheme.Accent : UiTheme.TextDim, flags);

            if (sorted)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int cx = e.Bounds.Right - Dpi.S(12);
                int cy = e.Bounds.Y + e.Bounds.Height / 2;
                int w = Dpi.S(4), h = Dpi.S(3);
                Point[] tri = _sortAsc
                    ? new Point[] { new Point(cx - w, cy + h), new Point(cx + w, cy + h), new Point(cx, cy - h) }
                    : new Point[] { new Point(cx - w, cy - h), new Point(cx + w, cy - h), new Point(cx, cy + h) };
                using (SolidBrush br = new SolidBrush(UiTheme.Accent))
                    g.FillPolygon(br, tri);
            }
        }

        private void RowBgDraw(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = false;
            Graphics g = e.Graphics;
            bool sel = (e.State & ListViewItemStates.Selected) != 0;
            Color bg = sel ? UiTheme.Selection : (e.ItemIndex % 2 == 0 ? UiTheme.Bg : UiTheme.RowAlt);
            using (SolidBrush br = new SolidBrush(bg))
                g.FillRectangle(br, e.Bounds);
            if (sel)
            {
                using (SolidBrush br = new SolidBrush(UiTheme.SelectionEdge))
                    g.FillRectangle(br, e.Bounds.X, e.Bounds.Y, Dpi.S(3), e.Bounds.Height);
            }
        }

        private void MainSubDraw(object sender, DrawListViewSubItemEventArgs e)
        {
            Row r = e.Item.Tag as Row;
            if (r == null) return;
            Rectangle b = SubBounds(_list, e);
            Graphics g = e.Graphics;

            // barra proporcional atrás dos números
            if (e.ColumnIndex >= 2 && e.ColumnIndex <= 4)
            {
                long val = e.ColumnIndex == 2 ? r.Local : (e.ColumnIndex == 3 ? r.NonLocal : r.Total);
                long refTotal = ReferenceTotal(e.ColumnIndex);
                if (val > 0 && refTotal > 0)
                {
                    double frac = (double)val / refTotal;
                    if (frac > 1) frac = 1;
                    int w = (int)Math.Round((b.Width - Dpi.S(8)) * frac);
                    if (w > 1)
                    {
                        Color c = e.ColumnIndex == 3 ? Color.FromArgb(64, UiTheme.SharedClr)
                                                     : Color.FromArgb(64, UiTheme.Accent);
                        UiTheme.FillRounded(g, new Rectangle(b.X + Dpi.S(4), b.Y + Dpi.S(3),
                                                             w, b.Height - Dpi.S(6)), c, Dpi.S(2));
                    }
                }
            }

            Color fg = UiTheme.Text;
            Font f = _fSmall;
            string text = e.SubItem.Text;

            switch (e.ColumnIndex)
            {
                case 0: fg = UiTheme.TextDim; f = _fMono; break;
                case 1: f = _fBold; break;
                case 2: fg = r.Local > 0 ? UiTheme.Text : UiTheme.TextDim; break;
                case 3: fg = r.NonLocal > 0 ? UiTheme.SharedClr : UiTheme.TextDim; break;
                case 5: fg = UiTheme.TextDim; break;
                case 6: fg = r.Gp.EnginePercent >= 50 ? UiTheme.Warn : UiTheme.TextDim; break;
                case 7: fg = UiTheme.TextDim; break;
                case 8:
                    {
                        RiskLevel rl = Risk(r);
                        fg = UiTheme.RiskColor(rl);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        Rectangle dot = new Rectangle(b.X + Dpi.S(6), b.Y + b.Height / 2 - Dpi.S(3),
                                                      Dpi.S(6), Dpi.S(6));
                        using (SolidBrush br = new SolidBrush(fg)) g.FillEllipse(br, dot);
                        b = new Rectangle(b.X + Dpi.S(16), b.Y, b.Width - Dpi.S(16), b.Height);
                        if (r.Pi != null && r.Pi.Elevated.HasValue && r.Pi.Elevated.Value &&
                            rl == RiskLevel.System)
                            text = text + " ↑";
                        break;
                    }
                case 9:
                case 10: fg = UiTheme.TextDim; break;
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                                    TextFormatFlags.NoPrefix;
            HorizontalAlignment al = _list.Columns[e.ColumnIndex].TextAlign;
            if (al == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            else if (al == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;

            TextRenderer.DrawText(g, text, f,
                                  new Rectangle(b.X + Dpi.S(5), b.Y, b.Width - Dpi.S(10), b.Height),
                                  fg, flags);
        }

        private long ReferenceTotal(int column)
        {
            if (_snap == null) return 0;
            string luid = SelectedAdapterLuid();
            long ded = 0, shr = 0;
            for (int i = 0; i < _snap.Adapters.Count; i++)
            {
                GpuAdapter a = _snap.Adapters[i];
                if (luid != null && !string.Equals(a.LuidKey, luid, StringComparison.OrdinalIgnoreCase))
                    continue;
                ded += a.DedicatedTotal;
                shr += a.SharedTotal;
            }
            if (column == 2) return ded;
            if (column == 3) return shr;
            return ded + shr;
        }

        private void SegSubDraw(object sender, DrawListViewSubItemEventArgs e)
        {
            Rectangle b = SubBounds(_segList, e);
            Color fg = UiTheme.Text;
            if (e.ColumnIndex == 0) fg = UiTheme.TextDim;
            else if (e.ColumnIndex == 2) fg = UiTheme.Accent;
            else if (e.ColumnIndex == 3) fg = UiTheme.SharedClr;
            else if (e.ColumnIndex >= 4) fg = UiTheme.TextDim;

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            HorizontalAlignment al = _segList.Columns[e.ColumnIndex].TextAlign;
            if (al == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            else if (al == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _fSmall,
                                  new Rectangle(b.X + Dpi.S(5), b.Y, b.Width - Dpi.S(10), b.Height),
                                  fg, flags);
        }

        private static Rectangle SubBounds(ListView lv, DrawListViewSubItemEventArgs e)
        {
            // Em Details, o Bounds da coluna 0 vem com a largura da linha inteira.
            if (e.ColumnIndex == 0)
                return new Rectangle(e.Bounds.X, e.Bounds.Y, lv.Columns[0].Width, e.Bounds.Height);
            return e.Bounds;
        }

        private void DetailPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush br = new SolidBrush(UiTheme.Bg))
                g.FillRectangle(br, _detail.ClientRectangle);
            using (Pen p = new Pen(UiTheme.Grid))
                g.DrawLine(p, 0, 0, _detail.ClientSize.Width, 0);

            int infoW = Math.Max(Dpi.S(330), (int)(_detail.ClientSize.Width * 0.42));
            int x = Dpi.S(14);

            using (SolidBrush br = new SolidBrush(UiTheme.TextDim))
                g.DrawString(I18n.T("seg.title"), _fSmall, br,
                             infoW, Dpi.S(9));

            if (_selected == null)
            {
                using (SolidBrush br = new SolidBrush(UiTheme.TextDim))
                    g.DrawString(I18n.T("detail.selectHint"), _fSmall, br, x, Dpi.S(12));
                return;
            }

            Row r = _selected;
            ProcInfo pi = r.Pi;
            int y = Dpi.S(10);

            string nm = ProcName(r) + "   ";
            using (SolidBrush br = new SolidBrush(UiTheme.Text))
                g.DrawString(nm, _fBold, br, x, y);
            SizeF nz = g.MeasureString(nm, _fBold);
            using (SolidBrush br = new SolidBrush(UiTheme.TextDim))
                g.DrawString(I18n.F("kill.pid", r.Gp.Pid), _fSmall, br, x + nz.Width, y + Dpi.S(2));

            if (pi != null)
            {
                Color rc = UiTheme.RiskColor(pi.Risk);
                string tag = pi.RiskText.ToUpperInvariant();
                SizeF tz = g.MeasureString(tag, _fSmall);
                Rectangle chip = new Rectangle(infoW - (int)tz.Width - Dpi.S(40), y,
                                               (int)tz.Width + Dpi.S(16), Dpi.S(20));
                UiTheme.FillRounded(g, chip, Color.FromArgb(46, rc), Dpi.S(10));
                using (SolidBrush br = new SolidBrush(rc))
                    g.DrawString(tag, _fSmall, br, chip.X + Dpi.S(8), chip.Y + Dpi.S(2));
            }

            y += Dpi.S(26);
            using (SolidBrush lb = new SolidBrush(UiTheme.TextDim))
            using (SolidBrush vb = new SolidBrush(UiTheme.Text))
            {
                y = Line(g, lb, vb, x, y, infoW, I18n.T("detail.path"),
                         pi != null && pi.ExePath.Length > 0 ? pi.ExePath : I18n.T("detail.noAccess"));
                y = Line(g, lb, vb, x, y, infoW, I18n.T("detail.user"),
                         pi != null && pi.User.Length > 0 ? pi.User : I18n.T("detail.unknown"));
                y = Line(g, lb, vb, x, y, infoW, I18n.T("detail.sessionElevation"),
                         (pi != null ? pi.SessionId.ToString(CultureInfo.InvariantCulture) : "?") +
                         "  ·  " + (pi == null ? "?" : (pi.Elevated.HasValue
                            ? I18n.T(pi.Elevated.Value ? "detail.elevatedYes" : "detail.elevatedNo")
                            : I18n.T("detail.elevationUnknown"))));
                if (pi != null && pi.Services.Count > 0)
                    y = Line(g, lb, vb, x, y, infoW, I18n.T("detail.services"), pi.ServicesText);

                y = Line(g, lb, vb, x, y, infoW, I18n.T("detail.gpuMemory"),
                         I18n.F("detail.gpuMemoryValue", Fmt.Bytes(r.Local), Fmt.Bytes(r.NonLocal),
                                Fmt.Bytes(r.Total)));
                y = Line(g, lb, vb, x, y, infoW, I18n.T("detail.committed"),
                         I18n.F("detail.committedValue", Fmt.Bytes(r.Committed), Fmt.Bytes(r.Dedicated)));

                string eng = EnginesText(r.Gp);
                y = Line(g, lb, vb, x, y, infoW, I18n.T("detail.engines"),
                         eng.Length > 0 ? eng : I18n.T("detail.noEngineActivity"));
            }

            if (pi != null && pi.RiskNote.Length > 0)
            {
                using (SolidBrush br = new SolidBrush(UiTheme.RiskColor(pi.Risk)))
                    g.DrawString(pi.RiskNote, _fSmall, br,
                                 new RectangleF(x, y + Dpi.S(4), infoW - x - Dpi.S(14),
                                                _detail.ClientSize.Height - y - Dpi.S(8)));
            }
        }

        private int Line(Graphics g, Brush lb, Brush vb, int x, int y, int maxRight,
                         string label, string value)
        {
            g.DrawString(label, _fSmall, lb, x, y);
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.Trimming = StringTrimming.EllipsisPath;
                g.DrawString(value, _fSmall, vb,
                             new RectangleF(x + Dpi.S(122), y, maxRight - x - Dpi.S(136), Dpi.S(18)), sf);
            }
            return y + Dpi.S(19);
        }

        private static string EnginesText(GpuProcess gp)
        {
            List<KeyValuePair<string, double>> list = new List<KeyValuePair<string, double>>();
            foreach (KeyValuePair<string, double> kv in gp.Engines)
                if (kv.Value > 0.05) list.Add(kv);
            list.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b)
            {
                return b.Value.CompareTo(a.Value);
            });
            List<string> parts = new List<string>();
            for (int i = 0; i < list.Count && i < 5; i++)
                parts.Add(list[i].Key + " " + list[i].Value.ToString("N1", CultureInfo.CurrentCulture) + "%");
            return string.Join("  ·  ", parts.ToArray());
        }

        private void StatusPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush br = new SolidBrush(UiTheme.HeaderBg))
                g.FillRectangle(br, _status.ClientRectangle);

            long ded = 0, shr = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                ded += _rows[i].Local;
                shr += _rows[i].NonLocal;
            }

            string left = I18n.F("status.processes", _rows.Count) +
                          "   ·   " + I18n.F("status.sumDedicated", Fmt.Bytes(ded)) +
                          "   ·   " + I18n.F("status.sumShared", Fmt.Bytes(shr)) +
                          "   ·   " + I18n.F("status.sumTotal", Fmt.Bytes(ded + shr));
            if (_snap != null)
                left += "   ·   " + _snap.Taken.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            left += "   ·   " + I18n.T(_elevated ? "toolbar.elevated" : "toolbar.notElevated");
            if (_orderFrozen) left += "   ·   " + I18n.T("status.frozen");
            if (_paused) left += "   ·   " + I18n.T("status.paused");

            using (SolidBrush br = new SolidBrush(UiTheme.TextDim))
                g.DrawString(left, _fSmall, br, Dpi.S(10), Dpi.S(5));

            string right;
            Color rc = UiTheme.TextDim;
            if (DateTime.UtcNow < _flashUntil)
            {
                right = _flash;
                rc = UiTheme.Ok;
            }
            else
            {
                right = I18n.T("status.hints");
            }
            SizeF sz = g.MeasureString(right, _fSmall);
            using (SolidBrush br = new SolidBrush(rc))
                g.DrawString(right, _fSmall, br, _status.ClientSize.Width - sz.Width - Dpi.S(10), Dpi.S(5));
        }

        // ----------------------------------------------------------------- ações
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5) { ForceRefresh(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { _txtFilter.Focus(); _txtFilter.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.C) && _list.Focused) { CopyTaskkill(); return true; }
            if (keyData == Keys.Delete && _list.Focused) { KillSelected(); return true; }
            if (keyData == Keys.Space && _list.Focused) { TogglePause(); return true; }
            if (keyData == Keys.F1) { ShowHelp(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ForceRefresh()
        {
            bool was = _paused;
            _paused = false;
            Refresh_();
            _paused = was;
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _btnPause.Text = I18n.T(_paused ? "toolbar.resume" : "toolbar.pause");
            _btnPause.ForeColor = _paused ? UiTheme.Warn : UiTheme.Text;
            if (_miTrayPause != null) _miTrayPause.Text = I18n.T(_paused ? "toolbar.resume" : "toolbar.pause");
            _status.Invalidate();
        }

        private void CopyTaskkill()
        {
            if (_selected == null) return;
            TrySetClipboard("taskkill /F /PID " + _selected.Gp.Pid.ToString(CultureInfo.InvariantCulture));
            Flash(I18n.T("status.flashCopied"));
        }

        private void TrySetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); }
            catch (Exception) { }
        }

        private void Flash(string msg)
        {
            _flash = msg;
            _flashUntil = DateTime.UtcNow.AddSeconds(4);
            _status.Invalidate();
        }

        private void RelaunchElevated()
        {
            // Solta a trava de instância única ANTES de iniciar a cópia elevada, senão ela
            // se veria como segunda instância e sairia na hora.
            SingleInstance.Release();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
            }
            catch (Exception)
            {
                SingleInstance.TryAcquire(); // UAC recusado: seguimos sendo a única instância
                Flash(I18n.T("status.flashElevationCancelled"));
                return;
            }

            // _reallyExit é obrigatório: sem ele o FormClosing minimizaria para a bandeja e
            // sobrariam duas instâncias rodando ao mesmo tempo.
            _reallyExit = true;
            Close();
        }

        protected override void WndProc(ref Message m)
        {
            uint show = SingleInstance.ShowMsg;
            if (show != 0 && m.Msg == (int)show)
            {
                ShowFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        private void OpenDonate()
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.DonateUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                TrySetClipboard(AppInfo.DonateUrl);
                MessageBox.Show(this,
                    I18n.F("dialog.donateFail", AppInfo.DonateUrl),
                    I18n.T("dialog.donateFailTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                I18n.Joined("help.body", _jsonPath, I18n.UserLangDir()),
                AppInfo.NameWithVersion + " — " + I18n.T("help.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void KillSelected()
        {
            if (_selected == null) return;
            int pid = _selected.Gp.Pid;
            ProcInfo pi = _selected.Pi;
            if (pi == null)
            {
                pi = new ProcInfo();
                pi.Pid = pid;
                pi.Name = "(pid " + pid + ")";
                pi.Risk = RiskLevel.Elevated;
                pi.RiskNoteKey = "kill.noMetadata";
            }

            using (KillConfirmForm dlg = new KillConfirmForm(pi, _selected.Gp, _elevated))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
            }

            KillResult res = ProcessCatalog.Kill(pid);
            if (res.Outcome == KillOutcome.AccessDenied)
            {
                DialogResult dr = MessageBox.Show(this,
                    I18n.F("dialog.accessDeniedRetry", res.Message),
                    I18n.T("dialog.accessDeniedTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr == DialogResult.Yes)
                    res = ProcessCatalog.KillElevated(pid);
            }

            if (res.Outcome == KillOutcome.Success)
            {
                _catalog.Forget(pid);
                _selected = null;
                Flash(I18n.F("status.flashKilled", pid));
                ForceRefresh();
            }
            else if (res.Outcome == KillOutcome.NotFound)
            {
                _catalog.Forget(pid);
                Flash(I18n.F("status.flashGone", pid));
                ForceRefresh();
            }
            else
            {
                MessageBox.Show(this, res.Message, I18n.T("dialog.killFailTitle"),
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    Icon oldIcon = _tray.Icon;
                    _tray.Icon = null;
                    if (oldIcon != null) oldIcon.Dispose();
                    TrayGauge.Destroy(_trayHIcon);
                    _trayHIcon = IntPtr.Zero;
                    _tray.Dispose();
                }
                _sampler.Dispose();
                if (_fSmall != null) _fSmall.Dispose();
                if (_fBold != null) _fBold.Dispose();
                if (_fMono != null) _fMono.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Paleta escura para o menu de contexto.</summary>
    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return UiTheme.Panel; } }
        public override Color ImageMarginGradientBegin { get { return UiTheme.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return UiTheme.Panel; } }
        public override Color ImageMarginGradientEnd { get { return UiTheme.Panel; } }
        public override Color MenuItemSelected { get { return UiTheme.Selection; } }
        public override Color MenuItemSelectedGradientBegin { get { return UiTheme.Selection; } }
        public override Color MenuItemSelectedGradientEnd { get { return UiTheme.Selection; } }
        public override Color MenuItemBorder { get { return UiTheme.SelectionEdge; } }
        public override Color MenuBorder { get { return UiTheme.Grid; } }
        public override Color SeparatorDark { get { return UiTheme.Grid; } }
        public override Color SeparatorLight { get { return UiTheme.Grid; } }
    }
}
