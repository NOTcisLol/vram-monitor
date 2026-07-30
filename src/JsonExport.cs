using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VramMonitor
{
    /// <summary>Escritor JSON mínimo (saída sempre ASCII, non-ASCII vira \uXXXX).</summary>
    internal sealed class Json
    {
        private readonly StringBuilder _sb = new StringBuilder(1 << 16);
        private readonly Stack<bool> _first = new Stack<bool>();
        private readonly bool _pretty;

        public Json(bool pretty)
        {
            _pretty = pretty;
            _first.Push(true);
        }

        private void Sep()
        {
            bool first = _first.Pop();
            if (!first) _sb.Append(',');
            _first.Push(false);
            if (_pretty && _first.Count > 1)
            {
                _sb.Append('\n');
                _sb.Append(' ', (_first.Count - 1) * 2);
            }
        }

        private void Close(char c)
        {
            bool empty = _first.Pop();
            if (_pretty && !empty)
            {
                _sb.Append('\n');
                _sb.Append(' ', (_first.Count - 1) * 2);
            }
            _sb.Append(c);
        }

        public Json Obj() { Sep(); _sb.Append('{'); _first.Push(true); return this; }
        public Json Obj(string name) { Sep(); Key(name); _sb.Append('{'); _first.Push(true); return this; }
        public Json EndObj() { Close('}'); return this; }
        public Json Arr(string name) { Sep(); Key(name); _sb.Append('['); _first.Push(true); return this; }
        public Json EndArr() { Close(']'); return this; }

        private void Key(string name)
        {
            Escape(name);
            _sb.Append(':');
            if (_pretty) _sb.Append(' ');
        }

        public Json Num(string name, long v)
        {
            Sep(); Key(name);
            _sb.Append(v.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public Json Num(string name, double v, int decimals)
        {
            Sep(); Key(name);
            double r = Math.Round(v, decimals);
            _sb.Append(r.ToString("0.0###", CultureInfo.InvariantCulture));
            return this;
        }

        public Json Str(string name, string v)
        {
            Sep(); Key(name);
            if (v == null) _sb.Append("null");
            else Escape(v);
            return this;
        }

        public Json Str(string v)
        {
            Sep();
            Escape(v ?? "");
            return this;
        }

        public Json Bool(string name, bool v)
        {
            Sep(); Key(name);
            _sb.Append(v ? "true" : "false");
            return this;
        }

        public Json Bool(string name, bool? v)
        {
            Sep(); Key(name);
            _sb.Append(v.HasValue ? (v.Value ? "true" : "false") : "null");
            return this;
        }

        private void Escape(string s)
        {
            _sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7E)
                            _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }

        public override string ToString()
        {
            return _sb.ToString();
        }
    }

    /// <summary>
    /// Leitor JSON mínimo, escrito à mão de propósito: o app não pode depender de
    /// System.Web.Extensions nem de qualquer assembly que possa faltar na máquina do usuário.
    /// Achata o documento em chaves pontuadas ("toolbar.pause", "risk.critical").
    /// </summary>
    internal static class JsonReader
    {
        public static Dictionary<string, string> Flatten(string text)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (text == null) return map;
            int i = 0;
            SkipWs(text, ref i);
            if (i < text.Length && text[i] == '﻿') i++;   // BOM
            SkipWs(text, ref i);
            Expect(text, ref i, '{');
            ReadObject(text, ref i, "", map);
            return map;
        }

        private static void ReadObject(string s, ref int i, string prefix,
                                       Dictionary<string, string> map)
        {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return; }

            while (true)
            {
                SkipWs(s, ref i);
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                Expect(s, ref i, ':');
                SkipWs(s, ref i);
                ReadValue(s, ref i, prefix.Length == 0 ? key : prefix + "." + key, map);
                SkipWs(s, ref i);
                if (i >= s.Length) Fail("fim inesperado do arquivo", i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return; }
                Fail("esperado ',' ou '}'", i);
            }
        }

        private static void ReadValue(string s, ref int i, string path,
                                      Dictionary<string, string> map)
        {
            if (i >= s.Length) Fail("valor ausente", i);
            char c = s[i];
            if (c == '{')
            {
                i++;
                ReadObject(s, ref i, path, map);
                return;
            }
            if (c == '[')
            {
                i++;
                int n = 0;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return; }
                while (true)
                {
                    SkipWs(s, ref i);
                    ReadValue(s, ref i, path + "." + n.ToString(CultureInfo.InvariantCulture), map);
                    n++;
                    SkipWs(s, ref i);
                    if (i >= s.Length) Fail("fim inesperado dentro de array", i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return; }
                    Fail("esperado ',' ou ']'", i);
                }
            }
            if (c == '"')
            {
                map[path] = ReadString(s, ref i);
                return;
            }
            if (Match(s, ref i, "true")) { map[path] = "true"; return; }
            if (Match(s, ref i, "false")) { map[path] = "false"; return; }
            if (Match(s, ref i, "null")) { map[path] = ""; return; }

            int start = i;
            while (i < s.Length && "+-.eE0123456789".IndexOf(s[i]) >= 0) i++;
            if (i == start) Fail("valor invalido", i);
            map[path] = s.Substring(start, i - start);
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) Fail("string sem fechamento", i);
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) Fail("escape incompleto", i);
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) Fail("escape \\u incompleto", i);
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber,
                                                 CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: Fail("escape desconhecido \\" + e, i); break;
                }
            }
        }

        private static bool Match(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length) return false;
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                // comentários de linha: tolerados para quem edita traduções à mão
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                break;
            }
        }

        private static void Expect(string s, ref int i, char c)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != c)
                Fail("esperado '" + c + "'", i);
            i++;
        }

        private static void Fail(string msg, int pos)
        {
            throw new FormatException("JSON invalido na posicao " + pos.ToString(CultureInfo.InvariantCulture) +
                                     ": " + msg);
        }
    }

    /// <summary>
    /// Serializa um snapshot completo (adaptadores + processos + blocos) para JSON.
    /// É a "ponte headless": o mesmo formato sai no stdout da CLI e no arquivo exportado.
    /// </summary>
    internal static class SnapshotJson
    {
        public const int Schema = 1;

        public static string DefaultPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VramMonitor");
                return Path.Combine(dir, "snapshot.json");
            }
        }

        public static string RiskCode(RiskLevel r)
        {
            switch (r)
            {
                case RiskLevel.Critical: return "critical";
                case RiskLevel.System: return "system";
                case RiskLevel.Elevated: return "elevated";
                default: return "user";
            }
        }

        private const double MB = 1024.0 * 1024.0;

        public static string Build(GpuSnapshot snap, ProcessCatalog catalog, string source,
                                  bool pretty, int topN, long minBytes)
        {
            Json j = new Json(pretty);
            j.Obj();
            j.Num("schema", Schema);
            j.Str("appVersion", AppInfo.Version);
            j.Str("timestamp", DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
            j.Str("source", source);
            j.Str("host", Environment.MachineName);
            j.Bool("monitorElevated", ProcessCatalog.IsCurrentProcessElevated());
            if (snap.Warning != null) j.Str("warning", snap.Warning);

            // -------- adaptadores
            long tDed = 0, tShr = 0, tDedTotal = 0, tShrTotal = 0;
            j.Arr("adapters");
            for (int i = 0; i < snap.Adapters.Count; i++)
            {
                GpuAdapter a = snap.Adapters[i];
                if (a.DedicatedTotal <= 0 && a.DedicatedUsed <= 0 && a.SharedUsed <= 0) continue;
                tDed += a.DedicatedUsed;
                tShr += a.SharedUsed;
                tDedTotal += a.DedicatedTotal;
                tShrTotal += a.SharedTotal;

                j.Obj();
                j.Str("luid", a.LuidKey);
                j.Str("name", a.Label);
                j.Bool("software", a.IsSoftware);
                j.Num("dedicatedTotalBytes", a.DedicatedTotal);
                j.Num("dedicatedUsedBytes", a.DedicatedUsed);
                j.Num("dedicatedUsedMB", a.DedicatedUsed / MB, 1);
                j.Num("dedicatedPercent", a.DedicatedTotal > 0
                    ? 100.0 * a.DedicatedUsed / a.DedicatedTotal : 0.0, 1);
                j.Num("sharedTotalBytes", a.SharedTotal);
                j.Num("sharedUsedBytes", a.SharedUsed);
                j.Num("sharedUsedMB", a.SharedUsed / MB, 1);
                j.Num("sharedPercent", a.SharedTotal > 0
                    ? 100.0 * a.SharedUsed / a.SharedTotal : 0.0, 1);
                j.Num("gpuMemoryTotalBytes", a.DedicatedTotal + a.SharedTotal);
                j.Num("gpuMemoryUsedBytes", a.DedicatedUsed + a.SharedUsed);
                j.EndObj();
            }
            j.EndArr();

            // -------- processos
            List<GpuProcess> procs = new List<GpuProcess>(snap.Processes);
            procs.Sort(delegate(GpuProcess a, GpuProcess b)
            {
                int c = b.Local.CompareTo(a.Local);
                if (c != 0) return c;
                c = b.TotalResident.CompareTo(a.TotalResident);
                if (c != 0) return c;
                return a.Pid.CompareTo(b.Pid);
            });

            long sumLocal = 0, sumNonLocal = 0, sumCommitted = 0;
            int emitted = 0;
            j.Arr("processes");
            for (int i = 0; i < procs.Count; i++)
            {
                GpuProcess p = procs[i];
                sumLocal += p.Local;
                sumNonLocal += p.NonLocal;
                sumCommitted += p.Committed;

                if (minBytes > 0 && p.TotalResident < minBytes) continue;
                if (topN > 0 && emitted >= topN) continue;
                emitted++;

                ProcInfo pi = catalog != null ? catalog.Get(p.Pid) : null;

                j.Obj();
                j.Num("pid", p.Pid);
                j.Str("name", pi != null ? pi.Name : "");
                j.Num("dedicatedBytes", p.Local);
                j.Num("dedicatedMB", p.Local / MB, 1);
                j.Num("sharedBytes", p.NonLocal);
                j.Num("sharedMB", p.NonLocal / MB, 1);
                j.Num("totalGpuBytes", p.TotalResident);
                j.Num("totalGpuMB", p.TotalResident / MB, 1);
                j.Num("committedBytes", p.Committed);
                j.Num("dedicatedCommittedBytes", p.Dedicated);
                j.Num("gpuPercent", p.EnginePercent, 1);
                j.Str("topEngine", p.TopEngine);

                if (pi != null)
                {
                    j.Str("risk", RiskCode(pi.Risk));
                    j.Str("riskLabel", pi.RiskText);
                    j.Bool("killBlocked", pi.Risk == RiskLevel.Critical);
                    j.Bool("elevated", pi.Elevated);
                    j.Bool("critical", pi.Critical);
                    j.Num("session", pi.SessionId);
                    j.Str("user", pi.User);
                    j.Str("userSid", pi.UserSid);
                    j.Str("path", pi.ExePath);
                    j.Str("description", pi.FileDescription);
                    j.Str("company", pi.Company);
                    j.Str("killCommand", "taskkill /F /PID " + p.Pid.ToString(CultureInfo.InvariantCulture));
                    j.Arr("services");
                    for (int k = 0; k < pi.Services.Count; k++) j.Str(pi.Services[k]);
                    j.EndArr();
                }
                else
                {
                    j.Str("risk", "unknown");
                }

                j.Arr("engines");
                foreach (KeyValuePair<string, double> kv in p.Engines)
                {
                    if (kv.Value <= 0.05) continue;
                    j.Obj();
                    j.Str("engine", kv.Key);
                    j.Num("percent", kv.Value, 1);
                    j.EndObj();
                }
                j.EndArr();

                j.Arr("blocks");
                for (int k = 0; k < p.Segments.Count; k++)
                {
                    GpuSegment s = p.Segments[k];
                    j.Obj();
                    j.Str("luid", s.LuidKey);
                    j.Num("segment", s.PhysIndex);
                    j.Num("dedicatedBytes", s.Local);
                    j.Num("sharedBytes", s.NonLocal);
                    j.Num("dedicatedCommittedBytes", s.Dedicated);
                    j.Num("committedBytes", s.Committed);
                    j.EndObj();
                }
                j.EndArr();

                j.EndObj();
            }
            j.EndArr();

            j.Obj("totals");
            j.Num("processCount", procs.Count);
            j.Num("processesEmitted", emitted);
            j.Num("dedicatedBytes", sumLocal);
            j.Num("dedicatedMB", sumLocal / MB, 1);
            j.Num("sharedBytes", sumNonLocal);
            j.Num("sharedMB", sumNonLocal / MB, 1);
            j.Num("totalGpuBytes", sumLocal + sumNonLocal);
            j.Num("committedBytes", sumCommitted);
            j.Num("adapterDedicatedUsedBytes", tDed);
            j.Num("adapterDedicatedTotalBytes", tDedTotal);
            j.Num("adapterSharedUsedBytes", tShr);
            j.Num("adapterSharedTotalBytes", tShrTotal);
            j.Num("adapterDedicatedPercent", tDedTotal > 0 ? 100.0 * tDed / tDedTotal : 0.0, 1);
            j.EndObj();

            j.EndObj();
            return j.ToString();
        }

        /// <summary>Grava o arquivo da ponte de forma que o leitor nunca veja conteúdo parcial.</summary>
        public static void WriteFile(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                    return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                try { File.Delete(path); }
                catch (Exception) { }
            }
            try { File.Move(tmp, path); }
            catch (Exception)
            {
                try { File.Copy(tmp, path, true); File.Delete(tmp); }
                catch (Exception) { }
            }
        }

        public static void AppendLine(string path, string compactJson)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            using (StreamWriter w = new StreamWriter(path, true, new UTF8Encoding(false)))
                w.WriteLine(compactJson);
        }
    }
}
