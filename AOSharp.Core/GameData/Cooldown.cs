using System.Runtime.InteropServices;
using AOSharp.Common.GameData;

namespace AOSharp.Core.GameData
{
    [StructLayout(LayoutKind.Explicit, Pack = 0, Size = 0x10)]
    public struct Cooldown
    {
        [FieldOffset(0x04)]
        public Stat Stat;

        [FieldOffset(0x08)]
        public int Total;

        [FieldOffset(0x0C)]
        public int Remaining;
    }
}
