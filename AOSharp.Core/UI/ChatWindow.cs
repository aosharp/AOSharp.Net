using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Core.UI
{
    public class ChatWindow
    {
        private IntPtr _pointer;

        internal ChatWindow(IntPtr pointer)
        {
            _pointer = pointer;
        }

        public void WriteLine(object obj, ChatColor color = ChatColor.Gold)
        {
            WriteLine((obj == null) ? "null" : obj.ToString(), color);
        }

        public void WriteLine(string text, ChatColor color = ChatColor.Gold)
        {
            if (_pointer != IntPtr.Zero)
            {
                ChatWindowNode_t.AppendText(_pointer, StdString.Create(text).Pointer, color);
            }
            else
            {
                Chat.WriteLine(text, color);
            }
        }
    }
}
