using System;
using System.Runtime.InteropServices;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Core
{
    public class MissionTerminal : Dynel
    {
        public MissionTerminal(IntPtr pointer) : base(pointer)
        {
        }

        public MissionTerminal(Dynel dynel) : base(dynel.Pointer)
        {
        }

        public void RequestMissions(byte difficulty = 6, byte goodBad = 255, byte orderChaos = 255, byte openHidden = 255, byte physicalMystical = 255, byte headonStealth = 255, byte creditsXp = 255)
        {
            MissionScope scope = Name.Contains("Team") ? MissionScope.Team : MissionScope.Solo;

            Network.Send(new QuestAlternativeMessage()
            {
                Unknown1 = 4,
                MissionSliders = new MissionSliders
                {
                    Difficulty = difficulty,
                    GoodBad = goodBad,
                    OrderChaos = orderChaos,
                    OpenHidden = openHidden,
                    PhysicalMystical = physicalMystical,
                    HeadonStealth = headonStealth,
                    CreditsXp = creditsXp
                },
                Scope = scope,
                Terminal = Identity,
                MissionDetails = Array.Empty<MissionInfo>()
            });
        }
    }
}
