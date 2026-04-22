using System;
using System.Runtime.InteropServices;
using AOSharp.Core.GameData;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Core
{
    public unsafe class LockableItem : SimpleItem
    {
        public unsafe bool IsLocked => ItemFlags.HasFlag(SimpleItemFlags.Locked);

        public unsafe bool IsOpen => ItemFlags.HasFlag(SimpleItemFlags.Open);

        public LockableItem(IntPtr pointer) : base(pointer)
        {
        }

        public LockableItem(Dynel dynel) : base(dynel.Pointer)
        {
        }
    }
}
