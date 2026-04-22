using System;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Core
{
    public static class Debug
    {
        public static void DrawSphere(Vector3 pos, float radius, Vector3 color)
        {
            IntPtr pDebugger = Debugger_t.GetInstance();
            Debugger_t.DrawSphere(pDebugger, pos.X, pos.Y, pos.Z, radius, color.X, color.Y, color.Z);
        }

        public static void DrawLine(Vector3 pos1, Vector3 pos2, Vector3 color)
        {
            IntPtr pDebugger = Debugger_t.GetInstance();
            Debugger_t.DrawLine(pDebugger, pos1.X, pos1.Y, pos1.Z, pos2.X, pos2.Y, pos2.Z, color.X, color.Y, color.Z);
        }
    }

    public static class DebuggingColor
    {
        public static Vector3 Purple = new Vector3(1, 0, 1);
        public static Vector3 Yellow = new Vector3(1, 1, 0);
        public static Vector3 Green = new Vector3(0, 1, 0);
        public static Vector3 LightBlue = new Vector3(0, 1, 1);
        public static Vector3 Blue = new Vector3(0, 0, 1);
        public static Vector3 Red = new Vector3(1, 0, 0);
        public static Vector3 White = new Vector3(1, 1, 1);
        public static Vector3 Black = new Vector3(0, 0, 0);
    }
}
