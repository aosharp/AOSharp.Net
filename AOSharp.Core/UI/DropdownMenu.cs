using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.DataTypes;
using System.Runtime.InteropServices;

namespace AOSharp.Core.UI
{
    public class DropdownMenu : View
    {
        internal DropdownMenu(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public void AppendItem(string label)
        {
            DropdownMenu_c.AppendItem(Pointer, Variant.Create().Pointer, StdString.Create(label).Pointer);
        }

        public void SelectByIndex(uint num, bool unk)
        {
            DropdownMenu_c.SelectByIndex(Pointer, num, unk);
        }

        public uint GetSelection()
        {
            return DropdownMenu_c.GetSelection(Pointer);
        }

        public void DeleteItem(uint num)
        {
            DropdownMenu_c.DeleteItem(Pointer, num);
        }

        public string GetItemLabel(uint num)
        {
            IntPtr intPtr = DropdownMenu_c.GetItemLabel(Pointer, num);

            if (intPtr == IntPtr.Zero)
                return string.Empty;

            return StdString.FromPointer(intPtr, false).ToString();
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
