using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Core
{
    public static class NpcDialog
    {
        public static Action<Identity> ChatWindowOpened;
        public static EventHandler<Dictionary<int, string>> AnswerListChanged;

        public static void Open(Dynel target)
        {
            Open(target.Identity);
        }

        public static void Open(Identity target)
        {
            Network.Send(new KnuBotOpenChatWindowMessage()
            {
                Unknown1 = 2,
                Target = target
            });
        }


        public static void SelectAnswer(Identity target, int answer)
        {
            Network.Send(new KnuBotAnswerMessage()
            {
                Unknown1 = 2,
                Target = target,
                Answer = answer
            });
        }

        public static void FinishTrade(Identity target, int amount)
        {
            Network.Send(new KnuBotFinishTradeMessage()
            {
                Unknown1 = 2,
                Target = target,
                Decline = 2,
                Amount = amount
            });
        }
        public static void OpenTrade(Identity target, int slots = 0)
        {
            Network.Send(new KnuBotStartTradeMessage()
            {
                Unknown1 = 2,
                Target = target,
                NumberOfItemSlotsInTradeWindow = slots,
                Message = ""
            });
        }

        internal static void OnKnubotAnswerList(N3Message n3Msg)
        {
            KnuBotAnswerListMessage knubotMsg = (KnuBotAnswerListMessage)n3Msg;
            Dictionary<int, string> options = new Dictionary<int, string>();

            for (int i = 0; i < knubotMsg.DialogOptions.Length; i++)
            {
                options.Add(i, knubotMsg.DialogOptions[i].Text);
            }

            AnswerListChanged?.Invoke(knubotMsg.Target, options);
        }

        internal static void OnKnubotOpenChatWindow(N3Message n3Msg)
        {
            KnuBotOpenChatWindowMessage knubotMsg = (KnuBotOpenChatWindowMessage)n3Msg;

            ChatWindowOpened?.Invoke(knubotMsg.Target);
        }
    }
}
