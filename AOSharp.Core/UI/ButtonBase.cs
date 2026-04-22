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
    public class ButtonBase : View
    {
        //TOOD: Toggle button stuff

        public EventHandler<ButtonBase> Clicked;

        internal ButtonBase(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
