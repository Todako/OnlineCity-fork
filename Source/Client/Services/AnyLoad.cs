using OCUnion;
using System;
using System.Collections.Generic;
using System.Threading;

namespace RimWorldOnlineCity.Services
{
    public class AnyLoadTask
    {
        public long Hash { get; set; }
        public string Data { get; set; }
        public DateTime LoadDate { get; set; }
    }

    /// <summary>
    /// Служба фонового асинхронного завантаження довільних пакетів даних за їхнім хешем.
    /// Забезпечує пакетне завантаження (batching) по 10 штук та локальне кешування в пам'яті.
    /// </summary>
    public class AnyLoad
    {
        private static Thread Downloader = null;
        private static readonly List<AnyLoad> Tasks = new List<AnyLoad>();

        // Потокобезпечний кеш з лімітом місткості
        private static readonly Dictionary<long, AnyLoadTask> Database = new Dictionary<long, AnyLoadTask>(512);
        private static readonly object DbLock = new object();
        private static DateTime LastDbCleanup = DateTime.UtcNow;

        public List<AnyLoadTask> ListLoad { get; set; }
        public Action<AnyLoad, int> TaskProgress { get; set; }
        public Action<AnyLoad> TaskFinish { get; set; }
        public Action<AnyLoad, string> TaskError { get; set; }

        public AnyLoad(List<AnyLoadTask> listLoad, Action<AnyLoad> taskFinish, Action<AnyLoad, int> taskProgress, Action<AnyLoad, string> taskError)
        {
            ListLoad = listLoad ?? new List<AnyLoadTask>(0);
            TaskFinish = taskFinish;
            TaskProgress = taskProgress;
            TaskError = taskError;

            lock (Tasks)
            {
                Tasks.Add(this);
                Start();
            }
        }

        public void Cancel()
        {
            Loger.Log("AnyLoadDownloadThread Cancel");
            lock (Tasks)
            {
                Tasks.Remove(this);
            }
            TaskFinish = null;
            TaskProgress = null;
            TaskError = null;
        }

        private static void Start()
        {
            if (Downloader == null)
            {
                Downloader = new Thread(DownloadThread)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal
                };
                Downloader.Start();
            }
        }

        /// <summary>
        /// Головний робочий цикл фонового завантажувача.
        /// ОПТИМІЗАЦІЯ: ліквідовано квадратичне O(N^2) сканування завдань;
        /// додано періодичне очищення кешу пам'яті Database.
        /// </summary>
        private static void DownloadThread()
        {
            try
            {
                while (true)
                {
                    AnyLoad currentTask = null;
                    lock (Tasks)
                    {
                        if (Tasks.Count == 0) break;
                        currentTask = Tasks[0];
                    }

                    if (currentTask.ListLoad == null || currentTask.ListLoad.Count == 0)
                    {
                        TasksRemove(currentTask);
                        continue;
                    }

                    if (!DownloadCheckConnect())
                    {
                        Error("Load: error connect");
                        break;
                    }

                    // 1. Однопрохідне розділення: що вже є в кеші, а що треба завантажити за O(N)
                    var neededToDownload = new List<AnyLoadTask>();
                    int resolvedCount = 0;
                    var now = DateTime.UtcNow;

                    lock (DbLock)
                    {
                        // Періодичне очищення кешу (раз на 10 хвилин, якщо більше 600 записів)
                        if ((now - LastDbCleanup).TotalMinutes > 10 && Database.Count > 600)
                        {
                            CleanupDatabaseCache(now);
                        }

                        for (int i = 0; i < currentTask.ListLoad.Count; i++)
                        {
                            var item = currentTask.ListLoad[i];
                            if (Database.TryGetValue(item.Hash, out var cached))
                            {
                                item.Data = cached.Data;
                                item.LoadDate = cached.LoadDate = now;
                                resolvedCount++;
                            }
                            else
                            {
                                neededToDownload.Add(item);
                            }
                        }
                    }

                    // Повідомляємо про початковий прогрес із кешу
                    if (resolvedCount > 0 && currentTask.ListLoad.Count > 0)
                    {
                        currentTask.TaskProgress?.Invoke(currentTask, (int)(100L * resolvedCount / currentTask.ListLoad.Count));
                    }

                    // 2. Пакетне завантаження відсутніх елементів блоками по 10 штук
                    bool hasError = false;
                    for (int chunkStart = 0; chunkStart < neededToDownload.Count; chunkStart += 10)
                    {
                        // Перевіряємо чи завдання не було скасовано гравцем
                        lock (Tasks)
                        {
                            if (!Tasks.Contains(currentTask)) break;
                        }

                        if (!DownloadCheckConnect())
                        {
                            Error("Load: error connect.");
                            hasError = true;
                            break;
                        }

                        int chunkSize = Math.Min(10, neededToDownload.Count - chunkStart);
                        var chunk = new List<AnyLoadTask>(chunkSize);
                        for (int c = 0; c < chunkSize; c++)
                        {
                            chunk.Add(neededToDownload[chunkStart + c]);
                        }

                        DownloadList(chunk);
                        resolvedCount += chunkSize;

                        currentTask.TaskProgress?.Invoke(currentTask, (int)(100L * resolvedCount / currentTask.ListLoad.Count));
                    }

                    if (hasError) break;

                    // Завершуємо поточну задачу
                    TasksRemove(currentTask);
                }
            }
            catch (Exception exp)
            {
                Error("Load error: " + exp.ToString());
            }
            finally
            {
                Downloader = null;
            }
        }

        private static void CleanupDatabaseCache(DateTime now)
        {
            var keysToRemove = new List<long>();
            foreach (var kvp in Database)
            {
                if ((now - kvp.Value.LoadDate).TotalMinutes > 30)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            for (int i = 0; i < keysToRemove.Count; i++)
            {
                Database.Remove(keysToRemove[i]);
            }
            LastDbCleanup = now;
        }

        private static void TasksRemove(AnyLoad that)
        {
            Action<AnyLoad> finishAction = null;
            lock (Tasks)
            {
                if (Tasks.Remove(that))
                {
                    finishAction = that.TaskFinish;
                }
            }
            // Виклик колбеку за межами блокування усуває ризик взаємного блокування потоків (Deadlock)
            finishAction?.Invoke(that);
        }

        private static bool DownloadCheckConnect()
        {
            while (SessionClient.Get.IsLogined && SessionClient.IsRelogin)
            {
                Thread.Sleep(10);
            }
            return SessionClient.Get.IsLogined;
        }

        /// <summary>
        /// Запитує блок даних у сервера за списком хешів.
        /// ОПТИМІЗАЦІЯ: список хешів формується без LINQ-викликів .Select().ToList().
        /// </summary>
        private static void DownloadList(List<AnyLoadTask> download)
        {
            var hashes = new List<long>(download.Count);
            for (int i = 0; i < download.Count; i++)
            {
                hashes.Add(download[i].Hash);
            }

            List<string> datas = null;
            SessionClientController.Command((connect) =>
            {
                datas = connect.AnyLoad(hashes);
            });

            if (datas == null || datas.Count != download.Count)
            {
                throw new ApplicationException("AnyLoad: bad response from server");
            }

            var now = DateTime.UtcNow;
            lock (DbLock)
            {
                for (int i = 0; i < download.Count; i++)
                {
                    download[i].Data = datas[i];
                    download[i].LoadDate = now;
                    Database[download[i].Hash] = download[i];
                }
            }
        }

        private static void Error(string error)
        {
            Loger.Log("Client AnyLoad error: " + error, Loger.LogLevel.ERROR);

            List<AnyLoad> tasksToNotify;
            lock (Tasks)
            {
                tasksToNotify = new List<AnyLoad>(Tasks);
                Tasks.Clear();
                Downloader = null;
            }

            // Оповіщення про помилку поза блокуванням Tasks
            for (int i = 0; i < tasksToNotify.Count; i++)
            {
                try
                {
                    tasksToNotify[i].TaskError?.Invoke(tasksToNotify[i], error);
                }
                catch { }
            }
        }
    }
}