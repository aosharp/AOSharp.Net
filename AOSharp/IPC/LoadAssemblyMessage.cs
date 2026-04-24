using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AOSharp.IPC
{
    public class LoadAssemblyMessage
    {
        private const byte OpCode = 0x00; // HookOpCode.LoadAssembly

        public IEnumerable<string> Assemblies { get; set; }

        public byte[] Serialize()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(OpCode);
                writer.Write(JsonConvert.SerializeObject(Assemblies));
                return stream.ToArray();
            }
        }
    }
}
