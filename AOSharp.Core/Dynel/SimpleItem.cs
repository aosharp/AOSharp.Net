using System;
using System.Runtime.InteropServices;
using AOSharp.Core.GameData;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Core
{
    public unsafe class SimpleItem : Dynel
    {
        public SimpleItemFlags ItemFlags => (*(MemStruct*)(Pointer - 0xB0)).Flags;

        public SimpleItem(IntPtr pointer) : base(pointer)
        {
        }

        public SimpleItem(Dynel dynel) : base(dynel.Pointer)
        {
        }

        public void Use()
        {
            Network.Send(new GenericCmdMessage()
            {
                Action = GenericCmdAction.Use,
                User = DynelManager.LocalPlayer.Identity,
                Target = Identity
            });
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        protected new unsafe struct MemStruct
        {
            [FieldOffset(0x4C)]
            public SimpleItemFlags Flags;
        }
    }
}
