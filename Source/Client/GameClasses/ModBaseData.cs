using HarmonyLib;
using OCUnion;
using RimWorldOnlineCity.GameClasses.Harmony;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    public class SettingHandle<T>
    {
        private readonly Func<T> _getter;
        private readonly Action<T> _setter;

        public SettingHandle(Func<T> getter, Action<T> setter)
        {
            _getter = getter;
            _setter = setter;
        }

        public T Value
        {
            get => _getter != null ? _getter() : default;
            set => _setter?.Invoke(value);
        }

        public bool NeverVisible { get; set; }
        public bool Unsaved { get; set; }

        public override string ToString() => Value?.ToString() ?? string.Empty;
    }

    public class ModBaseData : Mod
    {
        public static ModBaseData GlobalData { get; private set; }
        public static OCSettings Settings { get; private set; }

        public SettingHandle<string> LastIP;
        public SettingHandle<string> LastLoginName;
        public SettingHandle<string> LastPassword;
        public SettingHandle<string> LastCash;

        public ModBaseData(ModContentPack content) : base(content)
        {
            GlobalData = this;

            // Завантажуємо налаштування безпосередньо з Config/OnlineCity/ без реєстрації у LoadedModManager
            Settings = OCSettings.Load();

            InitSettingsHandles();

            var harmony = new HarmonyLib.Harmony("rimworld.onlinecity");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            HugsLibUtility_OpenModSettingsDialog_Patch.TryPatch(harmony);

            var dispatcherObject = new GameObject("OnlineCity_Dispatcher");
            UnityEngine.Object.DontDestroyOnLoad(dispatcherObject);
            dispatcherObject.AddComponent<OCDispatcher>();
        }

        public override void WriteSettings()
        {
            // Перевизначаємо метод Mod.WriteSettings(), щоб RimWorld не зберігав файл у корінь Config/
            Settings?.Save();
        }

        private void InitSettingsHandles()
        {
            LastIP = new SettingHandle<string>(
                () => Settings.LastIP,
                val => { Settings.LastIP = val ?? ""; WriteSettings(); }
            );

            LastLoginName = new SettingHandle<string>(
                () => Settings.LastLoginName,
                val => { Settings.LastLoginName = val ?? ""; WriteSettings(); }
            );

            LastPassword = new SettingHandle<string>(
                () => Settings.LastPassword,
                val => { Settings.LastPassword = val ?? ""; WriteSettings(); }
            );

            LastCash = new SettingHandle<string>(
                () => Settings.LastCash,
                val => { Settings.LastCash = val ?? ""; WriteSettings(); }
            );
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

        private static readonly long MaxTicksPerFrame = (Stopwatch.Frequency * 3) / 1000;

        public static bool RunMainThreadSync(Action act, int waitSecond = 10, bool softTimeout = false)
        {
            if (GlobalData.MainThreadNum == Thread.CurrentThread.ManagedThreadId)
            {
                act();
                return true;
            }

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

        public static long RunMainThread(Action act)
        {
            var num = Interlocked.Increment(ref GlobalData.ActionNumNext);
            var qa = RentAction(act, num, null);
            GlobalData.MainThread.Enqueue(qa);
            return num;
        }

        public void Update()
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
                        }
                    }
                }

                if (sw.ElapsedTicks > MaxTicksPerFrame)
                {
                    break;
                }
            }
        }
    }

    public class OCDispatcher : MonoBehaviour
    {
        private void Update()
        {
            ModBaseData.GlobalData?.Update();
        }
    }
}