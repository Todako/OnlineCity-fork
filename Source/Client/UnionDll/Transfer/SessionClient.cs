using OCUnion;
using System;
using System.Collections.Generic;
using System.Text;
using Transfer;
using Util;
using Model;
using OCUnion.Transfer.Model;
using OCUnion.Transfer;

namespace Transfer
{
    public class SessionClient
    {
        public const int DefaultPort = 19019; // :) https://www.random.org/integers/?num=1&min=5001&max=49151&col=5&base=10&format=html&rnd=new
        public const bool UseCryptoKeys = false;
        private readonly object LockObj = new object();

        // Статичні буфери для усунення зайвих алокацій щосекунди
        private static readonly byte[] KeepAlivePayload = new byte[1] { 0x00 };
        private static readonly byte[] ServiceCheckPayload = new byte[1] { 0x01 };

        public Action<int, string, ModelStatus> OnPostingChatAfter;
        public Func<int, string, ModelStatus> OnPostingChatBefore;

        #region Session State
        /// <summary>
        /// Підтримуємо статус відкритого з'єднання, поки намагаємося перепідключитися, щоб не розблокувати вразливості звичайної гри
        /// </summary>
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
        public int ErrorCode;
        public string ErrorMessage;

        public void Disconnect()
        {
            try
            {
                IsLogined = false;
                Client?.Dispose();
            }
            catch { }
            finally
            {
                Client = null;
            }
        }

        public bool Connect(string addr, int port = 0)
        {
            ErrorMessage = null;
            ErrorCode = 0;
            if (port == 0) port = DefaultPort;

            try
            {
                IsLogined = false;
                Client?.Dispose();
            }
            catch { }

            try
            {
                // Генеруємо випадкову пару ключів: KClose-KOpen (КЗакр-КВідкр)
                var crypto = new CryptoProvider();
                if (UseCryptoKeys) crypto.GenerateKeys();

                Client = new ConnectClient(addr, port);

                // Суворо перший пакет: передаємо серверу відкритий ключ (КВідкр)
                if (UseCryptoKeys)
                    Client.SendMessage(Encoding.UTF8.GetBytes(crypto.OpenKey));
                else
                    Client.SendMessage(KeepAlivePayload);

                // Суворо перша відповідь: передаємо клієнту КВідкр(Сесія)
                var rc = Client.ReceiveBytes();
                Key = UseCryptoKeys ? crypto.Decrypt(rc) : rc;

                // Запускаємо таймер, який фоново підтримує відкрите з'єднання під час простою
                ConnectSaver.AddClient(Client, (cl) =>
                {
                    lock (LockObj)
                    {
                        cl.SendMessage(KeepAlivePayload);
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
        /// Пінг
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
                    Client.SendMessage(KeepAlivePayload);
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
        /// Перевірка наявності нових даних на сервері, використовується лише для чату
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
                    Client.SendMessage(ServiceCheckPayload);
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
        /// Оптимізована передача контейнера: стиснення та розпакування винесені з блокування сокета
        /// </summary>
        private ModelContainer Trans(ModelContainer sendObj)
        {
            ErrorCode = 0;
            ErrorMessage = null;

            var time1 = DateTime.UtcNow;

            // 1. Серіалізація та шифрування у викликаючому потоці без блокування інших
            var ob = GZip.ZipObjByte(sendObj);
            var send = CryptoProvider.SymmetricEncrypt(ob, Key);

            var time2 = DateTime.UtcNow;
            byte[] rec;

            // 2. Блокування утримується виключно на час передачі байтів через сокет
            lock (LockObj)
            {
                Client.SendMessage(send);
                rec = Client.ReceiveBytes();
            }

            var time4 = DateTime.UtcNow;

            // 3. Дешифрування та десеріалізація виконуються паралельно поза блокуванням
            var rec2 = CryptoProvider.SymmetricDecrypt(rec, Key);
            var time5 = DateTime.UtcNow;

            var res = (ModelContainer)GZip.UnzipObjByte(rec2);
            var time6 = DateTime.UtcNow;

            if ((time6 - time1).TotalMilliseconds > 900)
            {
                Loger.Log(string.Concat(
                    "Client Network timeSerialize ", (time2 - time1).TotalMilliseconds.ToString("F1"),
                    " timeNet ", (time4 - time2).TotalMilliseconds.ToString("F1"),
                    " timeDecrypt ", (time5 - time4).TotalMilliseconds.ToString("F1"),
                    " timeDeserialize ", (time6 - time5).TotalMilliseconds.ToString("F1")
                ));
            }

            return res;
        }

        /// <summary>
        /// Передаємо об'єкт із зазначенням номера типу
        /// </summary>
        protected T TransObject<T>(object objOut, int typeOut, int typeIn) where T : class
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

                if (res.TypePacket != typeIn || !(res.Packet is T stat))
                {
                    throw new ApplicationException($"Unknown server error TransObject({typeOut} -> {typeIn}) response: {res.TypePacket} "
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

        public T TransObject2<T>(object objOut, PackageType typeOut, PackageType typeIn) where T : class
        {
            return TransObject<T>(objOut, (int)typeOut, (int)typeIn);
        }

        /// <summary>
        /// Передаємо об'єкт із зазначенням номера типу.
        /// Відповідь має надійти як ModelStatus зі статусом == 0 і вказаним номером типу.
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
            var packet = new ModelInt { Value = (int)serverInfoType };
            return TransObject<ModelInfo>(packet, (int)PackageType.Request5UserInfo, (int)PackageType.Response6UserInfo);
        }

        public ModelPlayToClient PlayInfo(ModelPlayToServer info)
        {
            return TransObject<ModelPlayToClient>(info, (int)PackageType.Request11, (int)PackageType.Response12);
        }

        public ModelUpdateChat UpdateChat(ModelUpdateTime modelUpdate)
        {
            return TransObject<ModelUpdateChat>(modelUpdate, (int)PackageType.Request17, (int)PackageType.Response18);
        }

        public ModelStatus PostingChat(int chatId, string msg, bool raw = false)
        {
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

        // Об'єкти світу (WIP)
        public ModelGameServerInfo GetGameServerInfo()
        {
            var packet = new ModelInt { Value = 1 };
            return TransObject<ModelGameServerInfo>(packet, (int)PackageType.Request43WObjectUpdate, (int)PackageType.Response44WObjectUpdate);
        }

        public List<string> AnyLoad(List<long> hashs)
        {
            var packet = new ModelAnyLoad { Hashs = hashs };
            var stat = TransObject<ModelAnyLoad>(packet, (int)PackageType.Request45AnyLoad, (int)PackageType.Response46AnyLoad);
            return (stat as ModelAnyLoad)?.Datas;
        }

        public ModelFileSharing FileSharingDownload(FileSharingCategory category, string name)
        {
            var packet = new ModelFileSharing { Category = category, Name = name };
            return TransObject<ModelFileSharing>(packet, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
        }

        /// <summary>
        /// Оновлює дані у fileSharing, якщо за вказаним Name змінився Hash. Інакше повертає fileSharing.
        /// Поле Data у fileSharing ігнорується.
        /// </summary>
        public ModelFileSharing FileSharingDownload(ModelFileSharing fileSharing)
        {
            var packet = new ModelFileSharing { Category = fileSharing.Category, Name = fileSharing.Name, Hash = fileSharing.Hash };
            var stat = TransObject<ModelFileSharing>(packet, (int)PackageType.Request49FileSharing, (int)PackageType.Response50FileSharing);
            return (stat?.Data == null ? fileSharing : stat);
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
            var packet = new ModelName { Value = playerName };
            return TransObject<ModelPlayerInfoExtended>(packet, (int)PackageType.Request55PlayerInfoExtended, (int)PackageType.Response56PlayerInfoExtended);
        }
    }
}
