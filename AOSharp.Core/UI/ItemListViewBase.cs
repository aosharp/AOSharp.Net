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
    public class ItemListViewBase : MultiListView
    {
        protected ItemListViewBase(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public static new ItemListViewBase Create(Rect rect, int flags, int unk = 0, int unk2 = 0)
        {
            IntPtr pView = ItemListViewBase_c.Create(rect, flags, unk, unk2, Identity.None);

            if (pView == IntPtr.Zero)
                return null;

            return new ItemListViewBase(pView, true);
        }

        public override void Dispose()
        {
            //MultiListView_c.Deconstructor(_pointer);
        }
    }
}
