using System;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Core.UI
{
    public class ViewSelector : View
    {
        internal ViewSelector(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public static ViewSelector FromPointer(IntPtr pointer, bool track)
        {
            return new ViewSelector(pointer, track);
        }

        public static ViewSelector Create(Rect rect, string name)
        {
            IntPtr pView = ViewSelector_c.Create(rect, name, -1, 0, 0);

            if (pView == IntPtr.Zero)
                return null;

            return new ViewSelector(pView, true);
        }

        public override void Dispose()
        {
            ViewSelector_c.Deconstructor(_pointer);
        }

        public void SetView(View view)
        {
            ViewSelector_c.SetValue(Pointer, Variant.Create(view.Handle).Pointer, true);
        }

        public void SetListView(ListViewBase listViewBase)
        {
            ViewSelector_c.SetListView(_pointer, listViewBase.Pointer);
        }

        public ListViewBase GetListView()
        {
            return ListViewBase.FromPointer(ViewSelector_c.GetListView(_pointer), true);
        }

        public void AppendView(View view)
        {
            ViewSelector_c.AppendView(_pointer, view.Pointer);
        }
    }
}
