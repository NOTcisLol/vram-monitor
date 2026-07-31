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

        /// <summary>Cria o atalho. Lança em caso de falha, com a mensagem do sistema.</summary>
        public static void Install(bool allUsers)
        {
            string link = PathFor(allUsers);
            if (string.IsNullOrEmpty(link))
                throw new IOException("pasta de inicializacao nao encontrada");

            string dir = Path.GetDirectoryName(link);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            CreateShortcut(link, exe, LinkArgs, Path.GetDirectoryName(exe),
                           AppInfo.Name + " " + AppInfo.Version);
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
