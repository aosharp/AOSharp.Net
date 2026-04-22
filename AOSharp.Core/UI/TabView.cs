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
    public class TabView : View
    {
        public int TabCount => GetTabCount();

        internal TabView(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        private int GetTabCount()
        {
            return TabView_c.GetTabCount(_pointer);
        }

        public void AppendTab(string name, IntPtr pView)
        {
            StdString nameStr = StdString.Create(name);
            TabView_c.AppendTab(_pointer, nameStr.Pointer, pView);
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
