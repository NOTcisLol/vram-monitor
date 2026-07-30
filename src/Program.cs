using System;
using System.Threading;
using System.Windows.Forms;

namespace VramMonitor
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
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
