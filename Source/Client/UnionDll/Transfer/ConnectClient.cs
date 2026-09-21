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

        // Постійні екземплярні буфери для усунення виділень пам'яті (0 байт GC на кожному пакеті)
        private readonly byte[] _headerSendBuffer = new byte[4];
        private readonly byte[] _headerReceiveBuffer = new byte[4];

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
        /// ОПТИМІЗАЦІЯ: усунено BitConverter.GetBytes(msgLen) без створення зайвих масивів у купі.
        /// Дані записуються безпосередньо в мережевий потік.
        /// </summary>
        public void SendMessage(byte[] message)
        {
            int msgLen = message?.Length ?? 0;
            CurrentSendRequestLength = msgLen + 4;
            CurrentReceiveRequestLength = 0;
            CurrentRequestStart = DateTime.UtcNow;

            try
            {
                _headerSendBuffer[0] = (byte)msgLen;
                _headerSendBuffer[1] = (byte)(msgLen >> 8);
                _headerSendBuffer[2] = (byte)(msgLen >> 16);
                _headerSendBuffer[3] = (byte)(msgLen >> 24);

                ClientStream.Write(_headerSendBuffer, 0, 4);
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
        /// ОПТИМІЗАЦІЯ: читання заголовка виконується безпосередньо у внутрішній буфер
        /// без виділення проміжного масиву new byte[4].
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
                int lengthAllMessageByte;

                if (prefix != null && prefix.Length >= 4)
                {
                    lengthAllMessageByte = prefix[0] | (prefix[1] << 8) | (prefix[2] << 16) | (prefix[3] << 24);
                }
                else
                {
                    ReadExactBytes(_headerReceiveBuffer, 0, Int32Length);
                    lengthAllMessageByte = _headerReceiveBuffer[0] | (_headerReceiveBuffer[1] << 8) | (_headerReceiveBuffer[2] << 16) | (_headerReceiveBuffer[3] << 24);
                }

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

                byte[] msg = new byte[lengthAllMessageByte];
                ReadExactBytes(msg, 0, lengthAllMessageByte);
                return msg;
            }
            finally
            {
                CurrentRequestStart = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Зчитує рівно count байт із сокета безпосередньо у вказаний буфер без проміжних копіювань.
        /// </summary>
        private void ReadExactBytes(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesToRead = count - totalRead;
                int numberOfBytesRead;

                try
                {
                    numberOfBytesRead = ClientStream.Read(buffer, offset + totalRead, bytesToRead);
                }
                catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut)
                {
                    throw new ConnectSilenceTimeOutException();
                }

                if (numberOfBytesRead <= 0)
                {
                    throw new ConnectNotConnectedException();
                }

                totalRead += numberOfBytesRead;
            }
        }

        public class ConnectSilenceTimeOutException : Exception
        { }

        public class ConnectNotConnectedException : Exception
        { }

        public byte[] ReceiveFourByte()
        {
            byte[] four = new byte[4];
            ReadExactBytes(four, 0, 4);
            return four;
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