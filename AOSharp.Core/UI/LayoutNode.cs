using System;

namespace AOSharp.Core.UI
{
    public class LayoutNode
    {
        protected readonly IntPtr _pointer;

        public IntPtr Pointer
        {
            get
            {
                return _pointer;
            }
        }

        protected LayoutNode(IntPtr pointer)
        {
            _pointer = pointer;
        }  
    }
}
