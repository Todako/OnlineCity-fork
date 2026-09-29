using Model;
using OCUnion;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Util;

namespace Transfer
{
    /// <summary>
    /// Клієнт мережевої сесії.
    /// Забезпечує TCP-з'єднання з сервером OnlineCity, шифрування,
    /// стиснення та передачу типізованих пакетів даних.
    /// </summary>
    public class SessionClient
    {
        public const int DefaultPort = 19019;
        public const bool UseCryptoKeys = false;

        private static SessionClient _Get;
        public static SessionClient Get => _Get ?? (_Get = new SessionClient());

        public static void Recreate(SessionClient newClient)
        {
            _Get = newClient;
        }

        /// <summary>
        /// Об'єкт блокування для синхронізації фізичних операцій запису та читання через сокет.
        /// </summary>
        private readonly object LockObj = new object();

        /// <summary>
        /// Статичні буфери для службових сигналів (усувають виділення пам'яті в купі).
        /// </summary>
        private static readonly byte[] PingByte = new byte[1] { 0x00 };
        private static readonly byte[] CheckByte = new byte[1] { 0x01 };

        public Action<int, string, ModelStatus> OnPostingChatAfter;
        public Func<int, string, ModelStatus> OnPostingChatBefore;

        #region Стан сесії та підключення

        public static bool IsRelogin = false;

        public bool IsLogined
        {
            get => IsLogined_ || IsRelogin;
            private set => IsLogined_ = value;
        }
        private volatile bool IsLogined_ = false;
        public static DateTime LoginTime;

        public ConnectClient Client;
        private byte[] Key;
        private string KeyStr;
        public int ErrorCode;
        public string ErrorMessage;

        /// <summary>
        /// Розірвання поточного з'єднання та звільнення ресурсів сокета.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                IsLogined = false;
                Client?.Dispose();
            }
            catch { }

            Client = null;
            KeyStr = null;
            Key = null;
        }

        /// <summary>
        /// Встановлення з'єднання з сервером за адресою та портом.
        /// </summary>
        public bool Connect(string addr, int port = 0)
        {
            ErrorMessage = null;
            ErrorCode = 0;
            if (port == 0) port = DefaultPort;

            Disconnect();

            try
            {
                var crypto = new CryptoProvider();
                if (UseCryptoKeys) crypto.GenerateKeys();

                Client = new ConnectClient(addr, port);

                // Перший пакет: надсилання відкритого ключа або пінг-байта
                if (UseCryptoKeys)
                    Client.SendMessage(Encoding.UTF8.GetBytes(crypto.OpenKey));
                else
                    Client.SendMessage(PingByte);

                // Перша відповідь: отримання сесійного ключа від сервера
                var rc = Client.ReceiveBytes();
                if (UseCryptoKeys)
                    Key = crypto.Decrypt(rc);
                else
                    Key = rc;

                // Кешування рядка ключа для апаратного шифрування AES
                KeyStr = Key != null ? Encoding.ASCII.GetString(Key) : string.Empty;

                // Реєстрація клієнта у вартовому таймері підтримки з'єднання
                ConnectSaver.AddClient(Client, (cl) =>
                {
                    lock (LockObj)
                    {
                        cl.SendMessage(PingByte);
                        cl.ReceiveBytes();
                    }
                });

                return true;
            }
            catch (Exception e)
            {
                ErrorCode = -1;
                ErrorMessage = FormatException(e);
                ExceptionUtil.ExceptionLog(e, "Client Connect");
                return false;
            }
        }

        /// <summary>
        /// Службовий пінг для перевірки каналу зв'язку.
        /// </summary>
        public bool ServicePing()
        {
            if (Client == null) return false;

            try
            {
                byte[] rec;
                lock (LockObj)
                {
                    if (Client == null) return false;
                    ErrorCode = 0;
                    ErrorMessage = null;
                    Client.SendMessage(PingByte);
                    rec = Client.ReceiveBytes();
                }

                return rec != null && rec.Length == 1 && rec[0] == 0x00;
            }
            catch (Exception e)
            {
                ErrorCode = -1;
                ErrorMessage = FormatException(e);
                ExceptionUtil.ExceptionLog(e, "Client ServicePing");
                return false;
            }
        }

        /// <summary>
        /// Швидка перевірка наявності нових повідомлень або подій на сервері.
        /// </summary>
        public bool? ServiceCheck()
        {
            if (Client == null) return null;

            try
            {
                byte[] rec;
                lock (LockObj)
                {
                    if (Client == null) return null;
                    ErrorCode = 0;
                    ErrorMessage = null;
                    Client.SendMessage(CheckByte);
                    rec = Client.ReceiveBytes();
                }

                return rec != null && rec.Length == 1 && rec[0] == 0x01;
            }
            catch (Exception e)
            {
                ErrorCode = -1;
                ErrorMessage = FormatException(e);
                ExceptionUtil.ExceptionLog(e, "Client ServiceCheck");
                return null;
            }
        }

        /// <summary>
        /// Відправка та прийом пакета даних ModelContainer.
        /// ОПТИМІЗАЦІЯ: серіалізація, стиснення та дешифрування виконуються поза блокуванням LockObj.
        /// </summary>
        private ModelContainer Trans(ModelContainer sendObj)
        {
            ErrorCode = 0;
            ErrorMessage = null;

            var sw = Stopwatch.StartNew();

            // 1. Серіалізація та шифрування поза блокуванням сокета
            var ob = GZip.ZipObjByte(sendObj);
            var send = CryptoProvider.SymmetricEncrypt(ob, KeyStr);

            if (send.Length > 1024 * 512)
            {
                Loger.Log($"Client Network toS {send.Length} unzip {GZip.LastSizeObj}");
            }

            long timeSerialize = sw.ElapsedMilliseconds;
            long timeWaitLock = 0;
            long timeSocketIO = 0;

            // 2. Блокування утримується суто на час передачі/прийому байтів мережевим адаптером
            byte[] rec;
            lock (LockObj)
            {
                if (Client == null)
                {
                    throw new IOException("Клієнт не підключений до сервера.");
                }

                timeWaitLock = sw.ElapsedMilliseconds;
                Client.SendMessage(send);
                rec = Client.ReceiveBytes();
                timeSocketIO = sw.ElapsedMilliseconds;
            }

            if (rec == null || rec.Length == 0)
            {
                throw new IOException("Сервер розірвав з'єднання або надіслав порожню відповідь.");
            }

            // 3. Дешифрування та десеріалізація отриманого пакета поза блокуванням
            var rec2 = CryptoProvider.SymmetricDecrypt(rec, KeyStr);
            if (rec2 == null || rec2.Length == 0)
            {
                throw new IOException("Помилка дешифрування отриманого пакета від сервера.");
            }
            long timeDecrypt = sw.ElapsedMilliseconds;

            var res = (ModelContainer)GZip.UnzipObjByte(rec2);
            if (res == null)
            {
                throw new IOException("Помилка десеріалізації об'єкта відповіді сервера.");
            }
            long timeTotal = sw.ElapsedMilliseconds;

            if (rec.Length > 1024 * 512)
            {
                Loger.Log($"Client Network fromS {rec.Length} unzip {GZip.LastSizeObj}");
            }

            if (timeTotal > 900)
            {
                Loger.Log($"Client Network total {timeTotal}ms: " +
                    $"Serialize {timeSerialize}ms, " +
                    $"WaitLock {timeWaitLock - timeSerialize}ms, " +
                    $"SocketIO {timeSocketIO - timeWaitLock}ms, " +
                    $"Decrypt {timeDecrypt - timeSocketIO}ms, " +
                    $"Deserialize {timeTotal - timeDecrypt}ms");
            }

            return res;
        }

        /// <summary>
        /// Відправка об'єкта із перевіркою кодів вихідного та вхідного пакетів.
        /// </summary>
        protected T TransObject<T>(object objOut, int typeOut, int typeIn)
            where T : class
        {
            try
            {
                var pack = new ModelContainer
                {
                    TypePacket = typeOut,
                    Packet = objOut
                };
                var res = Trans(pack);
                if (res == null) return null;

                var stat = res.Packet as T;
                if (res.TypePacket != typeIn || stat == null)
                {
                    throw new ApplicationException($"Невідома відповідь сервера TransObject({typeOut} -> {typeIn}) код: {res.TypePacket}, тип: "
                        + (res.Packet == null ? "null" : res.Packet.GetType().Name));
                }
                return stat;
            }
            catch (Exception e)
            {
                ErrorCode = -1;
                ErrorMessage = FormatException(e);
                ExceptionUtil.ExceptionLog(e, "Client TransObject");
                return null;
            }
        }

        public T TransObject2<T>(object objOut, PackageType typeOut, PackageType typeIn)
            where T : class
        {
            return TransObject<T>(objOut, (int)typeOut, (int)typeIn);
        }

        private bool TransStatus(object objOut, int typeOut, int typeIn)
        {
            var stat = TransObject<ModelStatus>(objOut, typeOut, typeIn);

            if (stat != null && stat.Status != 0)
            {
                ErrorCode = stat.Status;
                ErrorMessage = stat.Message;
                return false;
            }
            return stat != null;
        }

        private static string FormatException(Exception e)
        {
            return e.InnerException == null ? e.Message : e.Message + " -> " + e.InnerException.Message;
        }

        #endregion

        #region Клієнтські запити API

        public bool Registration(string login, string pass, string email, string discord)
        {
            var packet = new ModelLogin { Login = login, Pass = pass, Email = email, DiscordUserName = discord, Version = MainHelper.VersionNum };
            var good = TransStatus(packet, (int)PackageType.Request1Register, (int)PackageType.Response2Register);

            if (good)
            {
                IsLogined = true;
                LoginTime = DateTime.UtcNow;
            }
            return good;
        }

        public bool Login(string login, string pass, string email, string discord = null)
        {
            var packet = new ModelLogin { Login = login, Pass = pass, Email = email, DiscordUserName = discord, Version = MainHelper.VersionNum };
            var good = TransStatus(packet, (int)PackageType.Request3Login, (int)PackageType.Response4Login);

            if (good)
            {
                IsLogined = true;
                LoginTime = DateTime.UtcNow;
            }
            return good;
        }

        public bool Reconnect(string login, string key, string email)
        {
            var packet = new ModelLogin { Login = login, KeyReconnect = key, Email = email };
            var good = TransStatus(packet, (int)PackageType.Request3Login, (int)PackageType.Response4Login);

            if (good) IsLogined = true;
            return good;
        }

        public ModelInfo GetInfo(ServerInfoType serverInfoType)
        {
            Loger.Log("Client GetInfo " + serverInfoType);
            var packet = new ModelInt { Value = (int)serverInfoType };
            return TransObject<ModelInfo>(packet, (int)PackageType.Request5UserInfo, (int)PackageType.Response6UserInfo);
        }

        public ModelPlayToClient PlayInfo(ModelPlayToServer info)
        {
            return TransObject<ModelPlayToClient>(info, (int)PackageType.Request11, (int)PackageType.Response12);
        }

        public ModelUpdateChat UpdateChat(ModelUpdateTime modelUpdate)
        {
            if (Loger.Enable)
            {
                Loger.Log("Client UpdateChat " + modelUpdate.Time.ToGoodUtcString(), Loger.LogLevel.DEBUG);
            }

            return TransObject<ModelUpdateChat>(modelUpdate, (int)PackageType.Request17, (int)PackageType.Response18);
        }

        public ModelStatus PostingChat(int chatId, string msg, bool raw = false)
        {
            Loger.Log("Client PostingChat " + chatId + ", " + msg);

            if (!raw && OnPostingChatBefore != null)
            {
                var cancel = OnPostingChatBefore(chatId, msg);
                if (cancel != null) return cancel;
            }

            var packet = new ModelPostingChat { IdChat = chatId, Message = msg };
            var stat = TransObject<ModelStatus>(packet, (int)PackageType.Request19PostingChat, (int)PackageType.Response20PostingChat);

            ErrorCode = stat?.Status ?? 0;
            ErrorMessage = stat?.Message;

            if (!raw && OnPostingChatAfter != null) OnPostingChatAfter(chatId, msg, stat);

            return stat;
        }

        public Player GetPlayerByToken(Guid guidToken)
        {
            return TransObject2<Player>(guidToken, PackageType.RequestPlayerByToken, PackageType.ResponsePlayerByToken);
        }

        public ModelGameServerInfo GetGameServerInfo()
        {
            Loger.Log("Client Get WorldObject From Server");
            var packet = new ModelInt { Value = 1 };
            return TransObject<ModelGameServerInfo>(packet, (int)PackageType.Request43WObjectUpdate, (int)PackageType.Response44WObjectUpdate);
        }

        public List<string> AnyLoad(List<long> hashs)
        {
            var packet = new ModelAnyLoad { Hashs = hashs };
            var stat = TransObject<ModelAnyLoad>(packet, (int)PackageType.Request45AnyLoad, (int)PackageType.Response46AnyLoad);
            return stat?.Datas;
        }

        public ModelFileSharing FileSharingDownload(FileSharingCategory category, string name)
        {
            var packet = new ModelFileSharing { Category = category, Name = name };
            return TransObject<ModelFileSharing>(packet, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
        }

        public ModelFileSharing FileSharingDownload(ModelFileSharing fileSharing)
        {
            var packet = new ModelFileSharing { Category = fileSharing.Category, Name = fileSharing.Name, Hash = fileSharing.Hash };
            var stat = TransObject<ModelFileSharing>(packet, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
            return stat?.Data == null ? fileSharing : stat;
        }

        public List<ModelFileSharing> FileSharingDownloadOnlyCheck(List<ModelFileSharing> fileSharing)
        {
            return TransObject<List<ModelFileSharing>>(fileSharing, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
        }

        public ModelFileSharing FileSharingUpload(FileSharingCategory category, string name, byte[] data)
        {
            var packet = new ModelFileSharing { Category = category, Name = name, Data = data };
            return TransObject<ModelFileSharing>(packet, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
        }

        public ModelPlayerInfoExtended GetPlayerInfoExtended(string playerName)
        {
            Loger.Log("Client GetPlayerInfoExtended " + playerName);
            var packet = new ModelName { Value = playerName };
            return TransObject<ModelPlayerInfoExtended>(packet, (int)PackageType.Request55PlayerInfoExtended, (int)PackageType.Response56PlayerInfoExtended);
        }

        #endregion
    }
}