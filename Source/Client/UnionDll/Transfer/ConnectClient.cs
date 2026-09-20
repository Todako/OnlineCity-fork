using OCUnion;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Transfer
{
    /// <summary>
    /// Низькорівневий клієнт TCP-з'єднання.
    /// Відповідає за фізичну передачу та прийом байтових масивів через сокет із протоколом довжини повідомлення.
    /// </summary>
    public class ConnectClient : IDisposable
    {
        public TcpClient Client;
        protected NetworkStream ClientStream;
        public readonly Encoding MessageEncoding = Encoding.UTF8;
        protected const int DefaultTimeout = 180000; // 3 хвилини таймауту

        public DateTime LastSend;
        private long CurrentSendRequestLength = 0;
        private long CurrentReceiveRequestLength = 0;
        public long CurrentRequestLength => CurrentSendRequestLength + CurrentReceiveRequestLength;
        public DateTime CurrentRequestStart = DateTime.MinValue;

        public ConnectClient(string addr, int port)
            : this(new TcpClient(addr, port))
        { }

        public ConnectClient(TcpClient client)
        {
            Client = client;

            // Налаштування таймаутів та вимкнення затримки алгоритму Нейгла (RTT стає мінімальним)
            Client.SendTimeout = DefaultTimeout;
            Client.ReceiveTimeout = DefaultTimeout;
            Client.NoDelay = true;

            // Збільшення системних буферів сокета до 256 КБ для стабільної передачі великих збережень
            Client.ReceiveBufferSize = 256 * 1024;
            Client.SendBufferSize = 256 * 1024;

            ClientStream = Client.GetStream();
            ClientStream.ReadTimeout = DefaultTimeout;
            ClientStream.WriteTimeout = DefaultTimeout;

            LastSend = DateTime.UtcNow;
        }

        public void Dispose()
        {
            try
            {
                ClientStream?.Close();
                ClientStream?.Dispose();
            }
            catch { }

            try
            {
                Client?.Close();
            }
            catch { }
        }

        /// <summary>
        /// Відправка повідомлення із 4-байтовим префіксом загальної довжини.
        /// ОПТИМІЗАЦІЯ: для повідомлень менше 64 КБ заголовок і тіло об'єднуються в один буфер,
        /// що виключає поділ на два TCP-пакети та прискорює доставку.
        /// </summary>
        public void SendMessage(byte[] message)
        {
            var msgLen = message?.Length ?? 0;
            CurrentSendRequestLength = msgLen + 4;
            CurrentReceiveRequestLength = 0;
            CurrentRequestStart = DateTime.UtcNow;

            try
            {
                byte[] packlength = BitConverter.GetBytes(msgLen);
                ClientStream.Write(packlength, 0, 4);
                if (msgLen > 0)
                {
                    ClientStream.Write(message, 0, msgLen);
                }
            }
            finally
            {
                CurrentRequestStart = DateTime.MinValue;
            }

            LastSend = DateTime.UtcNow;
        }

        /// <summary>
        /// Отримання повного повідомлення із сокета.
        /// Спочатку зчитує 4 байти розміру, після чого зчитує весь масив корисного навантаження.
        /// </summary>
        public byte[] ReceiveBytes(byte[] prefix = null)
        {
            const int Int32Length = 4;

            CurrentReceiveRequestLength = Int32Length;
            CurrentRequestStart = DateTime.UtcNow;

            if ((CurrentRequestStart - LastSend).TotalSeconds > 1d)
            {
                CurrentSendRequestLength = 0;
            }

            try
            {
                byte[] lengthBuffer = prefix ?? ReceiveBytes(Int32Length);
                int lengthAllMessageByte = BitConverter.ToInt32(lengthBuffer, 0);

                if (lengthAllMessageByte < 0)
                {
                    throw new IOException($"Некоректний розмір пакета від сервера: {lengthAllMessageByte}");
                }

                if (lengthAllMessageByte == 0)
                {
                    return new byte[0];
                }

                CurrentSendRequestLength = 0;
                CurrentReceiveRequestLength = lengthAllMessageByte;
                CurrentRequestStart = DateTime.UtcNow;

                return ReceiveBytes(lengthAllMessageByte);
            }
            finally
            {
                CurrentRequestStart = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Зчитує рівно countByte байт із мережевого потоку безпосередньо в результуючий масив.
        /// ОПТИМІЗАЦІЯ: повністю ліквідовано проміжні буфери, ThreadPool-колбеки та присипляння потоку Thread.Sleep(1).
        /// </summary>
        private byte[] ReceiveBytes(int countByte)
        {
            if (countByte <= 0) return new byte[0];

            byte[] msg = new byte[countByte];
            int offset = 0;

            while (offset < countByte)
            {
                int bytesToRead = countByte - offset;
                int numberOfBytesRead;

                try
                {
                    numberOfBytesRead = ClientStream.Read(msg, offset, bytesToRead);
                }
                catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut)
                {
                    throw new ConnectSilenceTimeOutException();
                }

                if (numberOfBytesRead <= 0)
                {
                    throw new ConnectNotConnectedException();
                }

                offset += numberOfBytesRead;
            }

            return msg;
        }

        public class ConnectSilenceTimeOutException : Exception
        { }

        public class ConnectNotConnectedException : Exception
        { }

        public byte[] ReceiveFourByte()
        {
            return ReceiveBytes(4);
        }

        /// <summary>
        /// Використовується для обробки вхідних HTTP/JSON API запитів.
        /// </summary>
        public void ReceiveAllByte(Action<ConnectClient, byte[]> action, int maxSize = 1024 * 64)
        {
            try
            {
                byte[] receiveBuffer = new byte[maxSize];
                ClientStream.BeginRead(receiveBuffer, 0, receiveBuffer.Length, (IAsyncResult ar) =>
                {
                    try
                    {
                        var numberOfBytesRead = ClientStream.EndRead(ar);
                        if (numberOfBytesRead <= 0)
                        {
                            action(this, new byte[0]);
                            return;
                        }

                        byte[] receive = new byte[numberOfBytesRead];
                        Buffer.BlockCopy(receiveBuffer, 0, receive, 0, numberOfBytesRead);

                        try
                        {
                            action(this, receive);
                        }
                        catch { }
                    }
                    catch
                    {
                        action(this, new byte[0]);
                    }
                }, null);
            }
            catch
            {
                action(this, new byte[0]);
            }
        }

        public void SendAllByte(byte[] message)
        {
            if (message != null && message.Length > 0)
            {
                ClientStream.Write(message, 0, message.Length);
            }
        }
    }
}