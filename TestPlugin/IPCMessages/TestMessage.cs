using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;
using TestPlugin.IPCMessages;

namespace TestPlugin
{
    [AoContract((int)IPCOpcode.Test)]
    public class TestMessage : IPCMessage
    {
        public override short Opcode => (int)IPCOpcode.Test;

        [AoMember(0)]
        public int Leet { get; set; }

        [AoMember(1)]
        public Vector3 Position { get; set; }
    }
}
