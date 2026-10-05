using HarmonyLib;
using OCUnion;
using System;
using System.Threading;
using Verse;

namespace RimWorldOnlineCity
{
    public static class GameLoades
    {
        public static Action AfterLoad = null;

        /// <summary>
        /// Потокобезпечний одноразовий виклик делегата після завантаження карти.
        /// </summary>
        public static void TriggerAfterLoad()
        {
            var action = Interlocked.Exchange(ref AfterLoad, null);
            if (action == null) return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Loger.Log("GameLoades.TriggerAfterLoad error: " + ex.Message, Loger.LogLevel.ERROR);
            }
        }
    }

    [HarmonyPatch(typeof(SavedGameLoaderNow))]
    [HarmonyPatch("LoadGameFromSaveFileNow")]
    [HarmonyPatch(new[] { typeof(string) })]
    internal static class SavedGameLoader_LoadGameFromSaveFile_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            GameLoades.TriggerAfterLoad();
        }
    }
}