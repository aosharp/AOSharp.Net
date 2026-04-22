using System;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.DataTypes;

namespace AOSharp.Core.UI
{
    public class StringListViewItem : ListViewBaseItem
    {
        protected StringListViewItem(IntPtr pointer) : base(pointer)
        {
        }

        public static StringListViewItem Create(Variant variant, string name, int unk1, int unk2)
        {
            IntPtr pView = StringListViewItem_c.Create(variant, name, unk1, unk2);

            if (pView == IntPtr.Zero)
                return null;

            return new StringListViewItem(pView);
        }

        public override void Dispose()
        {
            StringListViewItem_c.Deconstructor(_pointer);
        }
    }
}
