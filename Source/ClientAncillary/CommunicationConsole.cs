using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace ClientAncillary
{
    public class CommunicationConsole
    {
        private const int ConnectionTimeoutMs = 5000;

        /// <summary>
        /// Відправляє бінарні дані через іменований канал (Named Pipe).
        /// ОПТИМІЗАЦІЯ: прямий бінарний запис усуває оверхед Base64 та виділення великих рядків у купі.
        /// </summary>
        public void SendData(int code, byte[] data)
        {
            var pipeName = "OCClientAncillary" + code;
            using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
            {
                client.Connect(ConnectionTimeoutMs);
                using (var writer = new BinaryWriter(client))
                {
                    if (data == null || data.Length == 0)
                    {
                        writer.Write(0);
                    }
                    else
                    {
                        writer.Write(data.Length);
                        writer.Write(data, 0, data.Length);
                    }
                    writer.Flush();
                }
            }
        }

        /// <summary>
        /// Приймає бінарні дані через іменований канал.
        /// ОПТИМІЗАЦІЯ: потокове вичитування сирих байтів безпосередньо у вихідний масив.
        /// </summary>
        public byte[] ReceiveData(int code, Action beforeWait)
        {
            var pipeName = "OCClientAncillary" + code;
            using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In))
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        beforeWait();
                    }
                    catch { }
                })
                {
                    IsBackground = true
                };
                thread.Start();

                server.WaitForConnection();

                using (var reader = new BinaryReader(server))
                {
                    int length = reader.ReadInt32();
                    if (length <= 0)
                    {
                        thread.Join(ConnectionTimeoutMs);
                        return new byte[0];
                    }

                    // Читаємо повний масив байтів
                    byte[] data = reader.ReadBytes(length);
                    thread.Join(ConnectionTimeoutMs);
                    return data;
                }
            }
        }
    }
}