using System;
using System.IO;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Messages;

namespace AOSharp.Core
{
    public class ChatPacketFactory
    {
        private static ChatMessageSerializer _serializer = new ChatMessageSerializer();

        public static byte[] Create(ChatMessageBody messageBody)
        {
            var message = new ChatMessage
            {
                Body = messageBody,
                Header = new ChatHeader
                        {
                            PacketType = messageBody.PacketType
                        }
            };

            using (MemoryStream stream = new MemoryStream())
            {
                _serializer.Serialize(stream, message);
                return stream.ToArray();
            }
        }

        public static ChatMessage Disassemble(byte[] packet)
        {
            return _serializer.Deserialize(packet);
        }
    }
}
