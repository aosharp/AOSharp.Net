using System;
using System.Runtime.InteropServices;
using AOSharp.Common.Helpers;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Bootstrap
{
    public class ProcessChatInputPatcher
    {
        private const string ProcessChatInputSig = "B8 ? ? ? ? E8 ? ? ? ? 81 EC ? ? ? ? 53 56 33 F6 57 89 4D D0 39 75 08 0F 85 ? ? ? ? E8 ? ? ? ? 8B C8 E8 ? ? ? ? 89 45 08 3B C6 75 07 32 C0 E9 ? ? ? ? 8B 7D 0C 83 7F 14 10 72 04 8B 07 EB 02 8B C7 80 38 2F 0F 84 ? ? ? ? FF 15 ? ? ? ? 05 ? ? ? ? 8B 08 3B CE 0F 84 ? ? ? ? 89 45 E8 A1 ? ? ? ? 89 4D E4 8B 08 89 4D EC 8D 4D E4 89 08 89 75 FC 8B 4D E4 8B 41 14 89 45 E4 8B 41 0C 2B C6 74 24 48 74 19 48 74 0D 48 75 20";

        private const int CommandNotFoundOffset = 0x145;
        private const int CommandNotSegmentSize = 0x83;
        private const int GetCommandOffset = 0x137;
        private const byte NOP = 0x90;

        private static bool _patched = false;
        private static IntPtr _pProcessChatInput;
        private static IntPtr _pOrig;

        public static unsafe bool Patch(out IntPtr pProcessChatInput, out IntPtr pGetCommand)
        {
            pProcessChatInput = Utils.FindPattern("GUI.dll", ProcessChatInputSig);
            pGetCommand = IntPtr.Zero;

            if (pProcessChatInput == IntPtr.Zero)
                return false;

            pGetCommand = pProcessChatInput + GetCommandOffset + sizeof(IntPtr) + Marshal.ReadInt32(pProcessChatInput + GetCommandOffset);

            if (pGetCommand == IntPtr.Zero)
                return false;

            if (!Kernel32.VirtualProtectEx(Kernel32.GetCurrentProcess(), pProcessChatInput + CommandNotFoundOffset, (UIntPtr)CommandNotSegmentSize, 0x40 /* EXECUTE_READWRITE */, out uint _))
                return false;

            _pOrig = Marshal.AllocHGlobal((int)CommandNotSegmentSize);

            // Buffer.MemoryCopy replaces RtlCopyMemory/CopyMemory — works directly with pointers,
            // no P/Invoke needed, and is fully supported in .NET Core.
            Buffer.MemoryCopy(
                (void*)(pProcessChatInput + CommandNotFoundOffset),
                (void*)_pOrig,
                CommandNotSegmentSize,
                CommandNotSegmentSize);

            for (int i = 0; i < CommandNotSegmentSize; i++)
                *(byte*)(pProcessChatInput + CommandNotFoundOffset + i) = NOP;

            _pProcessChatInput = pProcessChatInput;
            _patched = true;
            return true;
        }

        public static unsafe void Unpatch()
        {
            if (!_patched)
                return;

            Buffer.MemoryCopy(
                (void*)_pOrig,
                (void*)(_pProcessChatInput + CommandNotFoundOffset),
                CommandNotSegmentSize,
                CommandNotSegmentSize);

            Marshal.FreeHGlobal(_pOrig);

            Kernel32.VirtualProtectEx(Kernel32.GetCurrentProcess(), _pProcessChatInput + CommandNotFoundOffset, (UIntPtr)CommandNotSegmentSize, 0x20 /* EXECUTE_READ */, out uint _);
        }
    }
}