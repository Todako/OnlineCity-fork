using Model;
using OCUnion;
using OCUnion.Transfer;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using Transfer;
using Util;

namespace ServerOnlineCity
{
    /// <summary>
    /// Серверна сесія клієнта.
    /// Відповідає за прийом, перевірку, дешифрування, виконання команд
    /// та зворотне надсилання результатів клієнту гри.
    /// </summary>
    public class SessionServer : IDisposable
    {
        public bool IsAPI = false;
        public bool IsActive = true;
        private ConnectClient Client;
        private byte[] Key;
        private string KeyStr;

        private static readonly Encoding KeyEncoding = Encoding.GetEncoding(1252);
        private static readonly Encoding JsonEncoding = Encoding.UTF8;
        private static readonly byte[] KeySaltBytes = KeyEncoding.GetBytes("g08Р·dfgиА▀ЫЮЁрЫГxч<2en]*♫7ПМпёf#hю,>√^p147&$`tgjРмБ^g9~hjЮf%#h");
        private static readonly byte[] HttpOkHeaderBytes = JsonEncoding.GetBytes("HTTP/1.0 200 OK\r\nConnection: close\r\n\r\n");

        private readonly CryptoProvider cryptoHash = new CryptoProvider();
        private Service Worker;
        private DateTime ServiceCheckTime;

        // Статичні відповіді на службові пінг/чек пакети (0 байт GC на кожну перевірку)
        private static readonly byte[] PingResponse = new byte[1] { 0x00 };
        private static readonly byte[] CheckResponseTrue = new byte[1] { 0x01 };
        private static readonly byte[] CheckResponseFalse = new byte[1] { 0x00 };

        public void Dispose()
        {
            IsActive = false;
            try
            {
                Client?.Dispose();
            }
            catch { }
        }

        public string GetNameWhoConnect()
        {
            try
            {
                return Worker?.Context?.Player?.Public?.Login ?? "";
            }
            catch
            {
                return "";
            }
        }

        public ServiceContext GetContext()
        {
            try
            {
                return Worker?.Context;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Потокобезпечна генерація унікального сесійного ключа без зайвих перерозподілів масивів.
        /// </summary>
        private void SetKey()
        {
            var rnd = new Random();
            int rndLen = rnd.Next(400, 600);
            var k = new byte[rndLen + KeySaltBytes.Length];

            for (int i = 0; i < rndLen; i++)
            {
                k[i] = (byte)(rnd.Next(0, 128) + rnd.Next(0, 128));
            }

            Array.Copy(KeySaltBytes, 0, k, rndLen, KeySaltBytes.Length);

            Key = cryptoHash.GetHash(k);
            KeyStr = Encoding.ASCII.GetString(Key);
        }

        public void DoServiceJson(ConnectClient c)
        {
            var receiveReady = new ManualResetEventSlim(false);

            c.ReceiveAllByte((client, requestRaw) =>
            {
                try
                {
                    var data = new Dictionary<string, byte[]>();
                    var request = JsonEncoding.GetString(requestRaw);
                    int ii = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    int iiBoundary = request.IndexOf("boundary=", StringComparison.Ordinal);
                    string isBoundary = null;

                    if (iiBoundary > 0 && iiBoundary < ii)
                    {
                        iiBoundary += "boundary=".Length;
                        isBoundary = "\r\n--" + request.Substring(iiBoundary, request.IndexOf("\r\n", iiBoundary, StringComparison.Ordinal) - iiBoundary);
                    }

                    if (isBoundary != null)
                    {
                        var requestRawCode = Encoding.ASCII.GetString(requestRaw);
                        int rawCodeIndex = 0;
                        var listBoundary = request.Split(new string[] { isBoundary }, StringSplitOptions.RemoveEmptyEntries);
                        request = null;

                        for (int i = 1; i < listBoundary.Length; i++)
                        {
                            rawCodeIndex = requestRawCode.IndexOf(isBoundary, rawCodeIndex, StringComparison.Ordinal);
                            var item = listBoundary[i];
                            int itemi = item.IndexOf("name=\"", StringComparison.Ordinal);
                            if (itemi > 0)
                            {
                                itemi += "name=\"".Length;
                                var name = item.Substring(itemi, item.IndexOf("\"", itemi, StringComparison.Ordinal) - itemi);

                                rawCodeIndex = requestRawCode.IndexOf("\r\n\r\n", rawCodeIndex, StringComparison.Ordinal) + 4;
                                if (rawCodeIndex >= 4)
                                {
                                    int rawCodeIndexNext = requestRawCode.IndexOf(isBoundary, rawCodeIndex, StringComparison.Ordinal);
                                    if (rawCodeIndexNext < 0) rawCodeIndexNext = requestRaw.Length;

                                    int len = rawCodeIndexNext - rawCodeIndex;
                                    var content = new byte[len];
                                    Array.Copy(requestRaw, rawCodeIndex, content, 0, len);

                                    if (request != null) request += ", ";
                                    if (content.Length > 1000)
                                    {
                                        data[name] = content;
                                    }
                                    else
                                    {
                                        request += Environment.NewLine + $"\"{name}\":\"{JsonEncoding.GetString(content).Replace("\"", "'")}\"";
                                    }
                                }
                            }
                        }
                        request = "{" + request + Environment.NewLine + "}";
                    }
                    else
                    {
                        if (ii > 0 && request.Length - ii > 5)
                        {
                            request = request.Substring(ii + 4).Trim();
                        }
                        else
                        {
                            request = null;
                        }
                    }

                    if (request != null)
                    {
                        byte[] sendBytes;
                        var send = Service.GetPackageJson(request, data);

                        if (send is byte[] body)
                        {
                            sendBytes = new byte[HttpOkHeaderBytes.Length + body.Length];
                            Array.Copy(HttpOkHeaderBytes, 0, sendBytes, 0, HttpOkHeaderBytes.Length);
                            Array.Copy(body, 0, sendBytes, HttpOkHeaderBytes.Length, body.Length);
                        }
                        else
                        {
                            var sendHTTP = "HTTP/1.0 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nConnection: close\r\n\r\n"
                                + send.ToString();
                            sendBytes = JsonEncoding.GetBytes(sendHTTP);
                        }

                        client.SendAllByte(sendBytes);

                        if (!IsStatusRequest(request))
                        {
                            Loger.Log("DoServiceJson Request: " + request + Environment.NewLine + (send is byte[]? sendBytes.Length.ToString() : send.ToString()), Loger.LogLevel.INFO);
                        }
                    }
                }
                catch (Exception ext)
                {
                    Loger.Log("DoServiceJson Exception: " + ext.ToString(), Loger.LogLevel.ERROR);
                }
                finally
                {
                    receiveReady.Set();
                }
            }, 1024 * 1024 * 2);

            receiveReady.Wait(2000);
            receiveReady.Dispose();
        }

        private static bool IsStatusRequest(string request)
        {
            if (string.IsNullOrEmpty(request)) return false;
            return request.IndexOf("\"q\":\"s\"", StringComparison.OrdinalIgnoreCase) >= 0
                || request.IndexOf("\"q\": \"s\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Do(ConnectClient client, Action<Action<SessionServer>> allSessionAction)
        {
            try
            {
                Client = client;

                // Перевірка на REST API запит (перші 4 байти - 'POST')
                var firstByte = Client.ReceiveFourByte();
                if (firstByte?.Length == 4
                    && firstByte[0] == 80
                    && firstByte[1] == 79
                    && firstByte[2] == 83
                    && firstByte[3] == 84)
                {
                    IsAPI = true;
                    DoServiceJson(client);
                    return;
                }

                Loger.Log("Server ReceiveBytes1");

                // Початкове рукостискання
                var rc = Client.ReceiveBytes(firstByte);
                var crypto = new CryptoProvider();
                if (SessionClient.UseCryptoKeys) crypto.OpenKey = Encoding.UTF8.GetString(rc);

                SetKey();
                Loger.Log("Server SendMessage1");
                if (SessionClient.UseCryptoKeys)
                    Client.SendMessage(crypto.Encrypt(Key));
                else
                    Client.SendMessage(Key);

                var context = new ServiceContext();
                context.AddrIP = ((IPEndPoint)client.Client.Client.RemoteEndPoint).Address.ToString();
                context.AllSessionAction = allSessionAction;
                Worker = new Service(context);

                // Головний робочий цикл сесії
                while (IsActive)
                {
                    var rec = Client.ReceiveBytes();
                    if (!IsActive) break;

                    // Обробка швидких службових сигналів без витрат на дешифрування
                    if (rec.Length == 1)
                    {
                        if (rec[0] == 0x00)
                        {
                            Client.SendMessage(PingResponse);
                        }
                        else if (rec[0] == 0x01)
                        {
                            bool exists = ServiceCheck();
                            Client.SendMessage(exists ? CheckResponseTrue : CheckResponseFalse);
                        }
                        continue;
                    }

                    long startTimestamp = Stopwatch.GetTimestamp();

                    // Дешифрування та розпакування отриманого пакета
                    var rec2 = CryptoProvider.SymmetricDecrypt(rec, KeyStr);
                    var recObj = (ModelContainer)GZip.UnzipObjByte(rec2);

                    if (rec.Length > 1024 * 512)
                    {
                        Loger.Log($"Server Network fromC {rec.Length} unzip {GZip.LastSizeObj}");
                    }
                    long timeDeserializeTicks = Stopwatch.GetTimestamp();

                    // Обробка бізнес-логіки пакета в службі Service
                    ModelContainer sendObj;
                    try
                    {
                        sendObj = Worker.GetPackage(recObj);
                        if (!IsActive) break;
                    }
                    catch (Exception ext)
                    {
                        Loger.Log("Exception GetPackage: " + ext.ToString(), Loger.LogLevel.ERROR);
                        sendObj = null;
                    }

                    if (sendObj == null)
                    {
                        sendObj = new ModelContainer { TypePacket = 0 };
                    }
                    long timeWorkerTicks = Stopwatch.GetTimestamp();

                    // Стиснення та шифрування пакета-відповіді
                    var ob = GZip.ZipObjByte(sendObj);
                    var send = CryptoProvider.SymmetricEncrypt(ob, KeyStr);

                    if (send.Length > 1024 * 512)
                    {
                        Loger.Log($"Server Network toC {send.Length} unzip {GZip.LastSizeObj}");
                    }
                    long timeSerializeTicks = Stopwatch.GetTimestamp();

                    // Відправка клієнту
                    Client.SendMessage(send);
                    long timeSendTicks = Stopwatch.GetTimestamp();

                    long totalMs = (timeSendTicks - startTimestamp) * 1000 / Stopwatch.Frequency;
                    if (totalMs > 900)
                    {
                        long deserializeMs = (timeDeserializeTicks - startTimestamp) * 1000 / Stopwatch.Frequency;
                        long workerMs = (timeWorkerTicks - timeDeserializeTicks) * 1000 / Stopwatch.Frequency;
                        long serializeMs = (timeSerializeTicks - timeWorkerTicks) * 1000 / Stopwatch.Frequency;
                        long sendMs = (timeSendTicks - timeSerializeTicks) * 1000 / Stopwatch.Frequency;

                        Loger.Log($"Server Network total {totalMs}ms: " +
                            $"Deserialize {deserializeMs}ms, " +
                            $"Worker {workerMs}ms, " +
                            $"Serialize {serializeMs}ms, " +
                            $"Send {sendMs}ms");
                    }

                    // Оновлення часу останньої активності та перевірка запиту на відключення
                    if (context.Player != null)
                    {
                        lock (context.Player)
                        {
                            context.Player.Public.LastOnlineTime = DateTime.UtcNow;
                            if (context.Player.ExitReason != OCUnion.Transfer.DisconnectReason.AllGood)
                            {
                                Loger.Log("Disconnect . . . " + context.Player.ExitReason);
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                IsActive = false;
            }
        }

        /// <summary>
        /// Перевіряє наявність оновлень у чаті для гравця.
        /// </summary>
        private bool ServiceCheck()
        {
            if (ServiceCheckTime == DateTime.MinValue)
            {
                ServiceCheckTime = DateTime.UtcNow;
                return true;
            }

            var res = Worker.CheckChat(ServiceCheckTime);
            ServiceCheckTime = DateTime.UtcNow;
            return res;
        }
    }
}