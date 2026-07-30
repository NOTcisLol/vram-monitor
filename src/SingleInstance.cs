using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace VramMonitor
{
    /// <summary>
    /// Trava de instância única para o modo janela (os modos headless não usam).
    /// Também sabe pedir para a instância existente aparecer, em vez de abrir uma segunda.
    /// </summary>
    internal static class SingleInstance
    {
        private const string MutexName = "Local\\VramMonitor.SingleInstance.v1";
        private const int HWND_BROADCAST = 0xFFFF;

        private static Mutex _mutex; // estático de propósito: o GC não pode liberar a trava

        /// <summary>true quando a instância existente roda elevada e não pode ser sinalizada.</summary>
        public static bool ExistingIsElevated { get; private set; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static uint _showMsg;

        /// <summary>Mensagem registrada que pede à instância existente para se mostrar.</summary>
        public static uint ShowMsg
        {
            get
            {
                if (_showMsg == 0)
                {
                    try { _showMsg = RegisterWindowMessageW("VramMonitor.ShowWindow.v1"); }
                    catch (Exception) { _showMsg = 0; }
                }
                return _showMsg;
            }
        }

        /// <summary>true se esta é a única instância (e passamos a segurar a trava).</summary>
        public static bool TryAcquire()
        {
            ExistingIsElevated = false;
            try
            {
                bool createdNew;
                Mutex m = new Mutex(true, MutexName, out createdNew);
                if (createdNew)
                {
                    _mutex = m;
                    return true;
                }
                m.Close();
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // O mutex existe mas foi criado num nível de integridade mais alto:
                // há uma instância elevada rodando.
                ExistingIsElevated = true;
                return false;
            }
            catch (Exception)
            {
                // Sem a trava, o pior caso é permitir duas janelas; melhor do que não abrir.
                return true;
            }
        }

        /// <summary>Solta a trava antes de entregar o lugar para uma nova instância (elevação).</summary>
        public static void Release()
        {
            if (_mutex == null) return;
            try { _mutex.ReleaseMutex(); }
            catch (Exception) { }
            try { _mutex.Close(); }
            catch (Exception) { }
            _mutex = null;
        }

        /// <summary>Pede para a instância que já roda aparecer.</summary>
        public static void SignalExisting()
        {
            uint msg = ShowMsg;
            if (msg != 0)
            {
                try { PostMessage(new IntPtr(HWND_BROADCAST), msg, IntPtr.Zero, IntPtr.Zero); }
                catch (Exception) { }
            }

            // Reforço para o caso da janela já estar visível: traz para a frente.
            try
            {
                Process self = Process.GetCurrentProcess();
                Process[] others = Process.GetProcessesByName(self.ProcessName);
                for (int i = 0; i < others.Length; i++)
                {
                    try
                    {
                        if (others[i].Id != self.Id && others[i].MainWindowHandle != IntPtr.Zero)
                            SetForegroundWindow(others[i].MainWindowHandle);
                    }
                    catch (Exception) { }
                    finally { others[i].Dispose(); }
                }
                self.Dispose();
            }
            catch (Exception) { }
        }
    }
}
