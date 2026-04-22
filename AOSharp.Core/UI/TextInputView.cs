using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.DataTypes;

namespace AOSharp.Core.UI
{
    public class TextInputView : View
    {
        public string Text
        {
            get { return GetText(); }
            set { SetText(value); }
        }

        internal TextInputView(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }


        private void SetText(string text)
        {
            IntPtr pTextView = TextInputView_c.GetTextView(Pointer);

            if (pTextView == IntPtr.Zero)
                return;

            TextView_c.SetText(pTextView, StdString.Create(text).Pointer);
        }

        private string GetText()
        {
            IntPtr pTextView = TextInputView_c.GetTextView(Pointer);

            if (pTextView == IntPtr.Zero)
                return string.Empty;

            Variant var = Variant.Create();
            IntPtr pStr = TextView_c.GetValue(pTextView, var.Pointer);

            if (pStr == IntPtr.Zero)
                return string.Empty;

            return var.AsString();
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
