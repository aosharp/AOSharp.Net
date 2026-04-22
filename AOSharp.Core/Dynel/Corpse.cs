using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core.Inventory;

namespace AOSharp.Core
{
    public class Corpse : LockableContainer
    {
        public Corpse(IntPtr pointer) : base(pointer)
        {
        }

        public Corpse(Dynel dynel) : base(dynel)
        {
        }
    }
}
