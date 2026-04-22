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
    public class TextView : View
    {
        public string Text
        {
            get { return GetText(); }
            set { SetText(value); }
        }

        internal TextView(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        private void SetText(string text)
        {
            TextView_c.SetText(Pointer, StdString.Create(text).Pointer);
        }

        private string GetText()
        {
            Variant value = Variant.Create();
            TextView_c.GetValue(Pointer, value.Pointer);
            return value.AsString();
        }

        public void SetDefaultColor(uint unk)
        {
            TextView_c.SetDefaultColor(Pointer, unk);
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
