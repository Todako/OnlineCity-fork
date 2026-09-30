using HarmonyLib;
using OCUnion;
using System;

namespace RimWorldOnlineCity
{
    public static class GameExit
    {
        public static Action BeforeExit = null;

        /// <summary>
        /// Безпечний одноразовий виклик делегата виходу з ізоляцією винятків.
        /// </summary>
        public static void TriggerBeforeExit()
        {
            var action = BeforeExit;
            if (action == null) return;

            BeforeExit = null; // Захист від повторних каскадних викликів під час одного виходу
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Loger.Log("GameExit.TriggerBeforeExit error: " + ex.Message, Loger.LogLevel.ERROR);
            }
        }
    }

    [HarmonyPatch(typeof(MemoryUtility))]
    [HarmonyPatch("ClearAllMapsAndWorld")]
    internal static class MemoryUtility_ClearAllMapsAndWorld_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            GameExit.TriggerBeforeExit();
        }
    }

    [HarmonyPatch(typeof(GenScene))]
    [HarmonyPatch("GoToMainMenu")]
    internal static class GenScene_GoToMainMenu_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            GameExit.TriggerBeforeExit();
        }
    }

    [HarmonyPatch(typeof(PlayDataLoader))]
    [HarmonyPatch("ClearAllPlayData")]
    internal static class PlayDataLoader_ClearAllPlayData_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            GameExit.TriggerBeforeExit();
        }
    }

    [HarmonyPatch(typeof(Root_Entry))]
    [HarmonyPatch("Start")]
    internal static class Root_Entry_Start_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            GameExit.TriggerBeforeExit();
        }
    }

    [HarmonyPatch(typeof(UIRoot_Entry))]
    [HarmonyPatch("DoMainMenu")]
    internal static class UIRoot_Entry_DoMainMenu_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            GameExit.TriggerBeforeExit();
        }
    }
}