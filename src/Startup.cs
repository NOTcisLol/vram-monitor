using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VramMonitor
{
    internal enum StartupScope
    {
        None = 0,
        CurrentUser = 1,
        AllUsers = 2
    }

    /// <summary>
    /// Atalho de inicialização automática. "Todos os usuários" grava em
    /// shell:common startup (precisa de administrador); "só para mim" grava em
    /// shell:startup, que não precisa de nada.
    ///
    /// O atalho é criado via IShellLink em vez de WScript.Shell de propósito: WSH pode
    /// estar desabilitado por política e aí o recurso morreria sem motivo.
    /// </summary>
    internal static class Startup
    {
        public const string LinkName = "VRAM Monitor.lnk";

        /// <summary>Inicia minimizado na área de notificações, não com a janela na cara.</summary>
        public const string LinkArgs = "--tray";

        public static string UserPath
        {
            get { return Combine(Environment.SpecialFolder.Startup); }
        }

        public static string CommonPath
        {
            get { return Combine(Environment.SpecialFolder.CommonStartup); }
        }

        private static string Combine(Environment.SpecialFolder folder)
        {
            try
            {
                string dir = Environment.GetFolderPath(folder);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, LinkName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Onde o atalho existe hoje. "Todos" tem precedência se ambos existirem.</summary>
        public static StartupScope Current
        {
            get
            {
                if (Exists(CommonPath)) return StartupScope.AllUsers;
                if (Exists(UserPath)) return StartupScope.CurrentUser;
                return StartupScope.None;
            }
        }

        private static bool Exists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
            catch (Exception) { return false; }
        }

        public static string PathFor(bool allUsers)
        {
            return allUsers ? CommonPath : UserPath;
        }

        /// <summary>Onde o binário fica quando promovido para o escopo de todos os usuários.</summary>
        public static string ProgramFilesTarget
        {
            get
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(Path.Combine(pf, "VRAM Monitor"), "VramMonitor.exe");
            }
        }

        /// <summary>
        /// Cria o atalho. Lança em caso de falha, com a mensagem do sistema.
        ///
        /// SEGURANÇA: no escopo de todos os usuários o atalho roda na sessão de QUALQUER um que
        /// fizer logon, inclusive administradores. Se ele apontasse para um .exe em pasta que o
        /// usuário comum pode escrever (Documents, Downloads, Área de Trabalho...), bastaria um
        /// malware sem elevação trocar o binário para ganhar execução na sessão dos outros — uma
        /// escalada de privilégio criada pelo nosso próprio recurso. Por isso, nesse escopo, o
        /// binário é promovido para Program Files antes de o atalho ser criado.
        /// </summary>
        public static string Install(bool allUsers)
        {
            string link = PathFor(allUsers);
            if (string.IsNullOrEmpty(link))
                throw new IOException("pasta de inicializacao nao encontrada");

            string dir = Path.GetDirectoryName(link);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;

            if (allUsers && !IsProtectedDirectory(Path.GetDirectoryName(exe)))
                exe = PromoteToProgramFiles(exe);

            CreateShortcut(link, exe, LinkArgs, Path.GetDirectoryName(exe),
                           AppInfo.Name + " " + AppInfo.Version);
            return exe;
        }

        /// <summary>Copia o executável para Program Files, que só administrador escreve.</summary>
        private static string PromoteToProgramFiles(string sourceExe)
        {
            string target = ProgramFilesTarget;
            string dir = Path.GetDirectoryName(target);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);   // herda a ACL do Program Files

            // Se já estamos rodando de lá, não há o que copiar.
            if (string.Equals(Path.GetFullPath(sourceExe), Path.GetFullPath(target),
                              StringComparison.OrdinalIgnoreCase))
                return target;

            File.Copy(sourceExe, target, true);

            if (!IsProtectedDirectory(dir))
                throw new UnauthorizedAccessException(
                    "o destino em Program Files continua gravavel por usuario comum");

            return target;
        }

        /// <summary>
        /// true quando só SYSTEM, Administradores, TrustedInstaller ou CREATOR OWNER têm
        /// permissão de escrita. Qualquer outra identidade com escrita torna o diretório
        /// inadequado para autostart de todos os usuários. Falha na leitura da ACL = não confiável.
        /// </summary>
        public static bool IsProtectedDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                System.Security.AccessControl.DirectorySecurity sec = Directory.GetAccessControl(dir);
                System.Security.AccessControl.AuthorizationRuleCollection rules =
                    sec.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));

                const System.Security.AccessControl.FileSystemRights WriteMask =
                    System.Security.AccessControl.FileSystemRights.WriteData |
                    System.Security.AccessControl.FileSystemRights.CreateFiles |
                    System.Security.AccessControl.FileSystemRights.CreateDirectories |
                    System.Security.AccessControl.FileSystemRights.Modify |
                    System.Security.AccessControl.FileSystemRights.FullControl |
                    System.Security.AccessControl.FileSystemRights.TakeOwnership |
                    System.Security.AccessControl.FileSystemRights.ChangePermissions;

                foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
                {
                    if (rule.AccessControlType != System.Security.AccessControl.AccessControlType.Allow)
                        continue;
                    if ((rule.FileSystemRights & WriteMask) == 0)
                        continue;

                    System.Security.Principal.SecurityIdentifier sid =
                        rule.IdentityReference as System.Security.Principal.SecurityIdentifier;
                    if (sid == null) return false;
                    if (!IsAdministrativeSid(sid)) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;   // não deu para verificar: trata como inseguro
            }
        }

        private static bool IsAdministrativeSid(System.Security.Principal.SecurityIdentifier sid)
        {
            string v = sid.Value;
            if (v == "S-1-5-18") return true;        // LOCAL SYSTEM
            if (v == "S-1-5-32-544") return true;    // BUILTIN\Administrators
            if (v == "S-1-3-0") return true;         // CREATOR OWNER
            if (v == "S-1-5-32-549") return true;    // Server Operators
            if (v.StartsWith("S-1-5-80-", StringComparison.Ordinal)) return true;   // serviços (TrustedInstaller)
            return false;
        }

        public static void Uninstall(bool allUsers)
        {
            string link = PathFor(allUsers);
            if (!Exists(link)) return;
            File.Delete(link);
        }

        /// <summary>Remove os dois, ignorando o que não der (o de todos exige admin).</summary>
        public static void UninstallUserSilently()
        {
            try { Uninstall(false); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ COM
        private static void CreateShortcut(string linkPath, string target, string args,
                                           string workDir, string description)
        {
            IShellLinkW link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                link.SetPath(target);
                link.SetArguments(args ?? "");
                link.SetWorkingDirectory(workDir ?? "");
                link.SetDescription(description ?? "");
                link.SetIconLocation(target, 0);
                link.SetShowCmd(SW_SHOWMINNOACTIVE);

                IPersistFile file = (IPersistFile)link;
                file.Save(linkPath, true);
            }
            finally
            {
                try { Marshal.ReleaseComObject(link); }
                catch (Exception) { }
            }
        }

        private const int SW_SHOWMINNOACTIVE = 7;

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                         int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                                 int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
                      [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile(out IntPtr ppszFileName);
        }
    }
}
