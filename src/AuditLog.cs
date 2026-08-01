using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace VramMonitor
{
    /// <summary>
    /// Registro de toda tentativa de encerrar processo, em %LOCALAPPDATA%\VramMonitor\kills.log.
    /// Não impede nada — serve para você conseguir responder depois "quem matou o quê e quando",
    /// que é a pergunta que aparece quando algo encerra sozinho.
    /// </summary>
    internal static class AuditLog
    {
        private const long MaxBytes = 1024 * 1024;   // 1 MB, com uma rotação

        public static string Path_
        {
            get
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VramMonitor");
                return System.IO.Path.Combine(dir, "kills.log");
            }
        }

        public static void KillAttempt(int pid, ProcInfo pi, string origin, KillResult result)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.Append("  origem=").Append(origin);
                sb.Append("  pid=").Append(pid.ToString(CultureInfo.InvariantCulture));
                sb.Append("  nome=").Append(Clean(pi != null ? pi.Name : "?"));
                sb.Append("  risco=").Append(pi != null ? SnapshotJson.RiskCode(pi.Risk) : "unknown");
                sb.Append("  elevado=").Append(ProcessCatalog.IsCurrentProcessElevated() ? "sim" : "nao");
                sb.Append("  resultado=").Append(result != null ? result.Outcome.ToString() : "?");
                if (result != null && result.Message.Length > 0)
                    sb.Append("  detalhe=").Append(Clean(result.Message));
                if (pi != null && pi.ExePath.Length > 0)
                    sb.Append("  caminho=").Append(Clean(pi.ExePath));

                Write(sb.ToString());
            }
            catch (Exception)
            {
                // auditoria nunca pode derrubar a ação principal
            }
        }

        public static void Note(string origin, string message)
        {
            try
            {
                Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                      "  origem=" + origin + "  " + Clean(message));
            }
            catch (Exception) { }
        }

        private static void Write(string line)
        {
            string path = Path_;
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Copy(path, path + ".1", true);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Delete(path);
            }
            catch (Exception) { }

            using (StreamWriter w = new StreamWriter(path, true, new UTF8Encoding(false)))
                w.WriteLine(line);
        }

        /// <summary>
        /// Nome e caminho de processo são strings controladas por terceiros: sem limpeza, dá para
        /// forjar linhas falsas no log ou injetar sequências de escape que enganam o terminal.
        /// </summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length && sb.Length < 400; i++)
            {
                char c = s[i];
                sb.Append(c < 0x20 || c == 0x7F ? ' ' : c);
            }
            return sb.ToString();
        }
    }
}
