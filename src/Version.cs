using System.Reflection;
using System.Runtime.InteropServices;

// Metadados do executável. AppInfo.Version é a FONTE ÚNICA da versão: as constantes abaixo
// derivam dela, e release.ps1 lê este arquivo para nomear a tag e o release.
[assembly: AssemblyTitle("Monitor de VRAM")]
[assembly: AssemblyDescription("Memória de GPU por processo, com encerramento seguro e ponte headless em JSON")]
[assembly: AssemblyProduct("VramMonitor")]
[assembly: AssemblyCompany("NOTcisLol")]
[assembly: AssemblyCopyright("github.com/NOTcisLol/vram-monitor")]
[assembly: AssemblyVersion(VramMonitor.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(VramMonitor.AppInfo.Version + ".0")]
[assembly: AssemblyInformationalVersion(VramMonitor.AppInfo.Version)]
[assembly: ComVisible(false)]

namespace VramMonitor
{
    internal static class AppInfo
    {
        /// <summary>
        /// Versão do aplicativo (SemVer: MAIOR.MENOR.CORREÇÃO).
        /// Ao mudar aqui, atualize também o CHANGELOG.md — release.ps1 exige a seção.
        /// </summary>
        public const string Version = "1.1.0";

        /// <summary>Nome do produto — marca, não traduzido (o subtítulo é que é localizado).</summary>
        public const string Name = "VRAM Monitor";
        public const string Repo = "https://github.com/NOTcisLol/vram-monitor";
        public const string DonateUrl = "https://link.mercadopago.com.br/donatedev";

        /// <summary>"Monitor de VRAM v1.0.0"</summary>
        public static string NameWithVersion
        {
            get { return Name + " v" + Version; }
        }
    }
}
