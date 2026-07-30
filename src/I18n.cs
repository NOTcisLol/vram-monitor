using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace VramMonitor
{
    internal sealed class Language
    {
        public string Code = "";        // "en-US"
        public string Name = "";        // nome em inglês, para listas
        public string NativeName = "";  // nome no próprio idioma (o que aparece no seletor)
        public string CultureName = ""; // cultura de formatação de número/data
        public string Source = "";      // "" = embutido no executável; senão o caminho do arquivo
        public Dictionary<string, string> Strings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Label
        {
            get { return NativeName.Length > 0 ? NativeName : (Name.Length > 0 ? Name : Code); }
        }

        public bool IsExternal
        {
            get { return Source.Length > 0; }
        }
    }

    /// <summary>
    /// Idiomas da interface. Os arquivos de en-US, pt-BR, es-ES, fr-FR e de-DE são embutidos no
    /// executável (o download continua sendo um arquivo único), e qualquer JSON solto em
    /// "lang\" ao lado do .exe — ou em %LOCALAPPDATA%\VramMonitor\lang — é carregado também,
    /// podendo acrescentar um idioma novo ou sobrescrever um embutido sem recompilar.
    /// </summary>
    internal static class I18n
    {
        public const string DefaultCode = "en-US";
        private const string ResourcePrefix = "VramMonitor.lang.";

        private static readonly List<Language> _all = new List<Language>();
        private static Language _current;
        private static Language _fallback;
        private static readonly List<string> _loadErrors = new List<string>();

        public static List<Language> Available { get { return _all; } }
        public static List<string> LoadErrors { get { return _loadErrors; } }

        public static Language Current
        {
            get
            {
                if (_current == null) Init(null);
                return _current;
            }
        }

        public static string CurrentCode
        {
            get { return Current.Code; }
        }

        /// <summary>Carrega todos os idiomas e seleciona <paramref name="preferred"/> (ou inglês).</summary>
        public static void Init(string preferred)
        {
            if (_all.Count == 0)
            {
                LoadEmbedded();
                LoadExternal(ExeLangDir());
                LoadExternal(UserLangDir());
                _all.Sort(delegate(Language a, Language b)
                {
                    if (string.Equals(a.Code, DefaultCode, StringComparison.OrdinalIgnoreCase)) return -1;
                    if (string.Equals(b.Code, DefaultCode, StringComparison.OrdinalIgnoreCase)) return 1;
                    return string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
                });
                _fallback = Find(DefaultCode) ?? (_all.Count > 0 ? _all[0] : Empty());
            }
            if (!Select(preferred)) Select(DefaultCode);
        }

        private static Language Empty()
        {
            Language l = new Language();
            l.Code = DefaultCode;
            l.NativeName = "English";
            l.CultureName = "en-US";
            return l;
        }

        public static bool Select(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            Language lang = Find(code);
            if (lang == null) return false;
            _current = lang;
            ApplyCulture(lang);
            return true;
        }

        /// <summary>
        /// A cultura vem do idioma escolhido: sem isso a interface em inglês mostraria
        /// "5,64 GB" com vírgula decimal em uma máquina configurada em português.
        /// </summary>
        private static void ApplyCulture(Language lang)
        {
            string name = lang.CultureName.Length > 0 ? lang.CultureName : lang.Code;
            try
            {
                CultureInfo ci = CultureInfo.GetCultureInfo(name);
                Thread.CurrentThread.CurrentCulture = ci;
                // Mantém as threads criadas depois (ThreadPool do scan de serviços) coerentes.
                CultureInfo.DefaultThreadCurrentCulture = ci;
            }
            catch (CultureNotFoundException) { }
            catch (Exception) { }
        }

        public static Language Find(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            for (int i = 0; i < _all.Count; i++)
                if (string.Equals(_all[i].Code, code, StringComparison.OrdinalIgnoreCase))
                    return _all[i];
            // "pt" casa com "pt-BR"
            for (int i = 0; i < _all.Count; i++)
            {
                string two = _all[i].Code.Length >= 2 ? _all[i].Code.Substring(0, 2) : _all[i].Code;
                string want = code.Length >= 2 ? code.Substring(0, 2) : code;
                if (string.Equals(two, want, StringComparison.OrdinalIgnoreCase))
                    return _all[i];
            }
            return null;
        }

        /// <summary>Valor de Settings.Language que significa "seguir o idioma do Windows".</summary>
        public const string AutoValue = "auto";

        /// <summary>
        /// Traduz a preferência salva em um código concreto.
        /// Vazio (primeira execução) = inglês, que é o padrão do aplicativo;
        /// "auto" = idioma do Windows; qualquer outra coisa = escolha explícita.
        /// </summary>
        public static string Resolve(string setting)
        {
            if (string.IsNullOrEmpty(setting)) return DefaultCode;
            if (string.Equals(setting, AutoValue, StringComparison.OrdinalIgnoreCase)) return SystemCode;
            return setting;
        }

        /// <summary>Código sugerido a partir do idioma do Windows (usado só no modo automático).</summary>
        public static string SystemCode
        {
            get
            {
                try { return CultureInfo.InstalledUICulture.Name; }
                catch (Exception) { return DefaultCode; }
            }
        }

        // ------------------------------------------------------------------ texto
        public static string T(string key)
        {
            string v;
            if (_current == null) Init(null);
            if (_current != null && _current.Strings.TryGetValue(key, out v) && v.Length > 0) return v;
            if (_fallback != null && _fallback.Strings.TryGetValue(key, out v)) return v;
            return "«" + key + "»";   // chave faltando fica visível em vez de virar texto vazio
        }

        /// <summary>
        /// Junta "key.0", "key.1", ... com quebras de linha. Textos longos (a ajuda) ficam como
        /// array no JSON para quem traduz enxergar linha por linha em vez de um "\n" gigante.
        /// </summary>
        public static string Joined(string key, params object[] args)
        {
            if (_current == null) Init(null);
            Dictionary<string, string> dict = null;
            if (_current != null && _current.Strings.ContainsKey(key + ".0")) dict = _current.Strings;
            else if (_fallback != null && _fallback.Strings.ContainsKey(key + ".0")) dict = _fallback.Strings;
            if (dict == null) return T(key);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; ; i++)
            {
                string v;
                if (!dict.TryGetValue(key + "." + i.ToString(CultureInfo.InvariantCulture), out v)) break;
                if (i > 0) sb.Append("\r\n");
                sb.Append(v);
            }
            string text = sb.ToString();
            if (args == null || args.Length == 0) return text;
            try { return string.Format(CultureInfo.CurrentCulture, text, args); }
            catch (FormatException) { return text; }
        }

        public static string F(string key, params object[] args)
        {
            string fmt = T(key);
            if (args == null || args.Length == 0) return fmt;
            try { return string.Format(CultureInfo.CurrentCulture, fmt, args); }
            catch (FormatException) { return fmt; }
        }

        // ----------------------------------------------------------------- carga
        private static void LoadEmbedded()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (!names[i].StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!names[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using (Stream st = asm.GetManifestResourceStream(names[i]))
                        {
                            if (st == null) continue;
                            using (StreamReader rd = new StreamReader(st, new UTF8Encoding(false), true))
                                Add(Parse(rd.ReadToEnd(), ""), names[i]);
                        }
                    }
                    catch (Exception ex)
                    {
                        _loadErrors.Add(names[i] + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _loadErrors.Add("recursos embutidos: " + ex.Message);
            }
        }

        private static void LoadExternal(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                if (!Directory.Exists(dir)) return;
                string[] files = Directory.GetFiles(dir, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        Add(Parse(File.ReadAllText(files[i]), files[i]), files[i]);
                    }
                    catch (Exception ex)
                    {
                        _loadErrors.Add(Path.GetFileName(files[i]) + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _loadErrors.Add(dir + ": " + ex.Message);
            }
        }

        private static Language Parse(string json, string source)
        {
            Dictionary<string, string> map = JsonReader.Flatten(json);
            Language lang = new Language();
            lang.Source = source ?? "";
            lang.Code = Get(map, "meta.code");
            lang.Name = Get(map, "meta.name");
            lang.NativeName = Get(map, "meta.nativeName");
            lang.CultureName = Get(map, "meta.culture");
            if (lang.Code.Length == 0)
                throw new FormatException("falta \"meta\": { \"code\": ... }");
            lang.Strings = map;
            return lang;
        }

        private static string Get(Dictionary<string, string> map, string key)
        {
            string v;
            return map.TryGetValue(key, out v) ? v : "";
        }

        /// <summary>Arquivo externo com o mesmo código substitui o embutido.</summary>
        private static void Add(Language lang, string origin)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i].Code, lang.Code, StringComparison.OrdinalIgnoreCase))
                {
                    if (lang.IsExternal) _all[i] = lang;
                    return;
                }
            }
            _all.Add(lang);
        }

        public static string ExeLangDir()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exe)) return null;
                return Path.Combine(Path.GetDirectoryName(exe), "lang");
            }
            catch (Exception) { return null; }
        }

        public static string UserLangDir()
        {
            try
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                 "VramMonitor"),
                    "lang");
            }
            catch (Exception) { return null; }
        }
    }
}
