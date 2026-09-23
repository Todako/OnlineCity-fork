using HarmonyLib;
using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity.GameClasses.Harmony
{
    // ====================================================================================
    // Контроль режиму розробника (DevMode)
    // ====================================================================================

    [HarmonyPatch(typeof(PrefsData))]
    [HarmonyPatch("Apply")]
    internal class PrefsData_Apply_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Current.Game == null) return;
            if (!SessionClient.Get.IsLogined) return;

            if (SessionClientController.Data.DisableDevMode)
            {
                if (Prefs.DevMode) Prefs.DevMode = false;
                if (IdeoUIUtility.devEditMode) IdeoUIUtility.devEditMode = false;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_Options))]
    [HarmonyPatch("DoModOptions")]
    internal class Dialog_Options_DoModOptions_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Listing_Standard listing)
        {
            if (Current.Game == null && MainMenu.HasClickMainMenuNetClick)
            {
                listing.Gap();
                var rect2 = listing.GetRect(50f);
                Widgets.Label(rect2, "OCity_GamePatch_DisableModOptions".Translate());
                return false;
            }

            if (Current.Game == null) return true;
            if (!SessionClient.Get.IsLogined) return true;

            if (SessionClientController.Data.DisableDevMode)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Dialog_CreateXenotype))]
    [HarmonyPatch("PostXenotypeOnGUI")]
    internal class Dialog_CreateXenotype_PostXenotypeOnGUI_Patch
    {
        private static readonly AccessTools.FieldRef<Dialog_CreateXenotype, bool> IgnoreRestrictionsRef =
            AccessTools.FieldRefAccess<Dialog_CreateXenotype, bool>("ignoreRestrictions");

        [HarmonyPostfix]
        public static void Postfix(Dialog_CreateXenotype __instance)
        {
            if (Current.Game == null) return;
            if (!SessionClient.Get.IsLogined) return;

            if (!SessionClientController.Data.DisableDevMode) return;

            IgnoreRestrictionsRef(__instance) = false;
        }
    }

    [HarmonyPatch(typeof(DebugTool))]
    [HarmonyPatch("DebugToolOnGUI")]
    internal class DebugTool_DebugToolOnGUI_Patch
    {
        private static DateTime LastCheck = DateTime.MinValue;

        [HarmonyPrefix]
        public static bool Prefix()
        {
            if ((DateTime.UtcNow - LastCheck).TotalSeconds < 5) return true;
            LastCheck = DateTime.UtcNow;

            if (Current.Game == null) return true;
            if (!SessionClient.Get.IsLogined) return true;

            if (!SessionClientController.Data.DisableDevMode) return true;

            Loger.TransLog("ShowDevMode");
            return true;
        }
    }

    // ====================================================================================
    // Захист налаштувань оповідача та сторонніх модулів (HugsLib)
    // ====================================================================================

    [HarmonyPatch(typeof(Page_SelectStorytellerInGame))]
    [HarmonyPatch("DoWindowContents")]
    internal class Page_SelectStorytellerInGame_DoWindowContents_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page_SelectStorytellerInGame __instance)
        {
            if (Current.Game == null) return true;
            if (!SessionClient.Get.IsLogined) return true;
            if (Prefs.DevMode) return true;

            if (SessionClientController.Data.GeneralSettings.DisableGameSettings)
            {
                Loger.Log("Page_SelectStorytellerInGame_DoWindowContents_Patch DisableGameSettings");
                __instance.Close();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(HugsLib.Utils.HugsLibUtility))]
    [HarmonyPatch("OpenModSettingsDialog")]
    internal class HugsLibUtility_OpenModSettingsDialog_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (Current.Game == null) return true;
            if (!SessionClient.Get.IsLogined) return true;
            if (Prefs.DevMode) return true;

            if (SessionClientController.Data.GeneralSettings.DisableGameSettings)
            {
                Loger.Log("HugsLibUtility_OpenModSettingsDialog_Patch DisableGameSettings");
                return false;
            }

            return true;
        }
    }

    // ====================================================================================
    // Ігрова дата та синхронізація часу
    // ====================================================================================

    [HarmonyPatch(typeof(GenDate), "Year")]
    internal class GenDatePatch
    {
        public static void Postfix(long absTicks, float longitude, ref int __result)
        {
            if (!SessionClient.Get.IsLogined) return;
            if (SessionClientController.Data == null) return;

            int needYear = SessionClientController.Data.GeneralSettings.StartGameYear;
            if (needYear < 0 || needYear == 5500) return;

            long longAdj = GenDate.TimeZoneAt(longitude) * 2500L;
            __result = needYear + (int)((absTicks + longAdj) / 3600000L);
        }
    }

    [HarmonyPatch(typeof(CrossRefHandler))]
    [HarmonyPatch("ResolveAllCrossReferences")]
    public class CrossRefHandler_ResolveAllCrossReferences_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!GameXMLUtils.FromXmlIsActive) return true;
            if (Current.Game == null) return true;

            var crossRefs = Scribe.loader?.crossRefs?.crossReferencingExposables;
            if (crossRefs == null) return true;

            var toAdd = ThingEntry.crossReferencingExposables;
            if (toAdd != null && toAdd.Count > 0)
            {
                var existing = new HashSet<IExposable>(crossRefs);
                for (int i = 0; i < toAdd.Count; i++)
                {
                    var item = toAdd[i];
                    if (existing.Add(item))
                    {
                        crossRefs.Add(item);
                    }
                }

                toAdd.Clear();
            }

            return true;
        }
    }

    // ====================================================================================
    // Віджет статусу мережі в правому нижньому кутку
    // ====================================================================================

    [HarmonyPatch(typeof(GlobalControlsUtility))]
    [HarmonyPatch("DoDate")]
    [StaticConstructorOnStartup]
    public class GlobalControlsUtility_DoDate_Patch
    {
        public static List<string> OutText = null;
        public static string TooltipText = null;
        public static Texture2D OutInLastLine = null;
        public static DateTime Update;

        public static float CachedWidth = 0f;
        private static TipSignal CachedTipSignal;
        private static string CachedTipText;

        [HarmonyPostfix]
        public static void Postfix(float leftX, float width, ref float curBaseY)
        {
            if (!SessionClient.Get.IsLogined) return;
            if (OutText == null || OutText.Count == 0) return;

            if ((DateTime.UtcNow - Update).TotalSeconds > 5)
            {
                OutText = null;
                TooltipText = null;
                OutInLastLine = null;
                CachedWidth = 0f;
                return;
            }

            try
            {
                var outText = OutText;
                var height = 22 + 26 * (outText.Count - 1);
                Rect dateRect = new Rect(leftX, curBaseY - height, width, height);

                if (CachedWidth <= 0f)
                {
                    Text.Font = GameFont.Small;
                    float maxW = 0f;
                    for (int i = 0; i < outText.Count; i++)
                    {
                        float w = Text.CalcSize(outText[i]).x;
                        if (w > maxW) maxW = w;
                    }
                    CachedWidth = maxW + 7f;
                }

                dateRect.xMin = dateRect.xMax - CachedWidth;

                bool isOver = Mouse.IsOver(dateRect);
                if (isOver)
                {
                    Widgets.DrawHighlight(dateRect);
                }

                GUI.BeginGroup(dateRect);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperRight;
                Rect rect = dateRect.AtZero();
                rect.xMax -= 7f;
                Rect rectText = rect;

                for (int i = 0; i < outText.Count; i++)
                {
                    if (i + 1 == outText.Count && OutInLastLine != null)
                    {
                        rectText = rect;
                        rect.width -= 26f;
                        rectText.x += rect.width - 2f;
                        rectText.y += 1;
                        rectText.width = 24f;
                        rectText.height = 24f;
                    }
                    Widgets.Label(rect, outText[i]);
                    rect.yMin += 26f;
                }

                if (OutInLastLine != null) GUI.DrawTexture(rectText, OutInLastLine);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.EndGroup();

                if (TooltipText != null && isOver)
                {
                    if (CachedTipText != TooltipText)
                    {
                        CachedTipText = TooltipText;
                        CachedTipSignal = new TipSignal(TooltipText, 5634323);
                    }
                    TooltipHandler.TipRegion(dateRect, CachedTipSignal);
                }

                curBaseY -= dateRect.height;
            }
            catch
            { }
        }
    }

    // ====================================================================================
    // Оптимізація текстурних атласів пешок (GlobalTextureAtlasManager)
    // ====================================================================================

    [HarmonyPatch(typeof(GlobalTextureAtlasManager))]
    [HarmonyPatch("GlobalTextureAtlasManagerUpdate")]
    internal class GlobalTextureAtlasManager_GlobalTextureAtlasManagerUpdate_Patch
    {
        private static List<PawnTextureAtlas> pawnTextureAtlases;

        [HarmonyPrefix]
        public static bool Prefix()
        {
            // ОПТИМІЗАЦІЯ: швидкий пошук поля виконується лише 1 раз при першому виклику
            if (pawnTextureAtlases == null)
            {
                pawnTextureAtlases = AccessTools.Field(typeof(GlobalTextureAtlasManager), "pawnTextureAtlases")
                    ?.GetValue(null) as List<PawnTextureAtlas>;
            }

            if (GlobalTextureAtlasManager.rebakeAtlas)
            {
                GlobalTextureAtlasManager.FreeAllRuntimeAtlases();
                PortraitsCache.Clear();
                GlobalTextureAtlasManager.rebakeAtlas = false;
            }

            if (pawnTextureAtlases == null) return false;

            // ОПТИМІЗАЦІЯ: цикл for замість foreach для усунення щокадрових алокацій
            for (int i = 0; i < pawnTextureAtlases.Count; i++)
            {
                var pawnTextureAtlase = pawnTextureAtlases[i];
                try
                {
                    pawnTextureAtlase.GC();
                }
                catch (Exception exp)
                {
                    var that = Traverse.Create(pawnTextureAtlase);
                    var _frameAssignments = that.Field("frameAssignments").GetValue<Dictionary<Pawn, PawnTextureAtlasFrameSet>>();
                    if (_frameAssignments != null)
                    {
                        var test = new Dictionary<Pawn, PawnTextureAtlasFrameSet>(_frameAssignments);
                        that.Field("frameAssignments").SetValue(test);

                        Log.Message("Exception " + exp.Message + " Replace frameAssignments: "
                            + _frameAssignments.Keys.Aggregate("", (r, k) => r + Environment.NewLine + $"{k.LabelCap} hc{k.GetHashCode()} id{k.thingIDNumber}"));
                    }
                }
            }
            return false;
        }
    }

    // ====================================================================================
    // Кнопки взаємодії з біржею (Gizmo) для поселень та караванів
    // ====================================================================================

    [HarmonyPatch(typeof(Settlement))]
    [HarmonyPatch("GetGizmos")]
    internal class Settlement_GetGizmos_Patch
    {
        private static string CachedLabel;
        private static string Label => CachedLabel ?? (CachedLabel = "OCity_Dialog_Exchenge_Trade_Orders".Translate());

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Settlement __instance)
        {
            foreach (var value in values) yield return value;

            if (!SessionClient.Get.IsLogined) yield break;
            if (__instance.Faction == null || !__instance.Faction.IsPlayer) yield break;
            if (SessionClientController.Data?.GeneralSettings != null && !SessionClientController.Data.GeneralSettings.ExchengeEnable) yield break;

            var command_Action = new Command_Action
            {
                defaultLabel = Label,
                defaultDesc = Label,
                icon = GeneralTexture.TradeButtonIcon,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_Exchenge(__instance));
                }
            };
            yield return command_Action;
        }
    }

    [HarmonyPatch(typeof(Caravan))]
    [HarmonyPatch("GetGizmos")]
    internal class Caravan_GetGizmos_Patch
    {
        private static string CachedLabel;
        private static string Label => CachedLabel ?? (CachedLabel = "OCity_Dialog_Exchenge_Trade_Orders".Translate());

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Caravan __instance)
        {
            foreach (var value in values) yield return value;

            if (!SessionClient.Get.IsLogined) yield break;
            if (__instance.Faction == null || !__instance.Faction.IsPlayer) yield break;
            if (SessionClientController.Data?.GeneralSettings != null && !SessionClientController.Data.GeneralSettings.ExchengeEnable) yield break;

            var command_Action = new Command_Action
            {
                defaultLabel = Label,
                defaultDesc = Label,
                icon = GeneralTexture.TradeButtonIcon,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_Exchenge(__instance));
                }
            };
            yield return command_Action;
        }
    }

    // ====================================================================================
    // Розрахунок вартості майна колонії для оповідача подій
    // ====================================================================================

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch("PlayerWealthForStoryteller", MethodType.Getter)]
    internal class Map_PlayerWealthForStoryteller_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance, ref float __result)
        {
            if (Current.Game == null) return;
            if (!SessionClient.Get.IsLogined) return;

            if (__instance == null) return;
            if (MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth == null) return;
            if (!MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth.TryGetValue(__instance, out var wealth)) return;
            __result += wealth;
        }
    }

    [HarmonyPatch(typeof(WealthWatcher))]
    [HarmonyPatch("WealthTotal", MethodType.Getter)]
    internal class WealthWatcher_WealthTotal_Patch
    {
        private static readonly AccessTools.FieldRef<WealthWatcher, Map> WealthWatcherMapRef =
            AccessTools.FieldRefAccess<WealthWatcher, Map>("map");

        [HarmonyPostfix]
        public static void Postfix(WealthWatcher __instance, ref float __result)
        {
            if (Current.Game == null) return;
            if (!SessionClient.Get.IsLogined) return;

            var map = WealthWatcherMapRef(__instance);
            if (map == null) return;

            if (MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth == null) return;
            if (!MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth.TryGetValue(map, out var wealth)) return;
            __result += wealth;
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_History))]
    [HarmonyPatch("DoStatisticsPage")]
    internal class MainTabWindow_DoStatisticsPage_Patch
    {
        public static Dictionary<Map, float> PatchColonyWealth;

        public static string PatchInject1()
        {
            if (Current.Game == null) return "";
            if (!SessionClient.Get.IsLogined) return "";

            if (Find.CurrentMap == null) return "";
            if (PatchColonyWealth == null) return "";
            if (!PatchColonyWealth.TryGetValue(Find.CurrentMap, out var wealth)) return "";
            return "Online City: " + wealth.ToString("F0");
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> InjectCustomQuickstartSettings(IEnumerable<CodeInstruction> instructions)
        {
            var stringBuilderAppendLine = AccessTools.Method(typeof(StringBuilder), "AppendLine", new Type[] { typeof(string) });
            var mainTabWindow_DoStatisticsPage_Patch_PatchInject1 = AccessTools.Method(typeof(MainTabWindow_DoStatisticsPage_Patch), "PatchInject1");

            int state = 0;
            var codes = new List<CodeInstruction>(instructions);
            foreach (var code in codes)
            {
                yield return code;
                if (state == 0 && code?.operand?.ToString() == "ThisMapColonyWealthColonistsAndTameAnimals") state = 1;
                if (state == 1 && code?.opcode == OpCodes.Ldloc_0)
                {
                    state = 2;
                    yield return new CodeInstruction(OpCodes.Call, mainTabWindow_DoStatisticsPage_Patch_PatchInject1);
                    yield return new CodeInstruction(OpCodes.Callvirt, stringBuilderAppendLine);
                    yield return new CodeInstruction(OpCodes.Pop);
                    yield return new CodeInstruction(OpCodes.Ldloc_0);
                }
            }
        }
    }

    // ====================================================================================
    // Автоматична передача вмісту транспортних капсул у біржовий склад
    // ====================================================================================

    [HarmonyPatch(typeof(TravelingTransportPods))]
    [HarmonyPatch("DoArrivalAction")]
    internal class TravelingTransportPods_DoArrivalAction_Patch
    {
        private static readonly AccessTools.FieldRef<TravelingTransportPods, List<ActiveDropPodInfo>> PodsRef =
            AccessTools.FieldRefAccess<TravelingTransportPods, List<ActiveDropPodInfo>>("pods");

        [HarmonyPrefix]
        public static bool Prefix(TravelingTransportPods __instance)
        {
            if (Current.Game == null) return true;
            if (!SessionClient.Get.IsLogined) return true;

            if (__instance.arrivalAction != null) return true;
            if (__instance.destinationTile < 0) return true;

            Loger.Log("Client TravelingTransportPods SaveGame and ExchengeStorage 1", Loger.LogLevel.EXCHANGE);

            var pods = PodsRef(__instance);
            if (pods == null || pods.Count == 0) return true;

            var toTargetThing = new List<Thing>();
            for (int j = 0; j < pods.Count; j++)
            {
                var container = pods[j].innerContainer;
                for (int k = 0; k < container.Count; k++)
                {
                    toTargetThing.Add(container[k]);
                }
            }

            var filteredThings = toTargetThing.FilterBeforeSendServer();
            var toTargetEntry = new List<ThingTrade>(filteredThings.Count);
            for (int i = 0; i < filteredThings.Count; i++)
            {
                var t = filteredThings[i];
                toTargetEntry.Add(ThingTrade.CreateTrade(t, t.stackCount));
            }

            Loger.Log("Client TravelingTransportPods SaveGame and ExchengeStorage 2", Loger.LogLevel.EXCHANGE);

            SessionClientController.SaveGameNowSingleAndCommandSafely(
                (connect) =>
                {
                    Loger.Log("Client TravelingTransportPods SaveGame and ExchengeStorage 3", Loger.LogLevel.EXCHANGE);
                    return connect.ExchengeStorage(toTargetEntry, null, __instance.destinationTile);
                },
                () =>
                {
                    var msg = "OCity_DialogExchenge_ToStorage".Translate();
                    Find.WindowStack.Add(new Dialog_Input("OCity_Dialog_Exchenge_Action_CarriedOut".Translate(), msg, true));
                },
                null,
                false);

            return true;
        }
    }
}