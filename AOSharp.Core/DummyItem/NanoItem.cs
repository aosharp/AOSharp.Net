using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core.Combat;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using AOSharp.Core.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using AOSharp.Core.UI;

namespace AOSharp.Core
{
    public class NanoItem : DummyItem
    {
        public NanoLine Nanoline => (NanoLine)GetStat(Stat.NanoStrain);
        public int NCU => GetStat(Stat.Level);
        public int StackingOrder => GetStat(Stat.StackingOrder);
        public NanoSchool NanoSchool => (NanoSchool)GetStat(Stat.School);

        public int Cost => GetCost();

        public override float AttackRange => Math.Min(base.AttackRange * (1 + DynelManager.LocalPlayer.GetStat(Stat.NanoRange) / 100f), 40f);

        internal NanoItem(Identity identity) : base(GetPtr(identity))
        {
        }

        internal NanoItem(IntPtr pointer) : base(pointer)
        {
        }

        private int GetCost()
        {
            int costModifier = DynelManager.LocalPlayer.GetStat(Stat.NPCostModifier);
            int baseCost = GetStat(Stat.NanoPoints);

            switch (DynelManager.LocalPlayer.Breed)
            {
                case Breed.Nanomage:
                    costModifier = costModifier < 45 ? 45 : costModifier;
                    break;
                case Breed.Atrox:
                    costModifier = costModifier < 55 ? 55 : costModifier;
                    break;
                case Breed.Solitus:
                case Breed.Opifex:
                default:
                    costModifier = costModifier < 50 ? 50 : costModifier;
                    break;
            }

            return (int)(baseCost * ((double)costModifier / 100));
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        private struct MemStruct
        {
            [FieldOffset(0x08)]
            public int Id;
        }
    }
}
