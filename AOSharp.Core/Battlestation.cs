using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Core
{
    public class Battlestation
    {
        public static EventHandler<BattlestationInviteEventArgs> Invited;
        public static EventHandler<BattlestationScoreUpdateEventArgs> ScoreUpdated;

        [Obsolete("AOSharp.Core.JoinQueue(AOSharp.Core.Battlestation.Side) is deprecated. Use AOSharp.Core.JoinQueue(AOSharp.Common.GameData.BattlestationSide) instead")]
        public static void JoinQueue(Side side)
        {
            JoinQueue((BattlestationSide)side);
        }

        public static void JoinQueue(BattlestationSide side)
        {
            Network.Send(new CharacterActionMessage
            {
                Action = CharacterActionType.JoinBattlestationQueue,
                Target = DynelManager.LocalPlayer.Identity,
                Parameter2 = (int)side
            });
        }

        public static void LeaveQueue()
        {
            Network.Send(new CharacterActionMessage
            {
                Action = CharacterActionType.LeaveBattlestationQueue,
                Target = DynelManager.LocalPlayer.Identity
            });
        }

        public static void AcceptInvite(Identity battlestationIdentity)
        {
            Network.Send(new AcceptBSInviteMessage
            {
                UnkIdentity = battlestationIdentity,
                UnkByte = 1
            });
        }

        internal static void OnBattlestationInvite(Identity battlestationIdentity)
        {
            Invited?.Invoke(null, new BattlestationInviteEventArgs(battlestationIdentity));
        }

        internal static void OnSendScore(SendScoreMessage message)
        {
            ScoreUpdated?.Invoke(null, new BattlestationScoreUpdateEventArgs(message.RedScore, message.BlueScore, message.A, message.B, message.C, message.Core));
        }

        [Obsolete("AOSharp.Core.Battlestation.Side is obsolete. Use AOSharp.Common.GameData.BattlestationSide instead.")]
        public enum Side
        {
            Red = 0,
            Blue = 1            
        }
    }

    public class BattlestationScoreUpdateEventArgs : EventArgs
    {
        public int RedScore { get; }
        public int BlueScore { get; }
        public BattlestationSide A { get; }
        public BattlestationSide B { get; }
        public BattlestationSide C { get; }
        public BattlestationSide Core { get; }

        public BattlestationScoreUpdateEventArgs(int redScore, int blueScore, BattlestationSide a, BattlestationSide b, BattlestationSide c, BattlestationSide core)
        {
            RedScore = redScore;
            BlueScore = blueScore;
            A = a;
            B = b;
            C = c;
            Core = core;
        }
    }

    public class BattlestationInviteEventArgs : EventArgs
    {
        public Identity BattlestationIdentity { get; }

        public BattlestationInviteEventArgs(Identity battlestationIdentity)
        {
            BattlestationIdentity = battlestationIdentity;
        }

        public void Accept()
        {
            Battlestation.AcceptInvite(BattlestationIdentity);
        }
    }
}
