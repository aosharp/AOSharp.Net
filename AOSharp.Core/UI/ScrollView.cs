using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core.GameData;
using AOSharp.Common.Unmanaged.Imports;
namespace AOSharp.Core.UI
{
    public class ScrollView : View
    {
        protected ScrollView(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public static ScrollView Create(Rect rect, string name)
        {
            IntPtr pView = ScrollView_c.Create(rect, name, 0, 3, -1, 0, 0);

            if (pView == IntPtr.Zero)
                return null;

            return new ScrollView(pView, true);
        }

        public override void Dispose()
        {
            ScrollView_c.Deconstructor(_pointer);
        }

        public void SetClient(View view1, View view2)
        {
            ScrollView_c.SetClient(_pointer, view1.Pointer, (view2 == null) ? IntPtr.Zero : view2.Pointer);
        }
    }
}
