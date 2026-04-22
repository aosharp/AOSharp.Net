using System;
using System.Security.Policy;
using System.Runtime.InteropServices;
using AOSharp.Common.GameData;
using AOSharp.Common.Helpers;
using AOSharp.Common.Unmanaged.Imports;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Core
{
    public class TeamMember
    {
        public readonly string Name;
        public readonly Identity Identity;
        public readonly Profession Profession;
        public readonly int Level;
        public readonly int TeamIndex;
        public SimpleChar Character => DynelManager.GetDynel<SimpleChar>(Identity);
        public bool IsLeader => GetIsLeader();

        internal unsafe TeamMember(IntPtr pointer, int teamIndex)
        {
            Name = Utils.UnsafePointerToString(pointer);
            Identity = (*(MemStruct*)pointer).Identity;
            Profession = (Profession)(*(MemStruct*)pointer).Profession;
            Level = (*(MemStruct*)pointer).Level;
            TeamIndex = teamIndex;
        }

        private unsafe bool GetIsLeader()
        {
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return false;

            Identity identity = Identity;
            return N3EngineClientAnarchy_t.IsTeamLeader(pEngine, &identity);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        private struct MemStruct
        {
            //[FieldOffset(0x00)]
            //public string Name;

            [FieldOffset(0x1C)]
            public Identity Identity;

            [FieldOffset(0x2A)]
            public short Level;

            [FieldOffset(0x2C)]
            public byte Profession;
        }
    }
}
