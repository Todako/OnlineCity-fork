using HarmonyLib;
using Model;
using OCUnion;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace RimWorldOnlineCity.GameClasses.Harmony
{
    // ====================================================================================
    // Контроль режиму розробника (DevMode)
    // ====================================================================================

    [HarmonyPatch(typeof(PrefsData))]
    [HarmonyPatch("Apply")]
    internal static class PrefsData_Apply_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Current.Game == null) return;
            if (SessionClient.Get?.IsLogined != true) return;

            if (SessionClientController.Data?.DisableDevMode == true)
            {
                if (Prefs.DevMode) Prefs.DevMode = false;
                if (IdeoUIUtility.devEditMode) IdeoUIUtility.devEditMode = false;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_Options))]
    [HarmonyPatch("DoModOptions")]
    internal static class Dialog_Options_DoModOptions_Patch
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
            if (SessionClient.Get?.IsLogined != true) return true;

            return SessionClientController.Data?.DisableDevMode != true;
        }
    }

    [HarmonyPatch(typeof(Dialog_CreateXenotype))]
    [HarmonyPatch("PostXenotypeOnGUI")]
    internal static class Dialog_CreateXenotype_PostXenotypeOnGUI_Patch
    {
        private static readonly AccessTools.FieldRef<Dialog_CreateXenotype, bool> IgnoreRestrictionsRef =
            AccessTools.FieldRefAccess<Dialog_CreateXenotype, bool>("ignoreRestrictions");

        [HarmonyPostfix]
        public static void Postfix(Dialog_CreateXenotype __instance)
        {
            if (Current.Game == null) return;
            if (SessionClient.Get?.IsLogined != true) return;
            if (SessionClientController.Data?.DisableDevMode != true) return;

            IgnoreRestrictionsRef(__instance) = false;
        }
    }

    [HarmonyPatch(typeof(DebugTool))]
    [HarmonyPatch("DebugToolOnGUI")]
    internal static class DebugTool_DebugToolOnGUI_Patch
    {
        private static DateTime LastCheck = DateTime.MinValue;

        [HarmonyPrefix]
        public static bool Prefix()
        {
            if ((DateTime.UtcNow - LastCheck).TotalSeconds < 5) return true;
            LastCheck = DateTime.UtcNow;

            if (Current.Game == null) return true;
            if (SessionClient.Get?.IsLogined != true) return true;
            if (SessionClientController.Data?.DisableDevMode != true) return true;

            Loger.TransLog("ShowDevMode");
            return true;
        }
    }

    // ====================================================================================
    // Захист налаштувань оповідача та сторонніх модулів (HugsLib)
    // ====================================================================================

    [HarmonyPatch(typeof(Page_SelectStorytellerInGame))]
    [HarmonyPatch("DoWindowContents")]
    internal static class Page_SelectStorytellerInGame_DoWindowContents_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page_SelectStorytellerInGame __instance)
        {
            if (Current.Game == null) return true;
            if (SessionClient.Get?.IsLogined != true) return true;
            if (Prefs.DevMode) return true;

            // ВИПРАВЛЕНО CS0023: прямий доступ до поля структури GeneralSettings
            if (SessionClientController.Data != null && SessionClientController.Data.GeneralSettings.DisableGameSettings)
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
    internal static class HugsLibUtility_OpenModSettingsDialog_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (Current.Game == null) return true;
            if (SessionClient.Get?.IsLogined != true) return true;
            if (Prefs.DevMode) return true;

            // ВИПРАВЛЕНО CS0023: прямий доступ до поля структури GeneralSettings
            if (SessionClientController.Data != null && SessionClientController.Data.GeneralSettings.DisableGameSettings)
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
    internal static class GenDatePatch
    {
        public static void Postfix(long absTicks, float longitude, ref int __result)
        {
            if (SessionClient.Get?.IsLogined != true) return;
            if (SessionClientController.Data == null) return;

            int needYear = SessionClientController.Data.GeneralSettings.StartGameYear;
            if (needYear < 0 || needYear == 5500) return;

            long longAdj = GenDate.TimeZoneAt(longitude) * 2500L;
            __result = needYear + (int)((absTicks + longAdj) / 3600000L);
        }
    }

    [HarmonyPatch(typeof(CrossRefHandler))]
    [HarmonyPatch("ResolveAllCrossReferences")]
    public static class CrossRefHandler_ResolveAllCrossReferences_Patch
    {
        private static readonly HashSet<IExposable> s_ExistingCrossRefsBuffer = new HashSet<IExposable>();

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
                s_ExistingCrossRefsBuffer.Clear();
                for (int i = 0; i < crossRefs.Count; i++)
                {
                    s_ExistingCrossRefsBuffer.Add(crossRefs[i]);
                }

                for (int i = 0; i < toAdd.Count; i++)
                {
                    var item = toAdd[i];
                    if (s_ExistingCrossRefsBuffer.Add(item))
                    {
                        crossRefs.Add(item);
                    }
                }

                s_ExistingCrossRefsBuffer.Clear();
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
    public static class GlobalControlsUtility_DoDate_Patch
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
            if (SessionClient.Get?.IsLogined != true) return;
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
    internal static class GlobalTextureAtlasManager_GlobalTextureAtlasManagerUpdate_Patch
    {
        private static List<PawnTextureAtlas> pawnTextureAtlases;
        private static readonly FieldInfo FrameAssignmentsField =
            AccessTools.Field(typeof(PawnTextureAtlas), "frameAssignments");

        [HarmonyPrefix]
        public static bool Prefix()
        {
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

            for (int i = 0; i < pawnTextureAtlases.Count; i++)
            {
                var pawnTextureAtlase = pawnTextureAtlases[i];
                try
                {
                    pawnTextureAtlase.GC();
                }
                catch (Exception exp)
                {
                    var assignments = FrameAssignmentsField?.GetValue(pawnTextureAtlase) as Dictionary<Pawn, PawnTextureAtlasFrameSet>;
                    if (assignments != null)
                    {
                        var replacement = new Dictionary<Pawn, PawnTextureAtlasFrameSet>(assignments);
                        FrameAssignmentsField.SetValue(pawnTextureAtlase, replacement);

                        Log.Message("Exception " + exp.Message + " Replace frameAssignments: "
                            + assignments.Keys.Aggregate("", (r, k) => r + Environment.NewLine + $"{k.LabelCap} hc{k.GetHashCode()} id{k.thingIDNumber}"));
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
    internal static class Settlement_GetGizmos_Patch
    {
        private static string CachedLabel;
        private static string Label => CachedLabel ?? (CachedLabel = "OCity_Dialog_Exchenge_Trade_Orders".Translate());

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Settlement __instance)
        {
            foreach (var value in values) yield return value;

            if (SessionClient.Get?.IsLogined != true) yield break;
            if (__instance.Faction == null || !__instance.Faction.IsPlayer) yield break;

            // ВИПРАВЛЕНО CS0023: перевірка Data на null і прямий доступ до структури GeneralSettings
            if (SessionClientController.Data == null || !SessionClientController.Data.GeneralSettings.ExchengeEnable) yield break;

            yield return new Command_Action
            {
                defaultLabel = Label,
                defaultDesc = Label,
                icon = GeneralTexture.TradeButtonIcon,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_Exchenge(__instance));
                }
            };
        }
    }

    [HarmonyPatch(typeof(Caravan))]
    [HarmonyPatch("GetGizmos")]
    internal static class Caravan_GetGizmos_Patch
    {
        private static string CachedLabel;
        private static string Label => CachedLabel ?? (CachedLabel = "OCity_Dialog_Exchenge_Trade_Orders".Translate());

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Caravan __instance)
        {
            foreach (var value in values) yield return value;

            if (SessionClient.Get?.IsLogined != true) yield break;
            if (__instance.Faction == null || !__instance.Faction.IsPlayer) yield break;

            // ВИПРАВЛЕНО CS0023: перевірка Data на null і прямий доступ до структури GeneralSettings
            if (SessionClientController.Data == null || !SessionClientController.Data.GeneralSettings.ExchengeEnable) yield break;

            yield return new Command_Action
            {
                defaultLabel = Label,
                defaultDesc = Label,
                icon = GeneralTexture.TradeButtonIcon,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_Exchenge(__instance));
                }
            };
        }
    }

    // ====================================================================================
    // Розрахунок вартості майна колонії для оповідача подій
    // ====================================================================================

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch("PlayerWealthForStoryteller", MethodType.Getter)]
    internal static class Map_PlayerWealthForStoryteller_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance, ref float __result)
        {
            if (Current.Game == null || __instance == null) return;
            if (SessionClient.Get?.IsLogined != true) return;

            if (MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth != null
                && MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth.TryGetValue(__instance, out var wealth))
            {
                __result += wealth;
            }
        }
    }

    [HarmonyPatch(typeof(WealthWatcher))]
    [HarmonyPatch("WealthTotal", MethodType.Getter)]
    internal static class WealthWatcher_WealthTotal_Patch
    {
        private static readonly AccessTools.FieldRef<WealthWatcher, Map> WealthWatcherMapRef =
            AccessTools.FieldRefAccess<WealthWatcher, Map>("map");

        [HarmonyPostfix]
        public static void Postfix(WealthWatcher __instance, ref float __result)
        {
            if (Current.Game == null || __instance == null) return;
            if (SessionClient.Get?.IsLogined != true) return;

            var map = WealthWatcherMapRef(__instance);
            if (map != null && MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth != null
                && MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth.TryGetValue(map, out var wealth))
            {
                __result += wealth;
            }
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_History))]
    [HarmonyPatch("DoStatisticsPage")]
    internal static class MainTabWindow_DoStatisticsPage_Patch
    {
        public static Dictionary<Map, float> PatchColonyWealth;

        public static string PatchInject1()
        {
            if (Current.Game == null) return string.Empty;
            if (SessionClient.Get?.IsLogined != true) return string.Empty;

            var currentMap = Find.CurrentMap;
            if (currentMap == null || PatchColonyWealth == null) return string.Empty;
            if (!PatchColonyWealth.TryGetValue(currentMap, out var wealth)) return string.Empty;

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
    internal static class TravelingTransportPods_DoArrivalAction_Patch
    {
        private static readonly AccessTools.FieldRef<TravelingTransportPods, List<ActiveDropPodInfo>> PodsRef =
            AccessTools.FieldRefAccess<TravelingTransportPods, List<ActiveDropPodInfo>>("pods");

        [HarmonyPrefix]
        public static bool Prefix(TravelingTransportPods __instance)
        {
            if (Current.Game == null) return true;
            if (SessionClient.Get?.IsLogined != true) return true;

            if (__instance.arrivalAction != null) return true;
            if (__instance.destinationTile < 0) return true;

            var pods = PodsRef(__instance);
            if (pods == null || pods.Count == 0) return true;

            int estimatedCount = 0;
            for (int j = 0; j < pods.Count; j++)
            {
                estimatedCount += pods[j].innerContainer?.Count ?? 0;
            }

            var toTargetThing = new List<Thing>(estimatedCount);
            for (int j = 0; j < pods.Count; j++)
            {
                var container = pods[j].innerContainer;
                if (container != null)
                {
                    for (int k = 0; k < container.Count; k++)
                    {
                        toTargetThing.Add(container[k]);
                    }
                }
            }

            var filteredThings = toTargetThing.FilterBeforeSendServer();
            var toTargetEntry = new List<ThingTrade>(filteredThings.Count);
            for (int i = 0; i < filteredThings.Count; i++)
            {
                var t = filteredThings[i];
                toTargetEntry.Add(ThingTrade.CreateTrade(t, t.stackCount));
            }

            SessionClientController.SaveGameNowSingleAndCommandSafely(
                (connect) =>
                {
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