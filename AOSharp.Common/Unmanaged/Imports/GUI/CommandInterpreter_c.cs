using System;
using System.Text;
using System.Runtime.InteropServices;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.GameData;
using Reloaded.Hooks.Definitions.X86;

namespace AOSharp.Common.Unmanaged.Imports
{
    public class CommandInterpreter_c
    {
        [Function(CallingConventions.MicrosoftThiscall)]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public unsafe delegate IntPtr DGetCommand(IntPtr pThis, IntPtr pCmdText, bool unk);
        public static DGetCommand GetCommand;

        [Function(CallingConventions.MicrosoftThiscall)]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public unsafe delegate byte DProcessChatInput(IntPtr pThis, IntPtr pWindow, IntPtr pCmdText);
        public static unsafe DProcessChatInput ProcessChatInput;
    }
}
