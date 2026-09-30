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
    /// Переносить усі операції дискового I/O у фоновий потік із пакетним скиданням на накопичувач.
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

        private static readonly ConcurrentQueue<LogQueueEntry> LogQueue = new ConcurrentQueue<LogQueueEntry>();
        private static readonly AutoResetEvent QueueTrigger = new AutoResetEvent(false);
        private static readonly object DiskWriteLock = new object();
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
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _PathLog = string.Empty;
                    return;
                }
                var dir = Path.GetDirectoryName(value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
                _PathLog = string.IsNullOrEmpty(dir) ? string.Empty : dir + Path.DirectorySeparatorChar;
            }
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

        private static void LogWrite(string msg, bool withCatch, int threadId = 0, string suffix = null, DateTime time = default)
        {
            var h = msg.GetHashCode();
            var utcNow = DateTime.UtcNow;

            int thn = threadId != 0 ? threadId : Thread.CurrentThread.ManagedThreadId;
            var dn = time == default ? utcNow : time;
            long dd = 0;

            lock (ObjLock)
            {
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
                    EnqueueLogEntry("Was removed as spam " + lastSpamCount, withCatch, thn, suffix, lastLastTime, 0);
                    lastMsg = h;
                }

                if (LastMsg.TryGetValue(thn, out var lastThreadMsgTime))
                {
                    dd = (long)(dn - lastThreadMsgTime).TotalMilliseconds;
                    if (dd >= 1000000) dd = 0;
                }
                LastMsg[thn] = dn;

                if (LastMsg.Count > 256)
                {
                    LastMsg.Clear();
                }
            }

            EnqueueLogEntry(msg, withCatch, thn, suffix, dn, dd);
        }

        private static void EnqueueLogEntry(string msg, bool withCatch, int thn, string suffix, DateTime dn, long dd)
        {
            if (LogQueue.Count > 10000)
            {
                while (LogQueue.Count > 5000 && LogQueue.TryDequeue(out _)) { }
            }

            var logMsg = $"{dn:HH:mm:ss.ffff} |{dd,6} |{thn,4} | {msg}";
            var fileName = $"Log_{GetCurrentDateString()}_{MainHelper.LockCode}{(suffix == null ? "" : "_" + suffix)}.txt";
            var fullPath = (PathLog ?? "") + fileName;
            bool printConsole = !MainHelper.InGame && withCatch && suffix == null;

            LogQueue.Enqueue(new LogQueueEntry
            {
                FullPath = fullPath,
                FormattedMessage = logMsg,
                PrintToConsole = printConsole
            });

            QueueTrigger.Set();
        }

        private static void BackgroundDiskWriterLoop()
        {
            while (WriterActive)
            {
                QueueTrigger.WaitOne(500);
                DrainQueueToDisk();
            }
        }

        /// <summary>
        /// Пакетне скидання черги на диск за один I/O прохід для кожного файлу логу.
        /// </summary>
        private static void DrainQueueToDisk()
        {
            if (LogQueue.IsEmpty) return;

            lock (DiskWriteLock)
            {
                if (LogQueue.IsEmpty) return;

                var batch = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);

                while (LogQueue.TryDequeue(out var entry))
                {
                    if (entry.PrintToConsole)
                    {
                        Console.WriteLine(entry.FormattedMessage);
                    }

                    if (string.IsNullOrEmpty(entry.FullPath)) continue;

                    if (!batch.TryGetValue(entry.FullPath, out var sb))
                    {
                        sb = new StringBuilder(entry.FormattedMessage.Length + 64);
                        batch[entry.FullPath] = sb;
                    }
                    sb.AppendLine(entry.FormattedMessage);
                }

                foreach (var kvp in batch)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(kvp.Key);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        File.AppendAllText(kvp.Key, kvp.Value.ToString(), Encoding.UTF8);
                    }
                    catch (Exception exp)
                    {
                        LogErr = "Log exception: " + exp.Message;
                    }
                }
            }
        }

        /// <summary>
        /// Примусове скидання всіх накопичених повідомлень на диск перед закриттям програми.
        /// </summary>
        public static void Flush()
        {
            DrainQueueToDisk();
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