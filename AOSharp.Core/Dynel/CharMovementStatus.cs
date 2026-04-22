using System.Runtime.InteropServices;
using AOSharp.Common.GameData;

namespace AOSharp.Core
{
    [StructLayout(LayoutKind.Explicit, Pack = 0)]
    public struct CharMovementStatus
    {
        [FieldOffset(0x04)]
        public MovementState State;
    }
}
