using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace VramMonitor
{
    internal enum RiskLevel
    {
        Normal = 0,
        Elevated = 1,
        System = 2,
        Critical = 3
    }

    internal sealed class ProcInfo
    {
        public int Pid;
        public string Name = "";
        public string ExePath = "";
        public string FileDescription = "";
        public string Company = "";
        public string User = "";
        public string UserSid = "";
        public uint SessionId;
        public bool? Elevated;        // null = nao foi possivel consultar o token
        public bool Critical;
        public bool HandleDenied;
        public long CreationTime;
        public RiskLevel Risk;
        public List<string> Services = new List<string>();

        public string ServicesText
        {
            get { return Services.Count == 0 ? "" : string.Join(", ", Services.ToArray()); }
        }

        /// <summary>Chave do i18n do texto de risco, resolvida na hora de exibir.</summary>
        public string RiskKey
        {
            get
            {
                switch (Risk)
                {
                    case RiskLevel.Critical: return "risk.critical";
                    case RiskLevel.System: return "risk.system";
                    case RiskLevel.Elevated: return Elevated.HasValue ? "risk.elevated" : "risk.noAccess";
                    default: return "risk.user";
                }
            }
        }

        public string RiskText
        {
            get { return I18n.T(RiskKey); }
        }

        /// <summary>
        /// Chave e argumento da explicação de risco. Guardar a chave em vez do texto pronto
        /// permite trocar de idioma sem reclassificar os processos.
        /// </summary>
        public string RiskNoteKey = "";
        public string RiskNoteArg = "";

        public string RiskNote
        {
            get
            {
                if (RiskNoteKey.Length == 0) return "";
                if (RiskNoteKey == "risk.note.system")
                    return I18n.F(RiskNoteKey,
                        RiskNoteArg.Length > 0 ? RiskNoteArg : I18n.T("risk.accountFallback"));
                return RiskNoteArg.Length > 0 ? I18n.F(RiskNoteKey, RiskNoteArg) : I18n.T(RiskNoteKey);
            }
        }

        public string Detail
        {
            get
            {
                string s = ServicesText;
                if (s.Length > 0) return "svc: " + s;
                if (FileDescription.Length > 0) return FileDescription;
                return ExePath;
            }
        }
    }

    internal enum KillOutcome
    {
        Success,
        AccessDenied,
        NotFound,
        Failed
    }

    internal sealed class KillResult
    {
        public KillOutcome Outcome;
        public string Message = "";
        public bool Elevate;   // true quando foi feito via taskkill elevado
    }

    /// <summary>
    /// Metadados de processo (nome, caminho, usuario, elevacao, criticidade, servicos)
    /// com cache por PID e deteccao de reuso de PID pelo horario de criacao.
    /// </summary>
    internal sealed class ProcessCatalog
    {
        // Matar qualquer um destes derruba o Windows (bugcheck) ou o logon.
        private static readonly HashSet<string> CriticalNames = new HashSet<string>(
            new string[]
            {
                "system", "system idle process", "registry", "secure system", "memory compression",
                "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "fontdrvhost"
            }, StringComparer.OrdinalIgnoreCase);

        // Sistema, mas o Windows reinicia sozinho / impacto limitado. O valor é a chave do i18n.
        private static readonly Dictionary<string, string> RestartNotes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "dwm", "restart.dwm" },
                { "explorer", "restart.explorer" },
                { "svchost", "restart.svchost" },
                { "audiodg", "restart.audiodg" },
                { "sihost", "restart.sihost" },
                { "searchhost", "restart.searchhost" },
                { "runtimebroker", "restart.runtimebroker" },
                { "textinputhost", "restart.textinputhost" },
                { "startmenuexperiencehost", "restart.startmenuexperiencehost" },
                { "shellexperiencehost", "restart.shellexperiencehost" },
                { "wudfhost", "restart.wudfhost" },
                { "nvdisplay.container", "restart.nvdisplay" },
                { "amddvr", "restart.amddvr" },
                { "radeonsoftware", "restart.radeonsoftware" },
                { "amdow", "restart.amdow" },
            };

        private readonly Dictionary<int, ProcInfo> _cache = new Dictionary<int, ProcInfo>();
        private Dictionary<int, List<string>> _serviceMap = new Dictionary<int, List<string>>();
        private DateTime _lastServiceScan = DateTime.MinValue;
        private int _serviceScanRunning;

        public ProcInfo Get(int pid)
        {
            ProcInfo pi;
            return _cache.TryGetValue(pid, out pi) ? pi : null;
        }

        /// <summary>Garante metadados atualizados para os PIDs informados.</summary>
        public void Sync(ICollection<int> pids)
        {
            MaybeScanServices();

            List<int> missing = new List<int>();
            foreach (int pid in pids)
            {
                ProcInfo pi;
                if (!_cache.TryGetValue(pid, out pi))
                {
                    missing.Add(pid);
                    continue;
                }
                // PID reciclado?
                if (pi.CreationTime != 0)
                {
                    long ct = ReadCreationTime(pid);
                    if (ct != 0 && ct != pi.CreationTime)
                        missing.Add(pid);
                }
            }

            if (missing.Count == 0)
            {
                ApplyServices(pids);
                return;
            }

            // Um unico enumerate resolve o nome de todos os PIDs (funciona ate para processos protegidos).
            Dictionary<int, string> names = new Dictionary<int, string>();
            try
            {
                Process[] all = Process.GetProcesses();
                for (int i = 0; i < all.Length; i++)
                {
                    try { names[all[i].Id] = all[i].ProcessName; }
                    catch (Exception) { }
                    all[i].Dispose();
                }
            }
            catch (Exception) { }

            for (int i = 0; i < missing.Count; i++)
            {
                int pid = missing[i];
                string nm;
                if (!names.TryGetValue(pid, out nm)) nm = "";
                _cache[pid] = Build(pid, nm);
            }

            ApplyServices(pids);
        }

        private void ApplyServices(ICollection<int> pids)
        {
            Dictionary<int, List<string>> map = _serviceMap;
            foreach (int pid in pids)
            {
                ProcInfo pi;
                if (!_cache.TryGetValue(pid, out pi)) continue;
                List<string> svc;
                if (map.TryGetValue(pid, out svc))
                {
                    if (pi.Services.Count != svc.Count)
                    {
                        pi.Services = svc;
                        Classify(pi);
                    }
                }
                else if (pi.Services.Count > 0)
                {
                    pi.Services = new List<string>();
                    Classify(pi);
                }
            }
        }

        public void Forget(int pid)
        {
            _cache.Remove(pid);
        }

        // ------------------------------------------------------------- construcao
        private ProcInfo Build(int pid, string name)
        {
            ProcInfo pi = new ProcInfo();
            pi.Pid = pid;
            pi.Name = string.IsNullOrEmpty(name) ? "(pid " + pid + ")" : name;

            uint sess;
            if (Native.ProcessIdToSessionId((uint)pid, out sess))
                pi.SessionId = sess;

            if (pid <= 4)
            {
                pi.Critical = true;
                pi.HandleDenied = true;
                pi.User = "NT AUTHORITY\\SYSTEM";
                pi.UserSid = "S-1-5-18";
                Classify(pi);
                return pi;
            }

            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
            if (h == IntPtr.Zero)
            {
                pi.HandleDenied = true;
            }
            else
            {
                try
                {
                    long ct, et, kt, ut;
                    if (Native.GetProcessTimes(h, out ct, out et, out kt, out ut))
                        pi.CreationTime = ct;

                    StringBuilder sb = new StringBuilder(1024);
                    uint cap = (uint)sb.Capacity;
                    if (Native.QueryFullProcessImageNameW(h, 0, sb, ref cap))
                        pi.ExePath = sb.ToString();

                    bool crit;
                    try
                    {
                        if (Native.IsProcessCritical(h, out crit))
                            pi.Critical = crit;
                    }
                    catch (EntryPointNotFoundException) { }
                    catch (DllNotFoundException) { }

                    IntPtr token;
                    if (Native.OpenProcessToken(h, Native.TOKEN_QUERY, out token))
                    {
                        try
                        {
                            pi.Elevated = ReadElevation(token);
                            ReadUser(token, pi);
                        }
                        finally
                        {
                            Native.CloseHandle(token);
                        }
                    }
                }
                finally
                {
                    Native.CloseHandle(h);
                }
            }

            if (pi.ExePath.Length > 0)
            {
                try
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(pi.ExePath);
                    pi.FileDescription = (fvi.FileDescription ?? "").Trim();
                    pi.Company = (fvi.CompanyName ?? "").Trim();
                }
                catch (Exception) { }
            }

            List<string> svc;
            if (_serviceMap.TryGetValue(pid, out svc))
                pi.Services = svc;

            Classify(pi);
            return pi;
        }

        private static long ReadCreationTime(int pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
            if (h == IntPtr.Zero) return 0;
            try
            {
                long ct, et, kt, ut;
                if (Native.GetProcessTimes(h, out ct, out et, out kt, out ut))
                    return ct;
                return 0;
            }
            finally
            {
                Native.CloseHandle(h);
            }
        }

        private static bool? ReadElevation(IntPtr token)
        {
            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                int ret;
                if (Native.GetTokenInformation(token, Native.TokenElevation, buf, 4, out ret))
                    return Marshal.ReadInt32(buf) != 0;
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static void ReadUser(IntPtr token, ProcInfo pi)
        {
            int need;
            Native.GetTokenInformation(token, Native.TokenUser, IntPtr.Zero, 0, out need);
            if (need <= 0) return;

            IntPtr buf = Marshal.AllocHGlobal(need);
            try
            {
                int ret;
                if (!Native.GetTokenInformation(token, Native.TokenUser, buf, need, out ret))
                    return;

                Native.SID_AND_ATTRIBUTES sa =
                    (Native.SID_AND_ATTRIBUTES)Marshal.PtrToStructure(buf, typeof(Native.SID_AND_ATTRIBUTES));
                if (sa.Sid == IntPtr.Zero) return;

                SecurityIdentifier sid = new SecurityIdentifier(sa.Sid);
                pi.UserSid = sid.Value;
                try
                {
                    NTAccount acct = (NTAccount)sid.Translate(typeof(NTAccount));
                    pi.User = acct.Value;
                }
                catch (Exception)
                {
                    pi.User = sid.Value;
                }
            }
            catch (Exception) { }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static void Classify(ProcInfo pi)
        {
            string bare = pi.Name ?? "";
            if (bare.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                bare = bare.Substring(0, bare.Length - 4);

            bool isSystemSid = pi.UserSid == "S-1-5-18" || pi.UserSid == "S-1-5-19" || pi.UserSid == "S-1-5-20";

            if (pi.Critical || CriticalNames.Contains(bare) || pi.Pid <= 4)
            {
                pi.Risk = RiskLevel.Critical;
                pi.RiskNoteKey = "risk.note.critical";
                pi.RiskNoteArg = "";
                return;
            }

            string note;
            RestartNotes.TryGetValue(bare, out note);
            pi.RiskNoteArg = "";

            if (isSystemSid || pi.SessionId == 0 || pi.Services.Count > 0)
            {
                pi.Risk = RiskLevel.System;
                if (note != null)
                {
                    pi.RiskNoteKey = note;
                }
                else
                {
                    // O texto sai do i18n na hora de exibir, para trocar de idioma sem reclassificar.
                    pi.RiskNoteKey = "risk.note.system";
                    pi.RiskNoteArg = pi.User;
                }
                return;
            }

            if (pi.Elevated.HasValue && pi.Elevated.Value)
            {
                pi.Risk = RiskLevel.Elevated;
                pi.RiskNoteKey = note != null ? note : "risk.note.elevated";
                return;
            }

            if (!pi.Elevated.HasValue && pi.HandleDenied)
            {
                pi.Risk = RiskLevel.Elevated;
                pi.RiskNoteKey = "risk.note.denied";
                return;
            }

            pi.Risk = RiskLevel.Normal;
            pi.RiskNoteKey = note != null ? note : "risk.note.normal";
        }

        // -------------------------------------------------------------- servicos
        /// <summary>Consulta os serviços em primeiro plano (usada pelos modos headless).</summary>
        public void ScanServicesSync()
        {
            Dictionary<int, List<string>> map = QueryServices();
            if (map != null) _serviceMap = map;
            _lastServiceScan = DateTime.UtcNow;
        }

        private static Dictionary<int, List<string>> QueryServices()
        {
            Dictionary<int, List<string>> map = new Dictionary<int, List<string>>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT ProcessId, Name, DisplayName FROM Win32_Service WHERE State = 'Running'"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        try
                        {
                            object opid = mo["ProcessId"];
                            if (opid == null) continue;
                            int pid = Convert.ToInt32(opid, CultureInfo.InvariantCulture);
                            if (pid <= 0) continue;
                            string nm = mo["Name"] as string;
                            if (string.IsNullOrEmpty(nm)) nm = mo["DisplayName"] as string;
                            if (string.IsNullOrEmpty(nm)) continue;

                            List<string> list;
                            if (!map.TryGetValue(pid, out list))
                            {
                                list = new List<string>();
                                map[pid] = list;
                            }
                            list.Add(nm);
                        }
                        finally
                        {
                            mo.Dispose();
                        }
                    }
                }
                return map;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void MaybeScanServices()
        {
            if ((DateTime.UtcNow - _lastServiceScan).TotalSeconds < 15) return;
            if (Interlocked.CompareExchange(ref _serviceScanRunning, 1, 0) != 0) return;
            _lastServiceScan = DateTime.UtcNow;

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    Dictionary<int, List<string>> map = QueryServices();
                    if (map != null) _serviceMap = map;
                }
                catch (Exception) { }
                finally
                {
                    Interlocked.Exchange(ref _serviceScanRunning, 0);
                }
            });
        }

        // ------------------------------------------------------------------ kill
        /// <summary>
        /// Encerra o processo. <paramref name="expectedCreationTime"/> (0 = não verificar) protege
        /// contra reuso de PID: entre a amostra e o clique o processo pode ter morrido e o Windows
        /// pode ter reciclado o número para outro — matar "o PID" sem conferir identidade encerraria
        /// um processo inocente.
        /// </summary>
        public static KillResult Kill(int pid, long expectedCreationTime)
        {
            KillResult r = new KillResult();

            if (expectedCreationTime != 0)
            {
                long now = ReadCreationTime(pid);
                if (now != 0 && now != expectedCreationTime)
                {
                    r.Outcome = KillOutcome.NotFound;
                    r.Message = I18n.F("killmsg.pidRecycled", pid);
                    return r;
                }
            }

            IntPtr h = Native.OpenProcess(Native.PROCESS_TERMINATE, false, (uint)pid);
            if (h == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == Native.ERROR_ACCESS_DENIED)
                {
                    r.Outcome = KillOutcome.AccessDenied;
                    r.Message = I18n.T("killmsg.accessDenied");
                }
                else if (err == Native.ERROR_INVALID_PARAMETER)
                {
                    r.Outcome = KillOutcome.NotFound;
                    r.Message = I18n.F("killmsg.notFound", pid);
                }
                else
                {
                    r.Outcome = KillOutcome.Failed;
                    r.Message = I18n.F("killmsg.openFailed", new Win32Exception(err).Message, err);
                }
                return r;
            }

            try
            {
                if (Native.TerminateProcess(h, 1))
                {
                    r.Outcome = KillOutcome.Success;
                    r.Message = I18n.F("killmsg.terminated", pid);
                    return r;
                }
                int err = Marshal.GetLastWin32Error();
                r.Outcome = err == Native.ERROR_ACCESS_DENIED ? KillOutcome.AccessDenied : KillOutcome.Failed;
                r.Message = I18n.F("killmsg.terminateFailed", new Win32Exception(err).Message, err);
                return r;
            }
            finally
            {
                Native.CloseHandle(h);
            }
        }

        /// <summary>Fallback elevado: dispara taskkill via UAC.</summary>
        public static KillResult KillElevated(int pid)
        {
            KillResult r = new KillResult();
            r.Elevate = true;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "taskkill.exe";
                psi.Arguments = "/F /PID " + pid.ToString(CultureInfo.InvariantCulture);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(8000);
                    if (p.HasExited && p.ExitCode != 0)
                    {
                        r.Outcome = KillOutcome.Failed;
                        r.Message = I18n.F("killmsg.taskkillCode", p.ExitCode);
                        p.Dispose();
                        return r;
                    }
                    p.Dispose();
                }
                r.Outcome = KillOutcome.Success;
                r.Message = I18n.F("killmsg.elevatedRan", pid);
            }
            catch (Win32Exception ex)
            {
                r.Outcome = KillOutcome.Failed;
                r.Message = ex.NativeErrorCode == 1223
                    ? I18n.T("killmsg.uacCancelled")
                    : I18n.F("killmsg.elevateFailed", ex.Message);
            }
            catch (Exception ex)
            {
                r.Outcome = KillOutcome.Failed;
                r.Message = I18n.F("killmsg.elevateFailed", ex.Message);
            }
            return r;
        }

        public static bool IsCurrentProcessElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
