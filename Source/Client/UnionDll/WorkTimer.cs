using OCUnion;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OCUnion
{
    /// <summary>
    /// Високопродуктивний таймер періодичних фонових завдань.
    /// Працює на базі подій синхронізації без постійного навантаження на процесор (busy-wait).
    /// </summary>
    public class WorkTimer
    {
        private class WorkTimerData
        {
            public long Interval;
            public Action Act;
            public DateTime LastRun;
        }

        private readonly List<WorkTimerData> Timers;
        private readonly AutoResetEvent _wakeEvent = new AutoResetEvent(false);
        private int Index;

        public bool IsStop { get; private set; } = false;
        public bool Pause { get; set; } = false;
        public Thread ThreadDo { get; private set; }
        public DateTime LastLoop { get; set; }

        public WorkTimer()
        {
            Timers = new List<WorkTimerData>();
            Index = 0;
            ThreadDo = new Thread(Do)
            {
                IsBackground = true,
                Name = "OC_WorkTimerThread"
            };
            ThreadDo.Start();
        }

        /// <summary>
        /// Повна зупинка таймера без блокувань.
        /// </summary>
        public void Stop()
        {
            IsStop = true;
            _wakeEvent.Set();
            lock (Timers)
            {
                Timers.Clear();
            }
        }

        public object Add(long interval, Action action)
        {
            var item = new WorkTimerData
            {
                Interval = interval,
                Act = action,
                LastRun = DateTime.UtcNow
            };
            lock (Timers)
            {
                Timers.Add(item);
            }
            _wakeEvent.Set();
            return item;
        }

        public void Remove(object obj)
        {
            if (!(obj is WorkTimerData item)) return;
            lock (Timers)
            {
                Timers.Remove(item);
            }
            _wakeEvent.Set();
        }

        private void Do()
        {
            while (!IsStop)
            {
                if (Pause)
                {
                    _wakeEvent.WaitOne(50);
                    continue;
                }

                Action actionToRun = null;
                int waitMs = 50;

                lock (Timers)
                {
                    if (Timers.Count > 0)
                    {
                        var now = DateTime.UtcNow;
                        LastLoop = now;

                        long minDelay = long.MaxValue;
                        WorkTimerData targetItem = null;

                        int count = Timers.Count;
                        for (int i = 0; i < count; i++)
                        {
                            var idx = (Index + i) % count;
                            var item = Timers[idx];
                            var elapsed = (long)(now - item.LastRun).TotalMilliseconds;
                            var remaining = item.Interval - elapsed;

                            if (remaining <= 0)
                            {
                                targetItem = item;
                                Index = (idx + 1) % count;
                                break;
                            }

                            if (remaining < minDelay)
                            {
                                minDelay = remaining;
                            }
                        }

                        if (targetItem != null)
                        {
                            targetItem.LastRun = now;
                            actionToRun = targetItem.Act;
                            waitMs = 0; // Наступна дія готова до перевірки
                        }
                        else
                        {
                            waitMs = (int)Math.Min(Math.Max(1, minDelay), 50);
                        }
                    }
                }

                // ОПТИМІЗАЦІЯ: виконання делегата строго ПОЗА МЕЖАМИ lock (Timers)
                if (actionToRun != null)
                {
                    DoItem(actionToRun);
                }
                else
                {
                    // ОПТИМІЗАЦІЯ: потік чекає точного настання наступного таймера замість Thread.Sleep(1)
                    _wakeEvent.WaitOne(waitMs);
                }
            }
        }

        private void DoItem(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ExceptionUtil.ExceptionLog(e, "WorkTimer");
            }
        }
    }
}