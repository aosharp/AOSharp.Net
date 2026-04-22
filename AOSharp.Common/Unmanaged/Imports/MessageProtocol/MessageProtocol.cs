using System;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.X86;

namespace AOSharp.Common.Unmanaged.Imports
{
    public class MessageProtocol
    {
        [DllImport("MessageProtocol.dll", EntryPoint = "?DataBlockToMessage@@YAPAVMessage_t@@IPAX@Z", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr DataBlockToMessage(uint size, [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] byte[] dataBlock);
        [Function(CallingConventions.Cdecl)]
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        public delegate IntPtr DDataBlockToMessage(uint size, [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] byte[] dataBlock);
    }
}
