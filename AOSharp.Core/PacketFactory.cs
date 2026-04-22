using System;
using System.IO;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Messages;

namespace AOSharp.Core
{
    public class PacketFactory
    {
        private static MessageSerializer _serializer = new MessageSerializer();

        public static byte[] Create(MessageBody messageBody)
        {
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return null;

            int localDynelInstance = N3EngineClient_t.GetClientInst(pEngine);

            if(messageBody is N3Message n3Message)
                n3Message.Identity = new Identity(IdentityType.SimpleChar, localDynelInstance);
            else if (messageBody is TextMessage textMessage)
                textMessage.PayloadSize = textMessage.Text.Length + 3;

            var message = new Message
            {
                Body = messageBody,
                Header = new Header
                        {
                            MessageId = BitConverter.ToUInt16(new byte[] { 0xFF, 0xFF }, 0),
                            PacketType = messageBody.PacketType,
                            Unknown = 0x0001,
                            Sender = localDynelInstance,
                            Receiver = 0x02
                        }
            };

            using (MemoryStream stream = new MemoryStream())
            {
                _serializer.Serialize(stream, message);
                return stream.ToArray();
            }
        }

        public static Message Disassemble(byte[] packet)
        {
            return _serializer.Deserialize(packet);
        }
    }
}
