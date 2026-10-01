using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace Transfer
{
    /// <summary>
    /// Фоновий таймер підтримки відкритого TCP-з'єднання під час простою гри (Heartbeat/Keep-Alive).
    /// </summary>
    public static class ConnectSaver
    {
        private static readonly Dictionary<ConnectClient, Action<ConnectClient>> Clients = new Dictionary<ConnectClient, Action<ConnectClient>>();
        private static readonly AutoResetEvent WakeEvent = new AutoResetEvent(false);
        private static readonly object SyncLock = new object();
        private static Thread Worker;

        /// <summary>
        /// Додає клієнтське з'єднання під спостереження пінгера.
        /// </summary>
        public static void AddClient(ConnectClient client, Action<ConnectClient> ping)
        {
            if (client == null || ping == null) return;

            lock (SyncLock)
            {
                Clients[client] = ping;
                StartWorker();
            }
            WakeEvent.Set();
        }

        /// <summary>
        /// Видаляє клієнта зі списку пінгування.
        /// </summary>
        public static void RemoveClient(ConnectClient client)
        {
            if (client == null) return;

            lock (SyncLock)
            {
                Clients.Remove(client);
            }
            WakeEvent.Set();
        }

        private static void StartWorker()
        {
            if (Worker != null) return;

            Worker = new Thread(WorkerDo)
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest,
                Name = "OC_ConnectSaverWorker"
            };
            Worker.Start();
        }

        private static void WorkerDo()
        {
            var deadClients = new List<ConnectClient>();
            var pingsToExecute = new List<(ConnectClient Client, Action<ConnectClient> PingAction)>();

            while (true)
            {
                // Захисне очікування: перевіряємо стан кожні 30 секунд або за сигналом пробудження
                WakeEvent.WaitOne(30000);

                deadClients.Clear();
                pingsToExecute.Clear();

                lock (SyncLock)
                {
                    if (Clients.Count == 0)
                    {
                        Worker = null;
                        break;
                    }

                    var now = DateTime.UtcNow.AddMinutes(2);

                    foreach (var pair in Clients)
                    {
                        var client = pair.Key;

                        if (client.Client == null || !client.Client.Connected)
                        {
                            deadClients.Add(client);
                            continue;
                        }

                        // Якщо з моменту останньої передачі даних минуло понад 2-3 хвилини — надсилаємо ping
                        if (now > client.LastSend)
                        {
                            pingsToExecute.Add((client, pair.Value));
                        }
                    }

                    // Безпечне видалення неактивних клієнтів без помилки InvalidOperationException
                    for (int i = 0; i < deadClients.Count; i++)
                    {
                        Clients.Remove(deadClients[i]);
                    }
                }

                // Виконання дій пінгування поза блокуванням SyncLock для усунення взаємного блокування
                for (int i = 0; i < pingsToExecute.Count; i++)
                {
                    try
                    {
                        pingsToExecute[i].PingAction?.Invoke(pingsToExecute[i].Client);
                    }
                    catch
                    {
                        lock (SyncLock)
                        {
                            Clients.Remove(pingsToExecute[i].Client);
                        }
                    }
                }
            }
        }
    }
}