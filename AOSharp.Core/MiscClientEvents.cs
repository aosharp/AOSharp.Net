using AOSharp.Common.SharedEventArgs;
using System;

namespace AOSharp.Core
{
    //Hook events with specific purposes that don't really have a home
    public class MiscClientEvents
    {
        public static EventHandler<AttemptingSpellCastEventArgs> AttemptingSpellCast;

        private static void OnAttemptingSpellCast(AttemptingSpellCastEventArgs e)
        {
            AttemptingSpellCast?.Invoke(null, e);
        }
    }
}
