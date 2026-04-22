using System;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Core.UI
{
    public class BorderView : View
    {
        protected BorderView(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public new static BorderView Create(Rect rect, string name, int unk1, int unk2)
        {
            IntPtr pView = BorderView_c.Create(rect, name, unk1, unk2);

            if (pView == IntPtr.Zero)
                return null;

            return new BorderView(pView);
        }

        public override void Dispose()
        {
            BorderView_c.Deconstructor(_pointer);
        }

        public void SetClient(View client, float x1, float y1, float x2, float y2)
        {
            BorderView_c.SetClient(_pointer, client.Pointer, x1, y1, x2, y2);
        }
    }
}
