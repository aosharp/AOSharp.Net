using System;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.X86;

namespace AOSharp.Common.Unmanaged.Imports
{
    public class OptionPanelModule_c
    {
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public unsafe delegate IntPtr GetOptionWindowDelegate(IntPtr pThis);
        public static GetOptionWindowDelegate GetOptionWindow;

        [DllImport("GUI.dll", EntryPoint = "?ModuleActivated@OptionPanelModule_c@@UAEX_N@Z", CallingConvention = CallingConvention.ThisCall)]
        public static extern void ModuleActivated(IntPtr pThis, bool unk);
        [Function(CallingConventions.MicrosoftThiscall)]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, CharSet = CharSet.Unicode, SetLastError = true)]
        public delegate void DModuleActivated(IntPtr pThis, bool unk);
    }
}
