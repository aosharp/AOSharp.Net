using System;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Core
{
    public static class Time
    {
        private static DateTime InitTime = DateTime.UtcNow;

        public static double AONormalTime => GetAOTime();
        public static double NormalTime => GetSystemTime();

        private  static double GetAOTime()
        {
            IntPtr pGameTime = GameTime_t.GetInstance();

            if (pGameTime == IntPtr.Zero)
                return 0;

            return GameTime_t.GetNormalTime(pGameTime);
        }

        private static double GetSystemTime()
        {
            return (DateTime.UtcNow - InitTime).TotalSeconds;
        }
    }
}
