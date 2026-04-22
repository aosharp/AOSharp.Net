using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core.Combat;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using AOSharp.Core.GameData;

namespace AOSharp.Core
{
    public class Buff : DummyItem
    {
        public readonly Identity Owner;
        public readonly NanoLine Nanoline;
        public readonly int NCU;
        public readonly int StackingOrder;
        public float RemainingTime => GetCurrentTime();
        public float TotalTime => GetTotalTime();

        internal Buff(Identity owner, Identity identity) : base(GetPtr(identity))
        {
            Owner = owner;
            Nanoline = (NanoLine)GetStat(Stat.NanoStrain);
            StackingOrder = GetStat(Stat.StackingOrder);
            NCU = GetStat(Stat.Level);
        }

        private unsafe float GetCurrentTime()
        {
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return 0;

            Identity identity = new Identity(IdentityType.NanoProgram, Id);
            Identity owner = Owner;
            return N3EngineClientAnarchy_t.GetBuffCurrentTime(pEngine, ref identity, ref owner) / 100;
        }

        private unsafe float GetTotalTime()
        {
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return 0;

            Identity identity = new Identity(IdentityType.NanoProgram, Id);
            Identity owner = Owner;
            return N3EngineClientAnarchy_t.GetBuffTotalTime(pEngine, ref identity, ref owner) / 100f;
        }

        public bool Remove()
        {
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return false;

            Identity identity = new Identity(IdentityType.NanoProgram, Id);
            return N3EngineClientAnarchy_t.RemoveBuff(pEngine, ref identity);
        }
    }
}
