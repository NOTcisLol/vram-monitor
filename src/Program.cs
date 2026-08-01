using System;
using System.Threading;
using System.Windows.Forms;

namespace VramMonitor
{
    internal static class Program
    {
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint DirectoryFlags);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet =
            System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectoryW(string lpPathName);

        /// <summary>
        /// SEGURANÇA: o app resolve pdh.dll, dxgi.dll e uxtheme.dll por nome. Na ordem de busca
        /// padrão, o diretório do executável vem antes de System32 — e como o .exe costuma ficar
        /// numa pasta gravável pelo usuário, bastaria plantar um "pdh.dll" ao lado dele para
        /// executar código dentro deste processo (pior ainda quando ele roda elevado).
        /// Restringir a busca a System32 fecha isso.
        /// </summary>
        private static void HardenDllSearchPath()
        {
            try
            {
                if (!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32))
                    SetDllDirectoryW(string.Empty);   // Windows antigo sem a API acima
            }
            catch (EntryPointNotFoundException)
            {
                try { SetDllDirectoryW(string.Empty); }
                catch (Exception) { }
            }
            catch (Exception) { }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            HardenDllSearchPath();

            string mode;
            if (args != null && args.Length > 0 && Cli.IsCliMode(args, out mode))
            {
                try
                {
                    return Cli.Run(args, mode);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.ToString());
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Uma janela só: a segunda invocação apenas traz a existente para a frente.
            if (!SingleInstance.TryAcquire())
            {
                SingleInstance.SignalExisting();
                if (SingleInstance.ExistingIsElevated)
                {
                    Settings st = Settings.Load();
                    I18n.Init(I18n.Resolve(st.Language));
                    MessageBox.Show(I18n.T("dialog.alreadyRunningElevated"), AppInfo.Name,
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Report(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Report(e.ExceptionObject as Exception);
            };

            try
            {
                MainForm form = new MainForm();
                bool startHidden = args != null && HasFlag(args, "tray");
                if (startHidden)
                {
                    form.WindowState = FormWindowState.Minimized;
                    form.ShowInTaskbar = false;
                }
                Application.Run(form);
            }
            catch (Exception ex)
            {
                Report(ex);
                return 1;
            }
            return 0;
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i].TrimStart('-', '/'), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static void Report(Exception ex)
        {
            string msg = ex == null ? "Erro desconhecido." : ex.ToString();
            MessageBox.Show(msg, AppInfo.Name + " — " + I18n.T("dialog.errorTitle"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
