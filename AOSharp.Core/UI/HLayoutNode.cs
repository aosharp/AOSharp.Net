using System;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Core.UI
{
    public class HLayoutNode : LayoutNode
    {
        protected HLayoutNode(IntPtr pointer) : base(pointer)
        {
        }

        public static HLayoutNode Create()
        {
            IntPtr pLayoutNode = HLayoutNode_c.Create();

            if (pLayoutNode == IntPtr.Zero)
                return null;

            return new HLayoutNode(pLayoutNode);
        }

        public virtual void Dispose()
        {
            HLayoutNode_c.Deconstructor(_pointer);
        }     
    }
}
