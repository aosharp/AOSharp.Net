using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.GameData.UI;
using AOSharp.Common.Unmanaged.Interfaces;

namespace AOSharp.Core.UI
{
    public class Button : ButtonBase
    {
        public string Label => GetLabel();

        internal Button(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public void SetLabel(string text)
        {
            Button_c.SetLabel(Pointer, StdString.Create(text).Pointer);
        }

        public View GetBorderView(ButtonState state)
        {
            IntPtr ptr = Button_c.GetBorderView(Pointer, state);

            if (ptr == IntPtr.Zero)
                return null;

            return new View(ptr, false);
        }

        private string GetLabel()
        {
            IntPtr pStr = Button_c.GetLabel(Pointer);

            if (pStr == IntPtr.Zero)
                return string.Empty;

            return StdString.FromPointer(pStr, false).ToString();
        }

        public void SetGfx(ButtonState state, int gfxId)
        {
            Button_c.SetGfx(Pointer, state, gfxId);
        }

        public void SetGfx(ButtonState state, string gfxName)
        {
            SetGfx(state, DynamicID.GetID(gfxName, true));
        }

        public void SetBackgroundIcon(int gfxId, bool state)
        {
            Button_c.SetBackgroundIcon(Pointer, gfxId, state);
        }

        public void SetBackgroundIcon(string gfxName, bool state)
        {
            Button_c.SetBackgroundIcon(Pointer, DynamicID.GetID(gfxName, true), state);
        }

        public void SetColorOverride(uint unk)
        {
            Button_c.SetColorOverride(Pointer, unk);
        }

        public void SetLabelColor(uint unk)
        {
            Button_c.SetLabelColor(Pointer, unk);
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
