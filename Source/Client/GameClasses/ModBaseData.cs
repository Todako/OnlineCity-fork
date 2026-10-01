using HugsLib;
using HugsLib.Settings;
using OCUnion;
using System;
using System.Collections.Concurrent;
using System.Threading;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Головний диспетчер черги завдань основного потоку та налаштувань мода.
    /// Забезпечує миттєву синхронізацію між фоновими мережевими службами та головним циклом Unity.
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

        private long ActionNumNext = 0;
        public long ActionNumReady = 0;

        private readonly ConcurrentQueue<QueueActionModel> MainThread = new ConcurrentQueue<QueueActionModel>();
        public int MainThreadNum = int.MinValue;
        public DateTime LastRunDebug;

        /// <summary>
        /// Виконує дію в основному потоці гри з очікуванням завершення.
        /// ОПТИМІЗАЦІЯ: усунено цикл опитування Thread.Sleep. 
        /// Фоновий потік відновлює роботу миттєво після виконання дії через ManualResetEventSlim.
        /// </summary>
        public static bool RunMainThreadSync(Action act, int waitSecond = 10, bool softTimeout = false)
        {
            if (GlobalData.MainThreadNum == Thread.CurrentThread.ManagedThreadId)
            {
                act();
                return true;
            }

            if (softTimeout && GlobalData.MainThread.Count > 2)
            {
                Loger.Log($"Client RunMainThread CancelRun currentReady={GlobalData.ActionNumReady} count={GlobalData.MainThread.Count} LastRunDebug={GlobalData.LastRunDebug.Ticks}", Loger.LogLevel.DEBUG);
                return false;
            }

            using (var doneEvent = new ManualResetEventSlim(false))
            {
                var num = Interlocked.Increment(ref GlobalData.ActionNumNext);
                var qa = new QueueActionModel
                {
                    Num = num,
                    Act = act,
                    DoneEvent = doneEvent
                };

                GlobalData.MainThread.Enqueue(qa);

                if (!doneEvent.Wait(waitSecond * 1000))
                {
                    Loger.Log($"Client RunMainThread Timeout Exception num={num} currentReady={GlobalData.ActionNumReady} count={GlobalData.MainThread.Count} LastRunDebug={GlobalData.LastRunDebug.Ticks}", Loger.LogLevel.DEBUG);
                    if (!softTimeout) throw new ApplicationException("Client RunMainThread Timeout");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Додає дію до черги виконання в основному потоці Unity.
        /// ОПТИМІЗАЦІЯ: повністю безблокувальне (lock-free) додавання до ConcurrentQueue.
        /// </summary>
        public static long RunMainThread(Action act)
        {
            var num = Interlocked.Increment(ref GlobalData.ActionNumNext);
            var qa = new QueueActionModel
            {
                Num = num,
                Act = act,
                DoneEvent = null
            };
            GlobalData.MainThread.Enqueue(qa);
            return num;
        }

        /// <summary>
        /// Щокадрове оновлення з черги головного потоку RimWorld.
        /// </summary>
        public override void Update()
        {
            LastRunDebug = DateTime.UtcNow;
            if (MainThreadNum == int.MinValue)
            {
                MainThreadNum = Thread.CurrentThread.ManagedThreadId;
            }

            // ОПТИМІЗАЦІЯ: обробка без захоплення глобального блокування черги
            while (MainThread.TryDequeue(out var qa))
            {
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
                    qa.DoneEvent?.Set();
                }
            }
        }
    }
}