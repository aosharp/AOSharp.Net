using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Core
{
    public class LocalCooldown
    {
        private static bool hasUpdatedSinceInject = false;
        private static ConcurrentDictionary<Stat, DateTime> cooldownCache = new ConcurrentDictionary<Stat, DateTime>();

        public static bool IsOnCooldown(Stat stat)
        {
            if (!hasUpdatedSinceInject)
            {
                foreach (var cooldown in DynelManager.LocalPlayer.Cooldowns)
                {
                    AddOrUpdate(cooldown.Key, cooldown.Value.Remaining);
                }
                hasUpdatedSinceInject = true;
            }

            return cooldownCache.ContainsKey(stat);
        }

        private static void AddOrUpdate(Stat stat, int remaining)
        {
            DateTime availableAt = DateTime.UtcNow.AddSeconds(remaining);
            cooldownCache.AddOrUpdate(stat, availableAt, (s, d) => availableAt);
        }

        internal static void OnSpecialUsed(N3Message n3Msg)
        {
            CharacterActionMessage characterActionMessage = n3Msg as CharacterActionMessage;
            Stat stat = (Stat)characterActionMessage.Parameter1;
            AddOrUpdate(stat, characterActionMessage.Parameter2);
        }

        internal static void OnSpecialAvailable(N3Message n3Msg)
        {
            CharacterActionMessage characterActionMessage = n3Msg as CharacterActionMessage;
            Stat stat = (Stat)characterActionMessage.Parameter2;
            cooldownCache.TryRemove(stat, out _);
        }

        internal static void OnSpecialUnavailable(N3Message n3Msg)
        {
            CharacterActionMessage characterActionMessage = n3Msg as CharacterActionMessage;
            Stat stat = (Stat)characterActionMessage.Parameter1;
            AddOrUpdate(stat, characterActionMessage.Parameter2);
        }
    }
}
