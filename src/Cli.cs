using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VramMonitor
{
    /// <summary>
    /// Ponte headless: os mesmos dados da janela, sem janela.
    ///   --json                 um snapshot JSON no stdout
    ///   --watch                JSON por linha, continuamente
    ///   --text                 tabela legível
    ///   --headless             sem UI, só mantendo o arquivo da ponte atualizado
    ///   --kill PID             encerra com as mesmas travas de segurança da UI
    /// </summary>
    internal static class Cli
    {
        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private static bool _consoleReady;

        /// <summary>
        /// Quando false, não cria console próprio: usado nos modos disparados pela interface
        /// via UAC, onde um AllocConsole faria piscar uma janela preta na cara do usuário.
        /// </summary>
        private static bool _allowAllocConsole = true;

        private static void EnsureConsole()
        {
            if (_consoleReady) return;
            _consoleReady = true;
            try
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    if (!_allowAllocConsole) return;
                    AllocConsole();
                }
                StreamWriter so = new StreamWriter(Console.OpenStandardOutput());
                so.AutoFlush = true;
                Console.SetOut(so);
                StreamWriter se = new StreamWriter(Console.OpenStandardError());
                se.AutoFlush = true;
                Console.SetError(se);
            }
            catch (Exception) { }
        }

        private static void Out(string s)
        {
            EnsureConsole();
            try { Console.Out.WriteLine(s); }
            catch (Exception) { }
        }

        /// <summary>Retorna null quando a linha de comando pede a interface gráfica.</summary>
        public static bool IsCliMode(string[] args, out string mode)
        {
            mode = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].TrimStart('-', '/').ToLowerInvariant();
                switch (a)
                {
                    case "json":
                    case "watch":
                    case "text":
                    case "list":
                    case "headless":
                    case "kill":
                    case "help":
                    case "h":
                    case "?":
                    case "version":
                    case "icon-preview":
                    case "install-startup":
                    case "uninstall-startup":
                        mode = a;
                        return true;
                }
            }
            return false;
        }

        public static int Run(string[] args, string mode)
        {
            int interval = GetInt(args, "interval", 1000);
            int count = GetInt(args, "count", 0);
            int duration = GetInt(args, "duration", 0);
            int topN = GetInt(args, "top", 0);
            int minMb = GetInt(args, "min-mb", 0);
            int warmup = GetInt(args, "warmup", 800);
            int pid = GetInt(args, "kill", 0);
            string outPath = GetStr(args, "out", SnapshotJson.DefaultPath);
            string jsonl = GetStr(args, "jsonl", null);
            bool force = Has(args, "force");
            bool compact = Has(args, "compact");
            long minBytes = (long)minMb * 1024L * 1024L;

            if (mode == "help" || mode == "h" || mode == "?")
            {
                PrintHelp();
                return 0;
            }
            if (mode == "version")
            {
                Out(AppInfo.NameWithVersion +
                    "  ·  schema JSON " + SnapshotJson.Schema.ToString(CultureInfo.InvariantCulture) +
                    "  ·  " + AppInfo.Repo);
                return 0;
            }

            // Os modos headless também respeitam o idioma salvo (o --text é legível).
            Settings st = Settings.Load();
            I18n.Init(I18n.Resolve(st.Language));

            // Estes dois existem para a própria interface se relançar elevada e gravar o
            // atalho em shell:common startup, que exige administrador.
            if (mode == "install-startup" || mode == "uninstall-startup")
            {
                _allowAllocConsole = false;   // chamado pela UI via UAC: nada de console piscando
                return DoStartup(mode == "install-startup", Has(args, "all-users"));
            }

            if (mode == "kill")
                return DoKill(pid, force);

            if (mode == "icon-preview")
                return IconPreview(GetStr(args, "icon-preview", "gauge.png"));

            using (GpuSampler sampler = new GpuSampler())
            {
                if (!sampler.Ready)
                {
                    Out("{\"error\":\"" + (sampler.InitError ?? "contadores indisponiveis") + "\"}");
                    return 2;
                }

                ProcessCatalog catalog = new ProcessCatalog();
                catalog.ScanServicesSync();

                // A utilização dos motores de GPU precisa de duas amostras com intervalo.
                sampler.Sample();
                Thread.Sleep(Math.Max(100, mode == "headless" || mode == "watch" ? interval : warmup));

                if (mode == "json" || mode == "text" || mode == "list")
                {
                    GpuSnapshot snap = sampler.Sample();
                    catalog.Sync(PidsOf(snap));
                    if (mode == "json")
                        Out(SnapshotJson.Build(snap, catalog, "cli", !compact, topN, minBytes));
                    else
                        PrintText(snap, catalog, topN > 0 ? topN : 20, minBytes);
                    return 0;
                }

                // --watch / --headless
                DateTime stopAt = duration > 0
                    ? DateTime.UtcNow.AddSeconds(duration)
                    : DateTime.MaxValue;
                int emitted = 0;
                bool headless = mode == "headless";

                if (headless)
                    Out(I18n.F("cli.bridgeActive", outPath, interval) +
                        (jsonl != null ? I18n.F("cli.bridgeHistory", jsonl) : ""));

                while (DateTime.UtcNow < stopAt && (count <= 0 || emitted < count))
                {
                    GpuSnapshot snap = sampler.Sample();
                    catalog.Sync(PidsOf(snap));
                    emitted++;

                    if (headless)
                    {
                        try
                        {
                            SnapshotJson.WriteFile(outPath,
                                SnapshotJson.Build(snap, catalog, "headless", true, topN, minBytes));
                        }
                        catch (Exception ex)
                        {
                            Out(I18n.F("cli.writeError", outPath, ex.Message));
                        }
                    }
                    else
                    {
                        Out(SnapshotJson.Build(snap, catalog, "watch", false, topN, minBytes));
                    }

                    if (jsonl != null)
                    {
                        try
                        {
                            SnapshotJson.AppendLine(jsonl,
                                SnapshotJson.Build(snap, catalog, mode, false, topN, minBytes));
                        }
                        catch (Exception) { }
                    }

                    if (count > 0 && emitted >= count) break;
                    Thread.Sleep(Math.Max(100, interval));
                }
                return 0;
            }
        }

        private static List<int> PidsOf(GpuSnapshot snap)
        {
            List<int> pids = new List<int>(snap.Processes.Count);
            for (int i = 0; i < snap.Processes.Count; i++)
                pids.Add(snap.Processes[i].Pid);
            return pids;
        }

        /// <summary>Ferramenta de conferência: grava o ícone da bandeja em vários níveis.</summary>
        private static int IconPreview(string path)
        {
            int[] pcts = new int[] { 7, 34, 62, 88, 93, 100 };
            int[] sizes = new int[] { 16, 20, 24, 32 };
            using (System.Drawing.Bitmap sheet =
                   new System.Drawing.Bitmap(pcts.Length * 40 + 10, sizes.Length * 40 + 10))
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(sheet))
            {
                g.Clear(System.Drawing.Color.FromArgb(32, 32, 34));
                for (int r = 0; r < sizes.Length; r++)
                {
                    for (int c = 0; c < pcts.Length; c++)
                    {
                        IntPtr h;
                        using (System.Drawing.Icon ic = TrayGauge.Create(pcts[c], out h))
                        {
                            g.DrawIcon(ic, new System.Drawing.Rectangle(
                                10 + c * 40, 10 + r * 40, sizes[r], sizes[r]));
                        }
                        TrayGauge.Destroy(h);
                    }
                }
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Out("gravado: " + path);
            return 0;
        }

        /// <summary>Instala ou remove o atalho de inicialização. Exit 0 = ok, 1 = falhou.</summary>
        private static int DoStartup(bool install, bool allUsers)
        {
            Json j = new Json(true);
            j.Obj();
            j.Bool("install", install);
            j.Bool("allUsers", allUsers);
            j.Str("path", Startup.PathFor(allUsers));
            try
            {
                if (install) Startup.Install(allUsers);
                else Startup.Uninstall(allUsers);
                j.Bool("ok", true);
                j.EndObj();
                Out(j.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                j.Bool("ok", false);
                j.Str("error", ex.Message);
                j.EndObj();
                Out(j.ToString());
                return 1;
            }
        }

        // ------------------------------------------------------------------- kill
        private static int DoKill(int pid, bool force)
        {
            if (pid <= 0)
            {
                Out("{\"ok\":false,\"reason\":\"pid-invalido\"}");
                return 64;
            }

            ProcessCatalog catalog = new ProcessCatalog();
            catalog.ScanServicesSync();
            List<int> one = new List<int>();
            one.Add(pid);
            catalog.Sync(one);
            ProcInfo pi = catalog.Get(pid);

            Json j = new Json(true);
            j.Obj();
            j.Num("pid", pid);
            j.Str("name", pi != null ? pi.Name : "");
            j.Str("risk", pi != null ? SnapshotJson.RiskCode(pi.Risk) : "unknown");

            if (pi != null && pi.Risk == RiskLevel.Critical)
            {
                j.Bool("ok", false);
                j.Str("reason", "critical-blocked");
                j.Str("message", I18n.T("risk.note.critical"));
                j.EndObj();
                Out(j.ToString());
                return 3;
            }

            bool risky = pi != null && (pi.Risk == RiskLevel.System || pi.Risk == RiskLevel.Elevated);
            if (risky && !force)
            {
                j.Bool("ok", false);
                j.Str("reason", "needs-force");
                j.Str("message", "Risk level " + SnapshotJson.RiskCode(pi.Risk) +
                                 ": repeat with --force to confirm.");
                if (pi.Services.Count > 0) j.Str("services", pi.ServicesText);
                j.EndObj();
                Out(j.ToString());
                return 4;
            }

            KillResult res = ProcessCatalog.Kill(pid);
            if (res.Outcome == KillOutcome.AccessDenied && force)
                res = ProcessCatalog.KillElevated(pid);

            j.Bool("ok", res.Outcome == KillOutcome.Success);
            j.Str("outcome", res.Outcome.ToString().ToLowerInvariant());
            j.Str("message", res.Message);
            j.Bool("viaElevation", res.Elevate);
            j.EndObj();
            Out(j.ToString());

            if (res.Outcome == KillOutcome.Success) return 0;
            if (res.Outcome == KillOutcome.NotFound) return 5;
            if (res.Outcome == KillOutcome.AccessDenied) return 6;
            return 1;
        }

        // ------------------------------------------------------------------- texto
        private static void PrintText(GpuSnapshot snap, ProcessCatalog catalog, int topN, long minBytes)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(AppInfo.NameWithVersion + "  ·  " +
                          DateTime.Now.ToString("G", CultureInfo.CurrentCulture));
            sb.AppendLine();

            for (int i = 0; i < snap.Adapters.Count; i++)
            {
                GpuAdapter a = snap.Adapters[i];
                if (a.DedicatedTotal <= 0 && a.DedicatedUsed <= 0 && a.SharedUsed <= 0) continue;
                double p = a.DedicatedTotal > 0 ? 100.0 * a.DedicatedUsed / a.DedicatedTotal : 0;
                sb.AppendLine(a.Label);
                sb.AppendLine("  " + Pad(I18n.T("cli.dedicated"), 15) + Pad(Fmt.Bytes(a.DedicatedUsed), 12) + " / " +
                              Fmt.Gb(a.DedicatedTotal) + " GB   " +
                              p.ToString("N1", CultureInfo.CurrentCulture) + "%");
                sb.AppendLine("  " + Pad(I18n.T("cli.shared"), 15) + Pad(Fmt.Bytes(a.SharedUsed), 12) + " / " +
                              Fmt.Gb(a.SharedTotal) + " GB");
                sb.AppendLine("  " + Pad(I18n.T("cli.totalGpu"), 15) + Pad(Fmt.Bytes(a.DedicatedUsed + a.SharedUsed), 12) +
                              " / " + Fmt.Gb(a.DedicatedTotal + a.SharedTotal) + " GB");
                sb.AppendLine();
            }

            List<GpuProcess> procs = new List<GpuProcess>(snap.Processes);
            procs.Sort(delegate(GpuProcess a, GpuProcess b) { return b.Local.CompareTo(a.Local); });

            sb.AppendLine(Pad(I18n.T("cli.colPid"), 8) + Pad(I18n.T("cli.colProcess"), 24) +
                          PadL(I18n.T("cli.colDedicated"), 12) + PadL(I18n.T("cli.colShared"), 12) +
                          PadL(I18n.T("cli.colTotal"), 12) + PadL(I18n.T("cli.colGpu"), 7) +
                          "  " + I18n.T("cli.colType"));
            sb.AppendLine(new string('-', 92));

            long sd = 0, ss = 0;
            int shown = 0;
            for (int i = 0; i < procs.Count; i++)
            {
                GpuProcess p = procs[i];
                sd += p.Local;
                ss += p.NonLocal;
                if (p.TotalResident < minBytes) continue;
                if (topN > 0 && shown >= topN) continue;
                shown++;
                ProcInfo pi = catalog.Get(p.Pid);
                sb.AppendLine(
                    Pad(p.Pid.ToString(CultureInfo.InvariantCulture), 8) +
                    Pad(pi != null ? Cut(pi.Name, 22) : "?", 24) +
                    PadL(Fmt.Bytes(p.Local), 12) +
                    PadL(Fmt.Bytes(p.NonLocal), 12) +
                    PadL(Fmt.Bytes(p.TotalResident), 12) +
                    PadL(p.EnginePercent > 0.05
                        ? p.EnginePercent.ToString("N1", CultureInfo.CurrentCulture) : "-", 7) +
                    "  " + (pi != null ? pi.RiskText : "?"));
            }
            sb.AppendLine(new string('-', 92));
            sb.AppendLine(I18n.F("cli.sum", Fmt.Bytes(sd), Fmt.Bytes(ss), Fmt.Bytes(sd + ss), procs.Count));
            Out(sb.ToString());
        }

        private static string Cut(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        private static string Pad(string s, int n)
        {
            if (s == null) s = "";
            return s.Length >= n ? s + " " : s.PadRight(n);
        }

        private static string PadL(string s, int n)
        {
            if (s == null) s = "";
            return s.Length >= n ? s + " " : s.PadLeft(n);
        }

        // ---------------------------------------------------------------- opções
        private static bool Has(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i].TrimStart('-', '/'), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string GetStr(string[] args, string name, string def)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].TrimStart('-', '/');
                int eq = a.IndexOf('=');
                if (eq > 0 && string.Equals(a.Substring(0, eq), name, StringComparison.OrdinalIgnoreCase))
                    return a.Substring(eq + 1).Trim('"');
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string next = args[i + 1];
                    if (!next.StartsWith("-") && !next.StartsWith("/")) return next.Trim('"');
                }
            }
            return def;
        }

        private static int GetInt(string[] args, string name, int def)
        {
            string s = GetStr(args, name, null);
            int v;
            if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return v;
            return def;
        }

        private static void PrintHelp()
        {
            Out(
AppInfo.NameWithVersion + " — memoria de GPU por processo\r\n" +
AppInfo.Repo + "\r\n" +
"\r\n" +
"Sem argumentos abre a janela (com icone na area de notificacoes).\r\n" +
"\r\n" +
"MODOS HEADLESS\r\n" +
"  --json                  imprime um snapshot JSON e sai\r\n" +
"  --watch                 imprime um JSON por linha, continuamente\r\n" +
"  --text                  tabela legivel no console\r\n" +
"  --headless              sem janela; mantem o arquivo da ponte atualizado\r\n" +
"  --kill PID [--force]    encerra com as mesmas travas da interface\r\n" +
"  --help | --version\r\n" +
"\r\n" +
"OPCOES\r\n" +
"  --interval MS   periodo de amostragem (padrao 1000)\r\n" +
"  --count N       numero de amostras em --watch (0 = infinito)\r\n" +
"  --duration S    encerra depois de S segundos\r\n" +
"  --top N         limita aos N processos que mais usam VRAM\r\n" +
"  --min-mb N      ignora processos abaixo de N MB de memoria de GPU\r\n" +
"  --out PATH      arquivo da ponte (padrao " + SnapshotJson.DefaultPath + ")\r\n" +
"  --jsonl PATH    tambem acumula historico, um JSON por linha\r\n" +
"  --compact       JSON sem indentacao\r\n" +
"  --warmup MS     espera antes da amostra final em --json (padrao 800)\r\n" +
"\r\n" +
"CODIGOS DE SAIDA DO --kill\r\n" +
"  0 ok · 3 processo critico (bloqueado) · 4 exige --force\r\n" +
"  5 processo nao existe · 6 acesso negado · 1 outra falha\r\n" +
"\r\n" +
"A janela tambem mantem o arquivo da ponte atualizado a cada amostra,\r\n" +
"entao basta ler o JSON enquanto o monitor estiver aberto.");
        }
    }
}
