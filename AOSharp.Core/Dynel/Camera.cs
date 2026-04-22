using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core.Inventory;

namespace AOSharp.Core
{
    public class Camera
    {
        public static IntPtr Pointer => N3EngineClient_t.GetActiveCamera(N3Engine_t.GetInstance());
        public static bool IsFirstPerson => N3Camera_t.IsFirstPerson(Pointer);
        public static Vector3 Position => new Dynel(Pointer).Position;
        public static Quaternion Rotation => new Dynel(Pointer).Rotation;
        public static void StartZoomIn() => N3Camera_t.StartZoomIn(Pointer);
        public static void StopZoomIn() => N3Camera_t.StopZoomIn(Pointer);
        public static void StartZoomout() => N3Camera_t.StartZoomOut(Pointer);
        public static void StopZoomOut() => N3Camera_t.StopZoomOut(Pointer);

    }
}