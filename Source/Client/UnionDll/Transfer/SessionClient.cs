using Model;
using OCUnion;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Transfer;
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

        /// <summary>
        /// Об'єкт блокування для синхронізації операцій запису та читання через сокет.
        /// </summary>
        private readonly object LockObj = new object();

        /// <summary>
        /// Статичні буфери для службових сигналів (усувають постійні дрібні алокації в купі).
        /// </summary>
        private static readonly byte[] PingByte = new byte[1] { 0x00 };
        private static readonly byte[] CheckByte = new byte[1] { 0x01 };

        public Action<int, string, ModelStatus> OnPostingChatAfter;
        public Func<int, string, ModelStatus> OnPostingChatBefore;

        #region Стан сесії та підключення

        /// <summary>
        /// Ознака активного процесу перепідключення до сервера.
        /// </summary>
        public static bool IsRelogin = false;

        public bool IsLogined
        {
            get { return IsLogined_ || IsRelogin; }
            private set { IsLogined_ = value; }
        }
        private volatile bool IsLogined_ = false;
        public static DateTime LoginTime;

        public ConnectClient Client;
        private byte[] Key;
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
                if (Client != null) Client.Dispose();
            }
            catch
            {
            }

            Client = null;
        }

        /// <summary>
        /// Встановлення з'єднання з сервером за адресою та портом.
        /// Ініціалізує первинне узгодження ключів шифрування та фоновий вартовий таймер підключення.
        /// </summary>
        public bool Connect(string addr, int port = 0)
        {
            ErrorMessage = null;
            ErrorCode = 0;
            if (port == 0) port = DefaultPort;
            try
            {
                IsLogined = false;
                if (Client != null) Client.Dispose();
            }
            catch { }

            try
            {
                var crypto = new CryptoProvider();
                if (UseCryptoKeys) crypto.GenerateKeys();

                Client = new ConnectClient(addr, port);

                // Перший пакет: передача відкритого ключа або нульового байта
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

                // Реєстрація клієнта у фоновому вартовому таймері для підтримки активності сокета
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
                ErrorMessage = e.Message + (e.InnerException == null ? "" : " -> " + e.InnerException.Message);
                ExceptionUtil.ExceptionLog(e, "Client");
                return false;
            }
        }

        /// <summary>
        /// Службовий пінг для перевірки працездатності каналу зв'язку.
        /// </summary>
        public bool ServicePing()
        {
            try
            {
                byte[] rec;
                lock (LockObj)
                {
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
                ErrorMessage = e.Message + (e.InnerException == null ? "" : " -> " + e.InnerException.Message);
                ExceptionUtil.ExceptionLog(e, "Client ServicePing ");
                return false;
            }
        }

        /// <summary>
        /// Швидка перевірка наявності нових повідомлень або подій на сервері.
        /// </summary>
        public bool? ServiceCheck()
        {
            try
            {
                byte[] rec;
                lock (LockObj)
                {
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
                ErrorMessage = e.Message + (e.InnerException == null ? "" : " -> " + e.InnerException.Message);
                ExceptionUtil.ExceptionLog(e, "Client ServiceCheck ");
                return null;
            }
        }

        /// <summary>
        /// Відправка та прийом пакета даних типу ModelContainer.
        /// ОПТИМІЗАЦІЯ: важка серіалізація, GZip-стиснення та дешифрування виконуються поза блокуванням LockObj.
        /// Сокет блокується виключно на час передачі сирих байтів через мережу.
        /// </summary>
        private ModelContainer Trans(ModelContainer sendObj)
        {
            ErrorCode = 0;
            ErrorMessage = null;

            var time1 = DateTime.UtcNow;

            // 1. Серіалізація та шифрування (CPU-навантаження) поза блокуванням LockObj
            var ob = GZip.ZipObjByte(sendObj);
            var send = CryptoProvider.SymmetricEncrypt(ob, Key);

            if (send.Length > 1024 * 512)
            {
                Loger.Log($"Client Network toS {send.Length} unzip {GZip.LastSizeObj} ");
            }

            var time2 = DateTime.UtcNow;

            // 2. Блокування утримується виключно під час фізичного I/O обміну через сокет
            byte[] rec;
            DateTime time3, time4;
            lock (LockObj)
            {
                time3 = DateTime.UtcNow;
                Client.SendMessage(send);
                rec = Client.ReceiveBytes();
                time4 = DateTime.UtcNow;
            }

            if (rec == null || rec.Length == 0)
            {
                throw new IOException("Сервер розірвав з'єднання або надіслав порожню відповідь.");
            }

            var time5 = DateTime.UtcNow;

            // 3. Дешифрування та десеріалізація отриманого пакета поза блокуванням LockObj
            var rec2 = CryptoProvider.SymmetricDecrypt(rec, Key);
            var time6 = DateTime.UtcNow;

            var res = (ModelContainer)GZip.UnzipObjByte(rec2);
            var time7 = DateTime.UtcNow;

            if (rec.Length > 1024 * 512)
            {
                Loger.Log($"Client Network fromS {rec.Length} unzip {GZip.LastSizeObj} ");
            }

            var totalMs = (time7 - time1).TotalMilliseconds;
            if (totalMs > 900)
            {
                Loger.Log($"Client Network total {totalMs:F0}ms: " +
                    $"Serialize {(time2 - time1).TotalMilliseconds:F0}ms, " +
                    $"WaitLock {(time3 - time2).TotalMilliseconds:F0}ms, " +
                    $"SocketIO {(time4 - time3).TotalMilliseconds:F0}ms, " +
                    $"Decrypt {(time6 - time5).TotalMilliseconds:F0}ms, " +
                    $"Deserialize {(time7 - time6).TotalMilliseconds:F0}ms");
            }

            return res;
        }

        /// <summary>
        /// Відправка об'єкта із зазначенням типів вихідного та вхідного пакетів.
        /// </summary>
        protected T TransObject<T>(object objOut, int typeOut, int typeIn)
            where T : class
        {
            try
            {
                var pack = new ModelContainer()
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
                ErrorMessage = e.Message + (e.InnerException == null ? "" : " -> " + e.InnerException.Message);
                ExceptionUtil.ExceptionLog(e, "Client");
                return null;
            }
        }

        public T TransObject2<T>(object objOut, PackageType typeOut, PackageType typeIn)
            where T : class
        {
            return TransObject<T>(objOut, (int)typeOut, (int)typeIn);
        }

        /// <summary>
        /// Відправка об'єкта з очікуванням успішного статусу ModelStatus (Status == 0).
        /// </summary>
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

        #endregion

        public bool Registration(string login, string pass, string email, string discord)
        {
            var packet = new ModelLogin() { Login = login, Pass = pass, Email = email, DiscordUserName = discord, Version = MainHelper.VersionNum };
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
            var packet = new ModelLogin() { Login = login, Pass = pass, Email = email, DiscordUserName = discord, Version = MainHelper.VersionNum };
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
            var packet = new ModelLogin() { Login = login, KeyReconnect = key, Email = email };
            var good = TransStatus(packet, (int)PackageType.Request3Login, (int)PackageType.Response4Login);

            if (good) IsLogined = true;
            return good;
        }

        public ModelInfo GetInfo(ServerInfoType serverInfoType)
        {
            Loger.Log("Client GetInfo " + serverInfoType.ToString());
            var packet = new ModelInt() { Value = (int)serverInfoType };
            var stat = TransObject<ModelInfo>(packet, (int)PackageType.Request5UserInfo, (int)PackageType.Response6UserInfo);
            return stat;
        }

        public ModelPlayToClient PlayInfo(ModelPlayToServer info)
        {
            var stat = TransObject<ModelPlayToClient>(info, (int)PackageType.Request11, (int)PackageType.Response12);
            return stat;
        }

        public ModelUpdateChat UpdateChat(ModelUpdateTime modelUpdate)
        {
            Loger.Log("Client UpdateChat " + modelUpdate.Time.ToGoodUtcString());
            var packet = modelUpdate;
            var stat = TransObject<ModelUpdateChat>(packet, (int)PackageType.Request17, (int)PackageType.Response18);
            return stat;
        }

        public ModelStatus PostingChat(int chatId, string msg, bool raw = false)
        {
            Loger.Log("Client PostingChat " + chatId.ToString() + ", " + msg);

            if (!raw && OnPostingChatBefore != null)
            {
                var cancel = OnPostingChatBefore(chatId, msg);
                if (cancel != null) return cancel;
            }

            var packet = new ModelPostingChat() { IdChat = chatId, Message = msg };
            var stat = TransObject<ModelStatus>(packet, (int)PackageType.Request19PostingChat, (int)PackageType.Response20PostingChat);

            ErrorCode = stat?.Status ?? 0;
            ErrorMessage = stat?.Message;

            if (!raw && OnPostingChatAfter != null) OnPostingChatAfter(chatId, msg, stat);

            return stat;
        }

        public Player GetPlayerByToken(Guid guidToken)
        {
            var stat = TransObject2<Player>(guidToken, PackageType.RequestPlayerByToken, PackageType.ResponsePlayerByToken);
            return stat;
        }

        public ModelGameServerInfo GetGameServerInfo()
        {
            Loger.Log("Client Get WorldObject From Server");
            var packet = new ModelInt() { Value = 1 };
            var stat = TransObject<ModelGameServerInfo>(packet, (int)PackageType.Request43WObjectUpdate, (int)PackageType.Response44WObjectUpdate);
            return stat;
        }

        public List<string> AnyLoad(List<long> hashs)
        {
            var packet = new ModelAnyLoad() { Hashs = hashs };
            var stat = TransObject<ModelAnyLoad>(packet, (int)PackageType.Request45AnyLoad, (int)PackageType.Response46AnyLoad);
            return (stat as ModelAnyLoad)?.Datas;
        }

        public ModelFileSharing FileSharingDownload(FileSharingCategory category, string name)
        {
            var packet = new ModelFileSharing() { Category = category, Name = name };
            var stat = TransObject<ModelFileSharing>(packet, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
            return stat;
        }

        /// <summary>
        /// Оновлює дані у fileSharing, якщо за вказаним Name змінився Hash. Інакше повертає наявний fileSharing.
        /// </summary>
        public ModelFileSharing FileSharingDownload(ModelFileSharing fileSharing)
        {
            var packet = new ModelFileSharing() { Category = fileSharing.Category, Name = fileSharing.Name, Hash = fileSharing.Hash };
            var stat = TransObject<ModelFileSharing>(packet, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
            return (stat?.Data == null ? fileSharing : stat);
        }

        public List<ModelFileSharing> FileSharingDownloadOnlyCheck(List<ModelFileSharing> fileSharing)
        {
            var stat = TransObject<List<ModelFileSharing>>(fileSharing, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
            return stat;
        }

        public ModelFileSharing FileSharingUpload(FileSharingCategory category, string name, byte[] data)
        {
            var packet = new ModelFileSharing() { Category = category, Name = name, Data = data };
            var stat = TransObject<ModelFileSharing>(packet, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
            return stat;
        }

        public ModelPlayerInfoExtended GetPlayerInfoExtended(string playerName)
        {
            Loger.Log("Client GetPlayerInfoExtended " + playerName);
            var packet = new ModelName() { Value = playerName };
            var stat = TransObject<ModelPlayerInfoExtended>(packet, (int)PackageType.Request55PlayerInfoExtended, (int)PackageType.Response56PlayerInfoExtended);
            return stat;
        }
    }
}