using HarmonyLib;
using HugsLib;
using OCUnion;
using OCUnion.Common;
using RimWorld;
using System;
using System.IO;
using Verse;

namespace RimWorldOnlineCity.GameClasses.Harmony
{
    internal static class GamePresetFiles
    {
        private static string PrepareKey(byte[] data)
        {
            var key = FileChecker.GetCheckSum(data);
            key = FileChecker.GetCheckSum(key + "dfd%>*<" + (ModBaseData.GlobalData?.LastIP?.Value ?? "##"));
            return key.Length > 20 ? key.Substring(4, 16) : key;
        }

        /// <summary>
        /// Нуль-алокаційна перевірка наявності ключа у списку, розділеному '|'.
        /// </summary>
        private static bool IsKeyInList(string list, string key)
        {
            if (string.IsNullOrEmpty(list) || string.IsNullOrEmpty(key)) return false;

            int idx = 0;
            while ((idx = list.IndexOf(key, idx, StringComparison.Ordinal)) >= 0)
            {
                bool startOk = idx == 0 || list[idx - 1] == '|';
                int endIdx = idx + key.Length;
                bool endOk = endIdx == list.Length || list[endIdx] == '|';

                if (startOk && endOk) return true;

                idx++;
            }
            return false;
        }

        private static void AppendKeyToCache(string key)
        {
            if (ModBaseData.GlobalData?.LastCash == null) return;

            var current = ModBaseData.GlobalData.LastCash.Value ?? string.Empty;
            if (!IsKeyInList(current, key))
            {
                ModBaseData.GlobalData.LastCash.Value = current.Length == 0 ? key : current + "|" + key;
                HugsLibController.SettingsManager.SaveChanges();
            }
        }

        [HarmonyPatch(typeof(GameDataSaveLoader))]
        [HarmonyPatch("SaveIdeo")]
        internal static class GameDataSaveLoader_SaveIdeo_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Ideo ideo, string absFilePath)
            {
                if (Current.Game == null) return;
                if (SessionClient.Get?.IsLogined != true) return;

                try
                {
                    var normalizedPath = absFilePath.NormalizePath();
                    if (!File.Exists(normalizedPath)) return;

                    var key = PrepareKey(File.ReadAllBytes(normalizedPath));
                    Loger.Log("PresetSaveIdeo: " + absFilePath + " " + (ModBaseData.GlobalData?.LastIP?.Value ?? "##") + " " + key);

                    AppendKeyToCache(key);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(GameDataSaveLoader))]
        [HarmonyPatch("TryLoadIdeo")]
        internal static class GameDataSaveLoader_TryLoadIdeo_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(string absPath, out Ideo ideo, ref bool __result)
            {
                ideo = null;
                __result = false;
                if (Current.Game == null) return true;
                if (SessionClient.Get?.IsLogined != true) return true;
                if (SessionClientController.Data == null || !SessionClientController.Data.DisableDevMode) return true;

                try
                {
                    var normalizedPath = absPath.NormalizePath();
                    if (!File.Exists(normalizedPath)) return false;

                    var key = PrepareKey(File.ReadAllBytes(normalizedPath));
                    Loger.Log("PresetLoadIdeo: " + absPath + " " + (ModBaseData.GlobalData?.LastIP?.Value ?? "##") + " " + key);

                    var list = ModBaseData.GlobalData?.LastCash?.Value ?? string.Empty;
                    if (IsKeyInList(list, key)) return true;

                    if (Find.WindowStack != null)
                    {
                        var msg = "OCity_GamePresetFiles_IdeologyNotCreatedDuringANetworkGame".Translate();
                        Find.WindowStack.Add(new Dialog_Input("OCity_Dialog_CreateWorld_BtnCancel".Translate(), msg, true));
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(GameDataSaveLoader))]
        [HarmonyPatch("SaveXenotype")]
        internal static class GameDataSaveLoader_SaveXenotype_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CustomXenotype xenotype, string absFilePath)
            {
                if (Current.Game == null) return;
                if (SessionClient.Get?.IsLogined != true) return;

                try
                {
                    var normalizedPath = absFilePath.NormalizePath();
                    if (!File.Exists(normalizedPath)) return;

                    var key = PrepareKey(File.ReadAllBytes(normalizedPath));
                    Loger.Log("PresetSaveXenotype: " + absFilePath + " " + (ModBaseData.GlobalData?.LastIP?.Value ?? "##") + " " + key);

                    AppendKeyToCache(key);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(GameDataSaveLoader))]
        [HarmonyPatch("TryLoadXenotype")]
        internal static class GameDataSaveLoader_TryLoadXenotype_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(string absPath, out CustomXenotype xenotype, ref bool __result)
            {
                xenotype = null;
                __result = false;
                if (Current.Game == null) return true;
                if (SessionClient.Get?.IsLogined != true) return true;
                if (SessionClientController.Data == null || !SessionClientController.Data.DisableDevMode) return true;

                try
                {
                    var normalizedPath = absPath.NormalizePath();
                    if (!File.Exists(normalizedPath)) return false;

                    var key = PrepareKey(File.ReadAllBytes(normalizedPath));
                    Loger.Log("PresetLoadXenotype: " + absPath + " " + (ModBaseData.GlobalData?.LastIP?.Value ?? "##") + " " + key);

                    var list = ModBaseData.GlobalData?.LastCash?.Value ?? string.Empty;
                    if (IsKeyInList(list, key)) return true;

                    if (Find.WindowStack != null)
                    {
                        var msg = "OCity_GamePresetFiles_XenotypeNotCreatedDuringANetworkGame".Translate();
                        Find.WindowStack.Add(new Dialog_Input("OCity_Dialog_CreateWorld_BtnCancel".Translate(), msg, true));
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}