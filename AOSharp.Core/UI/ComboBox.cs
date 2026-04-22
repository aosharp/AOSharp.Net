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
    public class ComboBox : TextInputView
    {
        public EventHandler<ButtonBase> Clicked;

        internal ComboBox(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public void AppendItem(Variant value, string label)
        {
            ComboBox_c.AppendItem(Pointer, value.Pointer, StdString.Create(label).Pointer);
        }

        public void Clear()
        {
            ComboBox_c.Clear(Pointer);
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
