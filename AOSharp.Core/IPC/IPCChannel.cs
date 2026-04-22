using System;

namespace AOSharp.Core.IPC
{
    public class IPCChannel<TOpcode> : IPCChannel where TOpcode : Enum, IConvertible
    {
        public IPCChannel(byte channelId) : base(channelId)
        {
        }

        public void RegisterCallback(TOpcode opCode, Action<int, IPCMessage> callback) => RegisterCallback(opCode.ToInt16(null), callback);
    }

    public class IPCChannel : IPCChannelBase
    {
        public IPCChannel(byte channelId) : base(channelId)
        {
        }

        internal static void UpdateInternal() => Update();

        protected override int _localDynelId => Game.ClientInst;
    }
}
