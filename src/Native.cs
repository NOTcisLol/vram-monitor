using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VramMonitor
{
    /// <summary>
    /// P/Invoke para PDH (contadores de GPU), APIs de processo/token e DXGI.
    /// Compilado com csc do .NET Framework -> manter sintaxe C# 5.
    /// </summary>
    internal static class Native
    {
        // ---------------------------------------------------------------- PDH
        public const uint PDH_CSTATUS_VALID_DATA = 0x00000000;
        public const uint PDH_CSTATUS_NEW_DATA = 0x00000001;
        public const uint PDH_MORE_DATA = 0x800007D2;
        public const uint PDH_FMT_LARGE = 0x00000400;
        public const uint PDH_FMT_DOUBLE = 0x00000200;
        public const uint PDH_FMT_NOCAP100 = 0x00008000;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhOpenQueryW(string szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath,
                                                        IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")]
        public static extern uint PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhGetFormattedCounterArrayW(IntPtr hCounter, uint dwFormat,
                                                               ref uint lpdwBufferSize,
                                                               out uint lpdwItemCount,
                                                               IntPtr ItemBuffer);

        [DllImport("pdh.dll")]
        public static extern uint PdhCloseQuery(IntPtr hQuery);

        /// <summary>
        /// PDH_FMT_COUNTERVALUE_ITEM_W. Layout validado em x64 e x86:
        /// szName @0, CStatus @8, valor (uniao de 8 bytes) @16, tamanho total 24.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct PdhItem
        {
            public IntPtr szName;
            public uint CStatus;
            public uint Padding;
            public long Value; // largeValue; para PDH_FMT_DOUBLE reinterpretar os bits
        }

        // ------------------------------------------------------------ Processo
        public const uint PROCESS_TERMINATE = 0x0001;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint TOKEN_QUERY = 0x0008;

        public const int TokenUser = 1;
        public const int TokenElevation = 20;

        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_INVALID_PARAMETER = 87;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags,
                                                             StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessTimes(IntPtr hProcess, out long lpCreationTime, out long lpExitTime,
                                                  out long lpKernelTime, out long lpUserTime);

        /// <summary>Win8.1+. Processo critico: matar causa bugcheck (tela azul).</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool IsProcessCritical(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool Critical);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
                                                      IntPtr TokenInformation, int TokenInformationLength,
                                                      out int ReturnLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct SID_AND_ATTRIBUTES
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        // ---------------------------------------------------------------- DXGI
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public int Revision;
            public IntPtr DedicatedVideoMemory;  // SIZE_T
            public IntPtr DedicatedSystemMemory; // SIZE_T
            public IntPtr SharedSystemMemory;    // SIZE_T
            public uint LuidLowPart;
            public int LuidHighPart;
            public uint Flags;
        }

        [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1")]
        public static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 ppFactory);

        // Os metodos "slot*" existem apenas para alinhar a vtable COM; nunca sao chamados.
        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIFactory1
        {
            void slot_SetPrivateData();
            void slot_SetPrivateDataInterface();
            void slot_GetPrivateData();
            void slot_GetParent();
            void slot_EnumAdapters();
            void slot_MakeWindowAssociation();
            void slot_GetWindowAssociation();
            void slot_CreateSwapChain();
            void slot_CreateSoftwareAdapter();
            [PreserveSig]
            int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);
            [PreserveSig]
            bool IsCurrent();
        }

        [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIAdapter1
        {
            void slot_SetPrivateData();
            void slot_SetPrivateDataInterface();
            void slot_GetPrivateData();
            void slot_GetParent();
            void slot_EnumOutputs();
            void slot_GetDesc();
            void slot_CheckInterfaceSupport();
            [PreserveSig]
            int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
        }
    }
}
