using HarmonyLib;
using OCUnion;
using System;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity.GameClasses.Harmony
{
    /// <summary>
    /// Контекстний перехоплювач помилок гри під час виконання чутливих операцій (десеріалізації тощо).
    /// </summary>
    public class CatchGameError : IDisposable
    {
        private readonly Func<string, bool> OnError;
        public string GameError = null;

        public CatchGameError(Func<string, bool> onError = null)
        {
            OnError = onError ?? ((msg) => true);
            GameLog.OnError += GameLog_OnError;
        }

        private bool GameLog_OnError(string msg)
        {
            GameError = msg ?? "";
            return OnError(msg);
        }

        public void Dispose()
        {
            GameLog.OnError -= GameLog_OnError;
        }
    }

    public static class GameLog
    {
        public static event Func<string, bool> OnError;

        internal static bool Error(string text)
        {
            var res = OnError == null || OnError(text);

            // ОПТИМІЗАЦІЯ: важкий StackTraceUtility.ExtractStackTrace викликається
            // ЛИШЕ якщо помилка не приглушена і логування реально увімкнене в налаштуваннях
            if (res && Loger.Enable && !MainHelper.OffAllLog)
            {
                Loger.Log("Error game log. " + text + Environment.NewLine + GetStackTrace(), Loger.LogLevel.GAMEERROR);
            }

            return res;
        }

        private static string GetStackTrace()
        {
            var stackTrace = StackTraceUtility.ExtractStackTrace();
            var i = stackTrace.IndexOf("RimWorldOnlineCity.GameClasses.Harmony.Log_Error_Patch", StringComparison.Ordinal);
            if (i > 0)
            {
                i = stackTrace.IndexOf('\n', i);
                if (i > 0 && i + 1 < stackTrace.Length)
                {
                    stackTrace = stackTrace.Substring(i + 1);
                }
            }
            return stackTrace;
        }
    }

    /// <summary>
    /// Гармоні-перехоплювач ванільного методу Verse.Log.Error.
    /// </summary>
    [HarmonyPatch(typeof(Log))]
    [HarmonyPatch("Error")]
    [HarmonyPatch(new Type[] { typeof(string) })]
    internal class Log_Error_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(string text)
        {
            return GameLog.Error(text);
        }
    }
}