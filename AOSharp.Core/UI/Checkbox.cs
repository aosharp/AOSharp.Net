using System;
using AOSharp.Core.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.DataTypes;

namespace AOSharp.Core.UI
{
    public class Checkbox : View
    {
        public bool IsChecked => GetIsChecked();

        public EventHandler<bool> Toggled;

        internal Checkbox(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public static Checkbox Create(string name, string text, bool defaultValue, bool horizontalSpacer = false)
        {
            IntPtr pView = CheckBox_c.Create(name, text, defaultValue, horizontalSpacer);

            if (pView == IntPtr.Zero)
                return null;

            return new Checkbox(pView);
        }

        public override void Dispose()
        {
            CheckBox_c.Deconstructor(_pointer);
        }

        public void SetValue(bool value)
        {
            CheckBox_c.SetValue(Pointer, Variant.Create(value).Pointer, value);
        }

        private bool GetIsChecked()
        {
            Variant pOutput = Variant.Create(false);
            CheckBox_c.GetValue(_pointer, pOutput.Pointer);
            bool result = pOutput.AsBool();
            pOutput.Dispose();

            return result;
        }
    }
}
