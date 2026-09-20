using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace OCUnion
{
    /// <summary>
    /// Високопродуктивний асинхронний логер.
    /// Переносить усі операції дискового I/O у фоновий потік, усуваючи затримки тіків RimWorld.
    /// </summary>
    public static class Loger
    {
        public enum LogLevel
        {
            [Description("EE")]
            ERROR,
            [Description("WW")]
            WARNING,
            [Description("--")]
            INFO,
            [Description("DB")]
            DEBUG,
            [Description("EH")]
            EXCHANGE,
            [Description("RG")]
            REGISTER,
            [Description("LG")]
            LOGIN,
            [Description("GE")]
            GAMEERROR,
        }

        private struct LogQueueEntry
        {
            public string FullPath;
            public string FormattedMessage;
            public bool PrintToConsole;
        }

        private static string _PathLog;
        private static readonly Dictionary<int, DateTime> LastMsg = new Dictionary<int, DateTime>();
        private static readonly object ObjLock = new object();
        public static bool IsServer;
        public static bool Enable = false;

        // Черга та сигналізатор фонового запису на диск
        private static readonly ConcurrentQueue<LogQueueEntry> LogQueue = new ConcurrentQueue<LogQueueEntry>();
        private static readonly AutoResetEvent QueueTrigger = new AutoResetEvent(false);
        private static Thread DiskWriterThread;
        private static volatile bool WriterActive = true;

        static Loger()
        {
            DiskWriterThread = new Thread(BackgroundDiskWriterLoop)
            {
                IsBackground = true,
                Name = "OC_LogWriterThread",
                Priority = ThreadPriority.BelowNormal
            };
            DiskWriterThread.Start();
        }

        public static string PathLog
        {
            get => _PathLog;
            set => _PathLog = Path.GetDirectoryName(value + Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        public static string Bytes(byte[] bs)
        {
            return BitConverter.ToString(bs).Replace('-', ' ');
        }

        private static string LogErr = null;
        private static int LogErrThr = 0;

        private static DateTime lastTime;
        private static int lastMsg;
        private static int spam;

        // Кеш поточної дати для назви файлу (перераховується раз на хвилину)
        private static string CachedDateString;
        private static DateTime LastDateCacheUpdate = DateTime.MinValue;

        private static string GetCurrentDateString()
        {
            var now = DateTime.UtcNow;
            if ((now - LastDateCacheUpdate).TotalSeconds > 60 || CachedDateString == null)
            {
                CachedDateString = DateTime.Now.ToString("yyyy-MM-dd");
                LastDateCacheUpdate = now;
            }
            return CachedDateString;
        }

        /// <summary>
        /// Форматує запис та ставить його в чергу для фонового запису без затримки основного потоку.
        /// </summary>
        private static void LogWrite(string msg, bool withCatch, int threadId = 0, string suffix = null, DateTime time = default)
        {
            // Фільтрація дублікатів і спаму
            var h = msg.GetHashCode();
            var utcNow = DateTime.UtcNow;

            if (h == lastMsg && (utcNow - lastTime).TotalMilliseconds < 2000)
            {
                spam++;
                return;
            }

            var lastLastTime = lastTime;
            lastTime = utcNow;
            lastMsg = h;

            if (spam > 0)
            {
                var lastSpamCount = spam;
                spam = 0;
                LogWrite("Was removed as spam " + lastSpamCount, withCatch, time: lastLastTime);
                lastMsg = h;
            }

            var thn = threadId != 0 ? threadId : Thread.CurrentThread.ManagedThreadId;
            var dn = time == default ? utcNow : time;

            long dd = 0;
            lock (LastMsg)
            {
                if (LastMsg.TryGetValue(thn, out var lastThreadMsgTime))
                {
                    dd = (long)(dn - lastThreadMsgTime).TotalMilliseconds;
                    if (dd >= 1000000) dd = 0;
                }
                LastMsg[thn] = dn;
            }

            // ОПТИМІЗАЦІЯ: інтерполяція з вирівнюванням без зайвих викликів PadLeft і проміжних рядків
            var logMsg = $"{dn:HH:mm:ss.ffff} |{dd,6} |{thn,4} | {msg}";

            var fileName = $"Log_{GetCurrentDateString()}_{MainHelper.LockCode}{(suffix == null ? "" : "_" + suffix)}.txt";
            var fullPath = (PathLog ?? "") + fileName;
            bool printConsole = !MainHelper.InGame && withCatch && suffix == null;

            // Додаємо запис у неблокуючу чергу на запис
            LogQueue.Enqueue(new LogQueueEntry
            {
                FullPath = fullPath,
                FormattedMessage = logMsg,
                PrintToConsole = printConsole
            });

            QueueTrigger.Set();
        }

        /// <summary>
        /// Фоновий цикл запису порцій логів на накопичувач.
        /// </summary>
        private static void BackgroundDiskWriterLoop()
        {
            while (WriterActive)
            {
                QueueTrigger.WaitOne(500);

                if (LogQueue.IsEmpty) continue;

                // Пакетний запис накопичених повідомлень
                while (LogQueue.TryDequeue(out var entry))
                {
                    if (entry.PrintToConsole)
                    {
                        Console.WriteLine(entry.FormattedMessage);
                    }

                    if (string.IsNullOrEmpty(entry.FullPath)) continue;

                    try
                    {
                        File.AppendAllText(entry.FullPath, entry.FormattedMessage + Environment.NewLine, Encoding.UTF8);
                    }
                    catch (Exception exp)
                    {
                        LogErr = "Log exception: " + exp.Message + Environment.NewLine + entry.FormattedMessage;
                    }
                }
            }
        }

        public static void Log(string msg, LogLevel logType = LogLevel.INFO, string suffix = null)
        {
            if (!Enable || MainHelper.OffAllLog) return;

            msg = $"[{GetEnumDescriptionFast(logType)}] {msg}";

            if (LogErr != null)
            {
                LogWrite(LogErr, false, LogErrThr, suffix);
                LogErr = null;
            }

            LogWrite(msg, true, default, suffix);
        }

        /// <summary>
        /// Миттєве отримання коду рівня логування без рефлексії.
        /// </summary>
        private static string GetEnumDescriptionFast(LogLevel logType)
        {
            switch (logType)
            {
                case LogLevel.ERROR: return "EE";
                case LogLevel.WARNING: return "WW";
                case LogLevel.INFO: return "--";
                case LogLevel.DEBUG: return "DB";
                case LogLevel.EXCHANGE: return "EH";
                case LogLevel.REGISTER: return "RG";
                case LogLevel.LOGIN: return "LG";
                case LogLevel.GAMEERROR: return "GE";
                default: return "--";
            }
        }

        private static readonly Dictionary<Enum, string> GenericEnumCache = new Dictionary<Enum, string>();

        public static string GetEnumDescription(Enum enumValue)
        {
            if (enumValue is LogLevel ll) return GetEnumDescriptionFast(ll);

            lock (GenericEnumCache)
            {
                if (GenericEnumCache.TryGetValue(enumValue, out var cached)) return cached;

                var memInfo = enumValue.GetType().GetMember(enumValue.ToString());
                if (memInfo.Length > 0)
                {
                    var description = memInfo[0].GetCustomAttribute<DescriptionAttribute>();
                    if (description != null)
                    {
                        GenericEnumCache[enumValue] = description.Description;
                        return description.Description;
                    }
                }

                GenericEnumCache[enumValue] = enumValue.ToString();
                return GenericEnumCache[enumValue];
            }
        }

        private static readonly StringBuilder TransLogBuffer = new StringBuilder();

        public static void TransLog(string msg)
        {
            lock (TransLogBuffer)
            {
                if (TransLogBuffer.Length > 0) TransLogBuffer.AppendLine();
                TransLogBuffer.Append(msg);
            }
        }

        public static string GetTransLog()
        {
            lock (TransLogBuffer)
            {
                if (TransLogBuffer.Length == 0) return null;
                var log = TransLogBuffer.ToString();
                TransLogBuffer.Clear();
                return log;
            }
        }
    }
}