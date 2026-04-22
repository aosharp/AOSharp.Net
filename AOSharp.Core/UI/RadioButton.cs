using System;
using AOSharp.Core.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.DataTypes;

namespace AOSharp.Core.UI
{
    public class RadioButton : View
    {
        public bool IsSelected => RadioButton_c.GetState(Pointer);

        internal RadioButton(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public static RadioButton Create(string name, string text)
        {
            IntPtr pView = RadioButton_c.Create(name, text, -1, 0, 0);

            if (pView == IntPtr.Zero)
                return null;

            return new RadioButton(pView, true);
        }

        public override void Dispose()
        {
            RadioButton_c.Deconstructor(_pointer);
        }

        public override void Update()
        {
        }
    }
}
