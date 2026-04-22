using System;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.X86;

namespace AOSharp.Common.Unmanaged.Imports
{
    public class TeleportTrier_t
    {
        [DllImport("Gamecode.dll", EntryPoint = "?TeleportFailed@TeleportTrier_t@@QAEXXZ", CallingConvention = CallingConvention.ThisCall)]
        public static extern void TeleportFailed(IntPtr pThis);
        [Function(CallingConventions.MicrosoftThiscall)]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, CharSet = CharSet.Unicode, SetLastError = true)]
        public delegate void DTeleportFailed(IntPtr pThis);
    }
}
