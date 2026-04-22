using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Core
{
    public static class Duel
    {
        private static Identity _pendingOpponent;
        public static EventHandler<DuelRequestEventArgs> Challenged;
        public static EventHandler<DuelStatusChangedEventArgs> StatusChanged;

        public static void Challenge(Identity opponent)
        {
            Network.Send(new CharacterActionMessage
            {
                Action = CharacterActionType.DuelUpdate,
                Target = opponent
            });

            _pendingOpponent = opponent;
        }

        internal static void Accept()
        {
            Network.Send(new CharacterActionMessage
            {
                Action = CharacterActionType.DuelUpdate,
                Parameter1 = (int)DuelUpdate.Accept
            });
        }

        internal static void Decline()
        {
            Network.Send(new CharacterActionMessage
            {
                Action = CharacterActionType.DuelUpdate,
                Parameter1 = (int)DuelUpdate.Decline
            });
        }

        public static void Stop()
        {
            Network.Send(new CharacterActionMessage
            {
                Action = CharacterActionType.DuelUpdate,
                Parameter1 = (int)DuelUpdate.Stop
            });
        }

        internal static void OnDuelUpdate(Identity identity, DuelUpdate update)
        {
            switch (update)
            {
                case DuelUpdate.Challenge:
                    if (identity != _pendingOpponent)
                        Challenged?.Invoke(null, new DuelRequestEventArgs(identity));
                    break;
                case DuelUpdate.Accept:
                case DuelUpdate.Decline:
                case DuelUpdate.Stop:
                    _pendingOpponent = Identity.None;
                    StatusChanged?.Invoke(null, new DuelStatusChangedEventArgs(identity, (DuelStatus)update));
                    break;
            }
        }
    }

    public class DuelStatusChangedEventArgs : EventArgs
    {
        public Identity Opponent { get; }
        public DuelStatus Status { get; set; }

        public DuelStatusChangedEventArgs(Identity opponent, DuelStatus status)
        {
            Opponent = opponent;
            Status = status;
        }
    }

    public class DuelRequestEventArgs : EventArgs
    {
        public Identity Challenger { get; }
        public bool Responded { get; set; }

        public DuelRequestEventArgs(Identity challenger)
        {
            Challenger = challenger;
        }

        public void Accept()
        {
            Duel.Accept();
            Responded = true;
        }

        public void Decline()
        {
            Duel.Decline();
            Responded = true;
        }
    }

    public enum DuelStatus
    {
        Accepted = 1,
        Declined = 2,
        Stopped = 3
    }
}
