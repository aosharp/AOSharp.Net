using System;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Core.UI
{
    public class VLayoutNode : LayoutNode
    {
        protected VLayoutNode(IntPtr pointer) : base(pointer)
        {
        }

        public static VLayoutNode Create()
        {
            IntPtr pLayoutNode = VLayoutNode_c.Create();

            if (pLayoutNode == IntPtr.Zero)
                return null;

            return new VLayoutNode(pLayoutNode);
        }

        public virtual void Dispose()
        {
            VLayoutNode_c.Deconstructor(_pointer);
        }     
    }
}
