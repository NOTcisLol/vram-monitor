using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace VramMonitor
{
    /// <summary>
    /// Preferências do usuário em %LOCALAPPDATA%\VramMonitor\settings.json.
    /// Falha na leitura ou na escrita nunca é fatal: cai nos padrões e segue.
    /// </summary>
    internal sealed class Settings
    {
        /// <summary>"" = padrão do app (inglês); "auto" = idioma do Windows; ou um código.</summary>
        public string Language = "";
        public int IntervalMs = 1000;
        public bool ExportJson = true;
        public bool OnlyGpu = true;

        public static string Path_
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VramMonitor");
                return Path.Combine(dir, "settings.json");
            }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string path = Path_;
                if (!File.Exists(path)) return s;
                Dictionary<string, string> map = JsonReader.Flatten(File.ReadAllText(path));

                string v;
                if (map.TryGetValue("language", out v)) s.Language = v;
                if (map.TryGetValue("intervalMs", out v))
                {
                    int n;
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) &&
                        n >= 250 && n <= 60000)
                        s.IntervalMs = n;
                }
                if (map.TryGetValue("exportJson", out v)) s.ExportJson = IsTrue(v);
                if (map.TryGetValue("onlyGpu", out v)) s.OnlyGpu = IsTrue(v);
            }
            catch (Exception)
            {
                return new Settings();
            }
            return s;
        }

        private static bool IsTrue(string v)
        {
            return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
        }

        public void Save()
        {
            try
            {
                Json j = new Json(true);
                j.Obj();
                j.Str("language", Language);
                j.Num("intervalMs", IntervalMs);
                j.Bool("exportJson", ExportJson);
                j.Bool("onlyGpu", OnlyGpu);
                j.EndObj();

                string path = Path_;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, j.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch (Exception)
            {
                // preferência não salva não justifica derrubar o app
            }
        }
    }
}
