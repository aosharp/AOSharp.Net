using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace AOSharp.IPC
{
    public class IPCClient
    {
        public Action<IPCClient> OnDisconnected = null;
        private NamedPipeClientStream _client;
        private byte[] _buffer = new byte[ushort.MaxValue];

        public IPCClient(string name)
        {
            _client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        }

        public void Connect(int timeout = 10000)
        {
            _client.Connect(timeout);
            BeginRead();
        }

        public void Disconnect()
        {
            _client.Close();
            _client.Dispose();
        }

        public void Send(byte[] data)
        {
            using (BinaryWriter writer = new BinaryWriter(_client, Encoding.Default, true))
            {
                writer.Write(data);
                writer.Flush();
            }
        }

        private void BeginRead()
        {
            try
            {
                _client.BeginRead(_buffer, 0, _buffer.Length, ReadCallback, null);
            }
            catch (Exception)
            {
                OnDisconnected?.Invoke(this);
            }
        }

        private void ReadCallback(IAsyncResult result)
        {
            try
            {
                int bytesRead = _client.EndRead(result);

                if (bytesRead == 0)
                    throw new IOException("bytesRead == 0");
            }
            catch (IOException)
            {
                OnDisconnected?.Invoke(this);
                return;
            }

            BeginRead();
        }
    }
}
