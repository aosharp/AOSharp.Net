using System;
using System.Collections.Concurrent;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Core
{
    public static class CityController
    {
        public static CloakStatus CloakState { internal set; get; } = CloakStatus.Unknown;
        public static string OrgName { internal set; get; }
        public static int CreditUpkeep { internal set; get; }
        public static int NextUpkeep { internal set; get; }
        public static float Charge { internal set; get; }
        private static DateTime _cloakStartTime;
        private static int? _cloakTimer;
        private static AOSignalAction _lastSignalAction;

        public static void Use() => DynelManager.AllDynels.FirstOrDefault(x => x.Identity.Type == IdentityType.CityController)?.Use();

        public static bool CanToggleCloak()
        {
            if (_cloakTimer == null)
                return false;

            return (DateTime.Now - _cloakStartTime).TotalSeconds >= _cloakTimer;
        }

        public static void ToggleCloak()
        {
            if (_lastSignalAction == AOSignalAction.Close)
                return;

            if (!CanToggleCloak())
                return;

            Network.Send(new ToggleCloakMessage { Unknown1 = 49152 });
        }

        internal static void OnAOSignalTransportMessage(AOTransportSignalMessage transporgMsg)
        {
            _lastSignalAction = transporgMsg.Action;

            switch (transporgMsg.Action)
            {
                case AOSignalAction.CreditsUpkeepInfo:
                    CreditUpkeep = ((CityCreditsUpkeep)transporgMsg.TransportSignalMessage).CreditsUpkeep;
                    break;
                case AOSignalAction.UpkeepInfo:
                    NextUpkeep = ((CityNextUpkeep)transporgMsg.TransportSignalMessage).NextUpkeepPaymentInSeconds;
                    break;
                case AOSignalAction.CloakInfo:
                    var cloakInfo = (CloakInfo)transporgMsg.TransportSignalMessage;
                    CloakState = cloakInfo.CloakState; 
                    _cloakStartTime = DateTime.Now;
                    _cloakTimer = cloakInfo.ShieldTimerInSeconds;
                    break;
                case AOSignalAction.CityInfo:
                    OrgName = ((CityInfo)transporgMsg.TransportSignalMessage).OrgName;
                    break;
                case AOSignalAction.ChargeInfo:
                    Charge = ((CityCharge)transporgMsg.TransportSignalMessage).CityControllerCharge;
                    break;
            }
        }
    }
}