using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core.Inventory;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Core
{
    public enum TradeState
    {
        None,
        Opened,
        Accept,
        Confirm,
        Declined,
        Finished
    }

    public static class Trade
    {
        public static Identity? TradeTarget;
        public static Action<Identity, TradeState> TradeStateChanged;

        public static void Open(Identity trader)
        {
            Network.Send(new TradeMessage
            {
                Unknown1 = 2,
                Action = TradeAction.Open,
                Param1 = (int)trader.Type,
                Param2 = trader.Instance,
            });
        }

        public static void AddItem(Identity slot)
        {
            AddItem(DynelManager.LocalPlayer.Identity, slot);
        }

        public static void AddItem(Item item)
        {
            AddItem(DynelManager.LocalPlayer.Identity, item.Slot);
        }

        public static void AddItem(Identity trade, Identity slot)
        {
            Network.Send(new TradeMessage
            {
                Unknown1 = 2,
                Action = TradeAction.AddItem,
                Param1 = (int)trade.Type,
                Param2 = trade.Instance,
                Param3 = (int)slot.Type,
                Param4 = slot.Instance,
            });
        }

        public static void SetCredits(int credits)
        {
            Network.Send(new TradeMessage
            {
                Unknown1 = 2,
                Action = TradeAction.UpdateCredits,
                Param2 = credits
            });
        }

        public static void Accept()
        {
            if (TradeTarget.HasValue)
                Accept(TradeTarget.Value);
        }

        public static void Accept(Identity trade)
        {
            Network.Send(new TradeMessage
            {
                Unknown1 = 2,
                Action = TradeAction.Accept,
                Param1 = (int)trade.Type,
                Param2 = trade.Instance,
            });
        }

        public static void Confirm()
        {
            if (TradeTarget.HasValue)
                Confirm(TradeTarget.Value);
        }

        public static void Confirm(Identity trade)
        {
            Network.Send(new TradeMessage
            {
                Unknown1 = 2,
                Action = TradeAction.Confirm,
                Param1 = (int)trade.Type,
                Param2 = trade.Instance,
            });
        }

        public static void Decline()
        {
            Network.Send(new TradeMessage
            {
                Unknown1 = 2,
                Action = TradeAction.Decline,
                Param2 = 1,
            });
        }

        internal static void OnTradeMessage(N3Message n3Msg)
        {
            TradeMessage tradeMsg = (TradeMessage)n3Msg;

            switch(tradeMsg.Action)
            {
                case TradeAction.Open:
                    Identity target = new Identity((IdentityType)tradeMsg.Param1, tradeMsg.Param2);

                    if (target == DynelManager.LocalPlayer.Identity)
                        return;

                    TradeTarget = target;
                    TradeStateChanged?.Invoke(TradeTarget.Value, TradeState.Opened);
                    break;
                case TradeAction.Confirm:
                    TradeStateChanged?.Invoke(TradeTarget.Value, TradeState.Confirm);
                    break;
                case TradeAction.Accept:
                    TradeStateChanged?.Invoke(TradeTarget.Value, TradeState.Accept);
                    break;
                case TradeAction.Complete:
                    if (tradeMsg.Param1 == 50000 && tradeMsg.Param2 == Game.ClientInst)
                        return;

                    TradeStateChanged?.Invoke(Identity.None, TradeState.Finished);
                    TradeTarget = null;
                    break;
                case TradeAction.Decline:
                    TradeStateChanged?.Invoke(TradeTarget.Value, TradeState.Declined);
                    break;
            }
        }
    }
}
