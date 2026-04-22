using AOSharp.Common.Unmanaged.DbObjects;
using AOSharp.Common.Unmanaged.Imports;
using System;

namespace AOSharp.Core
{
    public class Zone : IEquatable<Zone>
    {
        public int Instance => N3Zone_t.GetInstance(Pointer);
        public virtual SurfaceResource SurfaceResource => SurfaceResource.FromPointer(N3Zone_t.GetSurface(Pointer));

        public readonly IntPtr Pointer;

        public Zone(IntPtr pointer)
        {
            Pointer = pointer;
        }

        internal unsafe void LoadSurface()
        {
            IntPtr pSurface = Playfield.GetSurface();

            if (pSurface == IntPtr.Zero)
                return;

            IntPtr pSurfaceUnk = *(IntPtr*)(pSurface + 0x0C);

            if (pSurfaceUnk == IntPtr.Zero)
                return;

            N3Zone_t.LoadSurface(Pointer, pSurfaceUnk);
        }

        public override int GetHashCode() => Instance.GetHashCode();
        public override bool Equals(object obj) => this.Equals(obj as Zone);

        public bool Equals(Zone obj)
        {
            if (Object.ReferenceEquals(obj, null))
                return false;

            if (Object.ReferenceEquals(this, obj))
                return true;


            return Instance == obj.Instance;
        }

        public static bool operator ==(Zone a, Zone b)
        {
            if (object.ReferenceEquals(a, null))
                return object.ReferenceEquals(b, null);

            return a.Equals(b);
        }

        public static bool operator !=(Zone a, Zone b) => !(a == b);
    }
}
