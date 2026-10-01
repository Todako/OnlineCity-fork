using HarmonyLib;
using OCUnion;
using System;
using Verse;

namespace RimWorldOnlineCity
{
    public static class GameLoades
    {
        public static Action AfterLoad = null;

        public static void TriggerAfterLoad()
        {
            var action = AfterLoad;
            if (action == null) return;

            AfterLoad = null;
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