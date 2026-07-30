using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VramMonitor
{
    /// <summary>Um bloco de memoria de GPU de um processo em um segmento fisico do adaptador.</summary>
    internal sealed class GpuSegment
    {
        public string LuidKey;
        public int PhysIndex;
        public long Local;      // residente na VRAM fisica
        public long NonLocal;   // "spill": residente na RAM do sistema (memoria compartilhada)
        public long Dedicated;  // dedicada COMPROMETIDA (inclui compartilhamento entre processos)
        public long Shared;     // compartilhada comprometida
        public long Committed;  // total comprometido
    }

    internal sealed class GpuProcess
    {
        public int Pid;
        public long Local;
        public long NonLocal;
        public long Dedicated;
        public long Shared;
        public long Committed;
        public long TotalResident;      // Local + NonLocal = "Memoria da GPU" do Gerenciador de Tarefas
        public double EnginePercent;    // maior utilizacao entre os motores
        public string TopEngine = "";
        public readonly List<GpuSegment> Segments = new List<GpuSegment>();
        public readonly Dictionary<string, double> Engines =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class GpuAdapter
    {
        public string LuidKey;
        public string Name;
        public long DedicatedTotal;   // VRAM fisica (DXGI)
        public long SharedTotal;      // limite de memoria compartilhada (DXGI)
        public long DedicatedUsed;
        public long SharedUsed;
        public long Committed;
        public bool IsSoftware;
        public bool KnownFromDxgi;

        public string Label
        {
            get { return string.IsNullOrEmpty(Name) ? ("Adaptador " + LuidKey) : Name; }
        }
    }

    internal sealed class GpuSnapshot
    {
        public readonly List<GpuAdapter> Adapters = new List<GpuAdapter>();
        public readonly List<GpuProcess> Processes = new List<GpuProcess>();
        public DateTime Taken;
        public string Warning;
    }

    /// <summary>
    /// Le os contadores PDH de GPU do Windows (a mesma fonte usada pelo Gerenciador de Tarefas)
    /// e agrega por PID / adaptador / segmento.
    /// </summary>
    internal sealed class GpuSampler : IDisposable
    {
        // Por processo
        private const string P_LOCAL = @"\GPU Process Memory(*)\Local Usage";
        private const string P_NONLOCAL = @"\GPU Process Memory(*)\Non Local Usage";
        private const string P_DEDICATED = @"\GPU Process Memory(*)\Dedicated Usage";
        private const string P_SHARED = @"\GPU Process Memory(*)\Shared Usage";
        private const string P_COMMITTED = @"\GPU Process Memory(*)\Total Committed";
        private const string P_ENGINE = @"\GPU Engine(*)\Utilization Percentage";
        // Por adaptador
        private const string A_DEDICATED = @"\GPU Adapter Memory(*)\Dedicated Usage";
        private const string A_COMMITTED = @"\GPU Adapter Memory(*)\Total Committed";
        private const string A_NONLOCAL = @"\GPU Non Local Adapter Memory(*)\Non Local Usage";

        private static readonly string[] AllPaths = new string[]
        {
            P_LOCAL, P_NONLOCAL, P_DEDICATED, P_SHARED, P_COMMITTED, P_ENGINE,
            A_DEDICATED, A_COMMITTED, A_NONLOCAL
        };

        private IntPtr _query = IntPtr.Zero;
        private readonly Dictionary<string, IntPtr> _counters = new Dictionary<string, IntPtr>();
        private readonly Dictionary<string, GpuAdapter> _dxgi =
            new Dictionary<string, GpuAdapter>(StringComparer.OrdinalIgnoreCase);
        private bool _firstCollectDone;

        public string InitError { get; private set; }

        public GpuSampler()
        {
            LoadDxgiAdapters();

            uint rc = Native.PdhOpenQueryW(null, IntPtr.Zero, out _query);
            if (rc != 0)
            {
                _query = IntPtr.Zero;
                InitError = "PdhOpenQuery falhou (0x" + rc.ToString("X8") + ").";
                return;
            }

            int added = 0;
            for (int i = 0; i < AllPaths.Length; i++)
            {
                IntPtr hc;
                if (Native.PdhAddEnglishCounterW(_query, AllPaths[i], IntPtr.Zero, out hc) == 0)
                {
                    _counters[AllPaths[i]] = hc;
                    added++;
                }
            }

            if (added == 0)
            {
                InitError = "Nenhum contador de GPU disponível. Os contadores 'GPU Process Memory' " +
                           "exigem Windows 10 1709+ com driver WDDM 2.x.";
                return;
            }

            Native.PdhCollectQueryData(_query);
            _firstCollectDone = true;
        }

        public bool Ready
        {
            get { return _query != IntPtr.Zero && _counters.Count > 0; }
        }

        // -------------------------------------------------------------- amostra
        public GpuSnapshot Sample()
        {
            GpuSnapshot snap = new GpuSnapshot();
            snap.Taken = DateTime.Now;
            if (!Ready)
            {
                snap.Warning = InitError;
                return snap;
            }

            Native.PdhCollectQueryData(_query);

            Dictionary<string, long> local = ReadLarge(P_LOCAL);
            Dictionary<string, long> nonLocal = ReadLarge(P_NONLOCAL);
            Dictionary<string, long> dedicated = ReadLarge(P_DEDICATED);
            Dictionary<string, long> shared = ReadLarge(P_SHARED);
            Dictionary<string, long> committed = ReadLarge(P_COMMITTED);
            Dictionary<string, double> engine = ReadDouble(P_ENGINE);

            Dictionary<string, long> aDedicated = ReadLarge(A_DEDICATED);
            Dictionary<string, long> aCommitted = ReadLarge(A_COMMITTED);
            Dictionary<string, long> aNonLocal = ReadLarge(A_NONLOCAL);

            // -------- adaptadores: uniao do que o DXGI conhece com o que os contadores mostram
            Dictionary<string, GpuAdapter> adapters =
                new Dictionary<string, GpuAdapter>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, GpuAdapter> kv in _dxgi)
            {
                GpuAdapter a = new GpuAdapter();
                a.LuidKey = kv.Value.LuidKey;
                a.Name = kv.Value.Name;
                a.DedicatedTotal = kv.Value.DedicatedTotal;
                a.SharedTotal = kv.Value.SharedTotal;
                a.IsSoftware = kv.Value.IsSoftware;
                a.KnownFromDxgi = true;
                adapters[a.LuidKey] = a;
            }

            AccumulateAdapter(adapters, aDedicated, 0);
            AccumulateAdapter(adapters, aNonLocal, 1);
            AccumulateAdapter(adapters, aCommitted, 2);

            // -------- processos
            Dictionary<int, GpuProcess> procs = new Dictionary<int, GpuProcess>();

            foreach (KeyValuePair<string, long> kv in committed)
                TouchSegment(procs, kv.Key);
            foreach (KeyValuePair<string, long> kv in local)
                TouchSegment(procs, kv.Key);

            foreach (KeyValuePair<int, GpuProcess> pkv in procs)
            {
                GpuProcess p = pkv.Value;
                for (int i = 0; i < p.Segments.Count; i++)
                {
                    GpuSegment seg = p.Segments[i];
                    string inst = MakeProcInstance(p.Pid, seg.LuidKey, seg.PhysIndex);
                    seg.Local = Get(local, inst);
                    seg.NonLocal = Get(nonLocal, inst);
                    seg.Dedicated = Get(dedicated, inst);
                    seg.Shared = Get(shared, inst);
                    seg.Committed = Get(committed, inst);

                    p.Local += seg.Local;
                    p.NonLocal += seg.NonLocal;
                    p.Dedicated += seg.Dedicated;
                    p.Shared += seg.Shared;
                    p.Committed += seg.Committed;
                }
                p.TotalResident = p.Local + p.NonLocal;
            }

            // -------- motores (3D / Compute / Copy / VideoDecode ...)
            foreach (KeyValuePair<string, double> kv in engine)
            {
                int pid;
                string luid;
                int phys;
                string engType;
                if (!TryParseEngineInstance(kv.Key, out pid, out luid, out phys, out engType))
                    continue;
                if (kv.Value <= 0.0)
                    continue;

                GpuProcess p;
                if (!procs.TryGetValue(pid, out p))
                {
                    p = new GpuProcess();
                    p.Pid = pid;
                    procs[pid] = p;
                }

                double cur;
                if (p.Engines.TryGetValue(engType, out cur))
                    p.Engines[engType] = cur + kv.Value;
                else
                    p.Engines[engType] = kv.Value;
            }

            foreach (KeyValuePair<int, GpuProcess> pkv in procs)
            {
                GpuProcess p = pkv.Value;
                foreach (KeyValuePair<string, double> e in p.Engines)
                {
                    if (e.Value > p.EnginePercent)
                    {
                        p.EnginePercent = e.Value;
                        p.TopEngine = e.Key;
                    }
                }
                if (p.EnginePercent > 100.0) p.EnginePercent = 100.0;
                snap.Processes.Add(p);
            }

            foreach (KeyValuePair<string, GpuAdapter> kv in adapters)
                snap.Adapters.Add(kv.Value);

            snap.Adapters.Sort(delegate(GpuAdapter x, GpuAdapter y)
            {
                int c = y.DedicatedTotal.CompareTo(x.DedicatedTotal);
                if (c != 0) return c;
                return string.Compare(x.Label, y.Label, StringComparison.OrdinalIgnoreCase);
            });

            return snap;
        }

        private static void AccumulateAdapter(Dictionary<string, GpuAdapter> adapters,
                                              Dictionary<string, long> data, int field)
        {
            foreach (KeyValuePair<string, long> kv in data)
            {
                string luid;
                int phys;
                if (!TryParseAdapterInstance(kv.Key, out luid, out phys))
                    continue;

                GpuAdapter a;
                if (!adapters.TryGetValue(luid, out a))
                {
                    a = new GpuAdapter();
                    a.LuidKey = luid;
                    adapters[luid] = a;
                }
                if (field == 0) a.DedicatedUsed += kv.Value;
                else if (field == 1) a.SharedUsed += kv.Value;
                else a.Committed += kv.Value;
            }
        }

        private static long Get(Dictionary<string, long> d, string key)
        {
            long v;
            return d.TryGetValue(key, out v) ? v : 0L;
        }

        private static void TouchSegment(Dictionary<int, GpuProcess> procs, string instance)
        {
            int pid;
            string luid;
            int phys;
            if (!TryParseProcInstance(instance, out pid, out luid, out phys))
                return;

            GpuProcess p;
            if (!procs.TryGetValue(pid, out p))
            {
                p = new GpuProcess();
                p.Pid = pid;
                procs[pid] = p;
            }
            for (int i = 0; i < p.Segments.Count; i++)
            {
                if (p.Segments[i].PhysIndex == phys &&
                    string.Equals(p.Segments[i].LuidKey, luid, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            GpuSegment seg = new GpuSegment();
            seg.LuidKey = luid;
            seg.PhysIndex = phys;
            p.Segments.Add(seg);
        }

        // ------------------------------------------------- parsing de instancias
        private static string MakeProcInstance(int pid, string luidKey, int phys)
        {
            return "pid_" + pid.ToString(CultureInfo.InvariantCulture) +
                   "_luid_" + luidKey +
                   "_phys_" + phys.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>pid_1952_luid_0x00000000_0x00012D76_phys_0</summary>
        public static bool TryParseProcInstance(string name, out int pid, out string luidKey, out int phys)
        {
            pid = 0;
            luidKey = null;
            phys = 0;
            if (string.IsNullOrEmpty(name) || !name.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
                return false;

            int luidAt = name.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
            if (luidAt < 4) return false;
            if (!int.TryParse(name.Substring(4, luidAt - 4), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out pid))
                return false;

            int physAt = name.IndexOf("_phys_", luidAt, StringComparison.OrdinalIgnoreCase);
            if (physAt < 0) return false;

            luidKey = name.Substring(luidAt + 6, physAt - (luidAt + 6));

            int end = physAt + 6;
            int stop = end;
            while (stop < name.Length && char.IsDigit(name[stop])) stop++;
            if (stop == end) return false;
            return int.TryParse(name.Substring(end, stop - end), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out phys);
        }

        /// <summary>pid_11028_luid_0x00000000_0x00012D76_phys_0_eng_0_engtype_3D</summary>
        public static bool TryParseEngineInstance(string name, out int pid, out string luidKey,
                                                  out int phys, out string engType)
        {
            engType = "";
            if (!TryParseProcInstance(name, out pid, out luidKey, out phys))
                return false;
            int at = name.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
                engType = name.Substring(at + 9);
            if (engType.Length == 0) engType = "Desconhecido";
            return true;
        }

        /// <summary>luid_0x00000000_0x00012D76_phys_0</summary>
        public static bool TryParseAdapterInstance(string name, out string luidKey, out int phys)
        {
            luidKey = null;
            phys = 0;
            if (string.IsNullOrEmpty(name) || !name.StartsWith("luid_", StringComparison.OrdinalIgnoreCase))
                return false;
            int physAt = name.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
            if (physAt < 5) return false;
            luidKey = name.Substring(5, physAt - 5);
            int end = physAt + 6;
            int stop = end;
            while (stop < name.Length && char.IsDigit(name[stop])) stop++;
            if (stop == end) return false;
            return int.TryParse(name.Substring(end, stop - end), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out phys);
        }

        // ------------------------------------------------------------ leitura PDH
        private Dictionary<string, long> ReadLarge(string path)
        {
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            ReadInto(path, Native.PDH_FMT_LARGE, result, null);
            return result;
        }

        private Dictionary<string, double> ReadDouble(string path)
        {
            Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            ReadInto(path, Native.PDH_FMT_DOUBLE, null, result);
            return result;
        }

        private void ReadInto(string path, uint fmt,
                              Dictionary<string, long> asLong,
                              Dictionary<string, double> asDouble)
        {
            IntPtr hc;
            if (!_counters.TryGetValue(path, out hc) || !_firstCollectDone)
                return;

            uint size = 0;
            uint count = 0;
            uint rc = Native.PdhGetFormattedCounterArrayW(hc, fmt, ref size, out count, IntPtr.Zero);
            if (rc != Native.PDH_MORE_DATA || size == 0 || count == 0)
                return;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                rc = Native.PdhGetFormattedCounterArrayW(hc, fmt, ref size, out count, buf);
                if (rc != 0)
                    return;

                int stride = Marshal.SizeOf(typeof(Native.PdhItem));
                for (int i = 0; i < count; i++)
                {
                    IntPtr at = new IntPtr(buf.ToInt64() + (long)i * stride);
                    Native.PdhItem item = (Native.PdhItem)Marshal.PtrToStructure(at, typeof(Native.PdhItem));
                    if (item.szName == IntPtr.Zero) continue;
                    if (item.CStatus != Native.PDH_CSTATUS_VALID_DATA &&
                        item.CStatus != Native.PDH_CSTATUS_NEW_DATA) continue;

                    string name = Marshal.PtrToStringUni(item.szName);
                    if (string.IsNullOrEmpty(name)) continue;

                    if (asLong != null)
                    {
                        long v = item.Value;
                        if (v < 0) v = 0;
                        asLong[name] = v;
                    }
                    else if (asDouble != null)
                    {
                        double d = BitConverter.Int64BitsToDouble(item.Value);
                        if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) d = 0;
                        asDouble[name] = d;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        // ----------------------------------------------------------------- DXGI
        private void LoadDxgiAdapters()
        {
            Native.IDXGIFactory1 factory = null;
            try
            {
                Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                if (Native.CreateDXGIFactory1(ref iid, out factory) != 0 || factory == null)
                    return;

                for (uint i = 0; i < 32; i++)
                {
                    Native.IDXGIAdapter1 adapter = null;
                    if (factory.EnumAdapters1(i, out adapter) != 0 || adapter == null)
                        break;
                    try
                    {
                        Native.DXGI_ADAPTER_DESC1 desc;
                        if (adapter.GetDesc1(out desc) != 0)
                            continue;

                        GpuAdapter a = new GpuAdapter();
                        a.LuidKey = FormatLuid(desc.LuidHighPart, desc.LuidLowPart);
                        a.Name = (desc.Description ?? "").Trim();
                        a.DedicatedTotal = desc.DedicatedVideoMemory.ToInt64();
                        a.SharedTotal = desc.SharedSystemMemory.ToInt64();
                        a.IsSoftware = (desc.Flags & 2u) != 0 || desc.VendorId == 0x1414;
                        a.KnownFromDxgi = true;
                        _dxgi[a.LuidKey] = a;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
            }
            catch (Exception)
            {
                // sem DXGI os adaptadores aparecem apenas pelo LUID
            }
            finally
            {
                if (factory != null)
                {
                    try { Marshal.ReleaseComObject(factory); }
                    catch (Exception) { }
                }
            }
        }

        public static string FormatLuid(int high, uint low)
        {
            return "0x" + high.ToString("X8", CultureInfo.InvariantCulture) +
                   "_0x" + low.ToString("X8", CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                Native.PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }
            _counters.Clear();
        }
    }
}
