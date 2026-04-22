using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.Unmanaged.Interfaces;

namespace AOSharp.Core.UI
{
    public class BitmapView : View
    {
        internal BitmapView(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public void SetBitmap(string gfxName)
        {
            SetBitmap(DynamicID.GetID(gfxName, true));
        }

        public void SetBitmap(int id)
        {
            Clear(true);
            BitmapView_c.SetBitmap(Pointer, id);
        }

        public void Clear(bool unk)
        {
            BitmapView_c.Clear(Pointer, unk);
        }
    }
}
