using System.Runtime.InteropServices;
using AOSharp.Common.GameData;

namespace AOSharp.Core.GameData
{
    [StructLayout(LayoutKind.Explicit, Pack = 0)]
    public struct SpecialAction
    {
        [FieldOffset(0x08)]
        public Identity Identity;

        [FieldOffset(0x10)]
        public Identity Owner;

        [FieldOffset(0x18)]
        public SpecialAttack OpCode;
        
        //There's technically more to this struct but I've yet to find a use in the rest..
    }
}
