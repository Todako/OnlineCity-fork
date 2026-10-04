using HugsLib;
using HugsLib.Settings;
using OCUnion;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Головний диспетчер черги завдань основного потоку та налаштувань мода.
    /// Забезпечує синхронізацію між фоновими мережевими службами та головним циклом Unity
    /// з обмеженням бюджету часу на кадр (frame time budget) та пулом об'єктів.
    /// </summary>
    public class ModBaseData : ModBase
    {
        public static ModBaseData GlobalData = null;

        public ModBaseData() : base()
        {
            GlobalData = this;
        }

        public override string ModIdentifier => "OnlineCity";

        public SettingHandle<string> LastIP;
        public SettingHandle<string> LastLoginName;
        public SettingHandle<string> LastPassword;
        public SettingHandle<string> LastCash;

        public override void DefsLoaded()
        {
            LastIP = Settings.GetHandle<string>("ocLastIP", "OCity_StorageTest_LastIP".Translate(), null, "");
            LastIP.NeverVisible = true;
            LastIP.Unsaved = false;

            LastLoginName = Settings.GetHandle<string>("ocLastLoginName", "OCity_StorageTest_LoginName".Translate(), null, "");
            LastLoginName.NeverVisible = true;
            LastLoginName.Unsaved = false;

            LastPassword = Settings.GetHandle<string>("ocLastPassword", "OCity_StorageTest_Password".Translate(), null, "");
            LastPassword.NeverVisible = true;
            LastPassword.Unsaved = false;

            LastCash = Settings.GetHandle<string>("ocLastCash", "OCity_StorageTest_LastCash".Translate(), null, "");
            LastCash.NeverVisible = true;
            LastCash.Unsaved = false;
        }

        private class QueueActionModel
        {
            public Action Act;
            public long Num;
            public ManualResetEventSlim DoneEvent;
        }

        #region Пул моделей дій
        private const int MaxPoolSize = 128;
        private static int PoolSize = 0;
        private static readonly ConcurrentQueue<QueueActionModel> ActionPool = new ConcurrentQueue<QueueActionModel>();

        private static QueueActionModel RentAction(Action act, long num, ManualResetEventSlim doneEvent)
        {
            if (ActionPool.TryDequeue(out var qa))
            {
                Interlocked.Decrement(ref PoolSize);
                qa.Act = act;
                qa.Num = num;
                qa.DoneEvent = doneEvent;
                return qa;
            }
            return new QueueActionModel { Act = act, Num = num, DoneEvent = doneEvent };
        }

        private static void ReturnAction(QueueActionModel qa)
        {
            qa.Act = null;
            qa.DoneEvent = null;
            if (PoolSize < MaxPoolSize)
            {
                Interlocked.Increment(ref PoolSize);
                ActionPool.Enqueue(qa);
            }
        }
        #endregion

        private long ActionNumNext = 0;
        public long ActionNumReady = 0;

        private readonly ConcurrentQueue<QueueActionModel> MainThread = new ConcurrentQueue<QueueActionModel>();
        public int MainThreadNum = int.MinValue;
        public DateTime LastRunDebug;

        // Бюджет часу виконання черги: максимум 3 мс на кадр для уникнення мікрофризів Unity
        private static readonly long MaxTicksPerFrame = (Stopwatch.Frequency * 3) / 1000;

        /// <summary>
        /// Виконує дію в основному потоці гри з очікуванням завершення.
        /// ОПТИМІЗАЦІЯ: миттєве відновлення роботи через ManualResetEventSlim і пул завдань.
        /// </summary>
        public static bool RunMainThreadSync(Action act, int waitSecond = 10, bool softTimeout = false)
        {
            if (GlobalData.MainThreadNum == Thread.CurrentThread.ManagedThreadId)
            {
                act();
                return true;
            }

            // Швидка lock-free перевірка навантаження черги за O(1)
            long pendingCount = Interlocked.Read(ref GlobalData.ActionNumNext) - Interlocked.Read(ref GlobalData.ActionNumReady);
            if (softTimeout && pendingCount > 2)
            {
                Loger.Log($"Client RunMainThread CancelRun currentReady={GlobalData.ActionNumReady} pending={pendingCount} LastRunDebug={GlobalData.LastRunDebug.Ticks}", Loger.LogLevel.DEBUG);
                return false;
            }

            using (var doneEvent = new ManualResetEventSlim(false))
            {
                var num = Interlocked.Increment(ref GlobalData.ActionNumNext);
                var qa = RentAction(act, num, doneEvent);

                GlobalData.MainThread.Enqueue(qa);

                if (!doneEvent.Wait(waitSecond * 1000))
                {
                    Loger.Log($"Client RunMainThread Timeout Exception num={num} currentReady={GlobalData.ActionNumReady} pending={pendingCount} LastRunDebug={GlobalData.LastRunDebug.Ticks}", Loger.LogLevel.DEBUG);
                    if (!softTimeout) throw new ApplicationException("Client RunMainThread Timeout");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Додає дію до черги виконання в основному потоці Unity.
        /// ОПТИМІЗАЦІЯ: повторне використання об'єктів з пулу замість алокацій у купі.
        /// </summary>
        public static long RunMainThread(Action act)
        {
            var num = Interlocked.Increment(ref GlobalData.ActionNumNext);
            var qa = RentAction(act, num, null);
            GlobalData.MainThread.Enqueue(qa);
            return num;
        }

        /// <summary>
        /// Щокадрове оновлення з черги головного потоку RimWorld з контролем часу виконання (Frame Budgeting).
        /// </summary>
        public override void Update()
        {
            LastRunDebug = DateTime.UtcNow;
            if (MainThreadNum == int.MinValue)
            {
                MainThreadNum = Thread.CurrentThread.ManagedThreadId;
            }

            var sw = Stopwatch.StartNew();

            while (MainThread.TryDequeue(out var qa))
            {
                var done = qa.DoneEvent;
                try
                {
                    qa.Act?.Invoke();
                }
                catch (Exception ext)
                {
                    Loger.Log("Client RunMainThread Exception " + ext.ToString(), Loger.LogLevel.ERROR);
                }
                finally
                {
                    ActionNumReady = qa.Num;
                    ReturnAction(qa);

                    if (done != null)
                    {
                        try
                        {
                            done.Set();
                        }
                        catch (ObjectDisposedException)
                        {
                            // Викликаючий потік завершився за таймаутом і утилізував подію
                        }
                    }
                }

                // Якщо бюджет кадру (3 мс) вичерпано — переносимо решту завдань на наступний кадр
                if (sw.ElapsedTicks > MaxTicksPerFrame)
                {
                    break;
                }
            }
        }
    }
}