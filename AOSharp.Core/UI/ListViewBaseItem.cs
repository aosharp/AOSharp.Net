using System;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core.GameData;

namespace AOSharp.Core.UI
{
    public class ListViewBaseItem
    {
        protected readonly IntPtr _pointer;

        public IntPtr Pointer
        {
            get
            {
                return _pointer;
            }
        }

        protected ListViewBaseItem(IntPtr pointer)
        {
            _pointer = pointer;
        }

        public virtual void Dispose()
        {
        }

        public void AppendChild(ListViewBaseItem item)
        {
            ListViewBaseItem_c.AppendChild(_pointer, item.Pointer);
        }

        public void MakeSelectable(bool selectable)
        {
            ListViewBaseItem_c.MakeSelectable(_pointer, selectable);
        }
    }
}
