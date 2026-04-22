using AOSharp.Common.GameData;
using AOSharp.Core.Combat;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.Unmanaged.Interfaces;

namespace AOSharp.Core
{
    public class ACGItem : DummyItem
    {
        [Obsolete("LowId is deprecated, use Id instead.", false)]
        public readonly int LowId;
        public readonly int HighId;
        public readonly int QualityLevel;

        internal ACGItem(int lowId, int highId, int ql) : this(GetPtr(lowId, highId, ql))
        {
        }

        internal ACGItem(IntPtr pointer) : base(pointer)
        {
            Id = GetStat(Stat.ACGItemTemplateID);
            LowId = Id;
            HighId = GetStat(Stat.ACGItemTemplateID2);
            QualityLevel = GetStat(Stat.ACGItemLevel);
        }
    }
}
