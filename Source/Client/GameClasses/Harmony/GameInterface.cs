using HarmonyLib;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity.GameClasses
{
    /// <summary>
    /// Гармоні-патчі для інтеграції елементів мережевого інтерфейсу OnlineCity
    /// в інформаційні картки (Dialog_InfoCard) та панелі інспектора (InspectPane).
    /// </summary>
    internal static class GameInterfaceHelper
    {
        private static readonly Dictionary<string, string> PlayerIconKeyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Повертає кешований ідентифікатор аватарки гравця, запобігаючи створенню нових рядків щокадру.
        /// </summary>
        public static string GetPlayerIconKey(string login)
        {
            if (string.IsNullOrEmpty(login)) return string.Empty;
            if (!PlayerIconKeyCache.TryGetValue(login, out var key))
            {
                key = "pl_" + login;
                PlayerIconKeyCache[login] = key;
            }
            return key;
        }
    }

    /// <summary>
    /// Додає кнопку вставки тегу об'єкта в чат та аватарку власника в інформаційну картку об'єкта ("i").
    /// </summary>
    [HarmonyPatch(typeof(StatsReportUtility))]
    [HarmonyPatch("DrawStatsWorker")]
    internal class StatsReportUtility_DrawStatsWorker_Patch
    {
        private static string CachedInsertChatText;
        private static string InsertChatText => CachedInsertChatText ?? (CachedInsertChatText = "OCity_GameInterface_InsertIntoChat".Translate());

        [HarmonyPrefix]
        public static bool Prefix(ref Rect rect, Thing optionalThing, WorldObject optionalWorldObject)
        {
            if (!SessionClient.Get.IsLogined) return true;

            var iconCopy = new Rect(rect.width - 32f, 18f, 32f, 32f);

            var txt = InsertChatText;
            var font = Text.Font;
            Text.Font = GameFont.Small;
            var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(iconCopy.x - 153f, iconCopy.y, 150f, iconCopy.height), txt);
            TooltipHandler.TipRegion(iconCopy, txt);
            Text.Anchor = anchor;
            Text.Font = font;

            var serverId = (optionalWorldObject as WorldObjectBaseOnline)?.Place?.PlaceServerId;

            // Кнопка копіювання посилання на предмет, базу чи тайл у рядок вводу чату
            if (Widgets.ButtonImage(iconCopy, GeneralTexture.OCToChat))
            {
                if (optionalThing != null)
                {
                    var msg = $"<!{optionalThing.def.defName}/>";
                    ChatController.AddToInputChat(msg, true);
                }
                else if (serverId != null)
                {
                    var msg = $"<&{serverId.Value}/>";
                    ChatController.AddToInputChat(msg, true);
                }
                else if (optionalWorldObject != null)
                {
                    int tile = optionalWorldObject.Tile;
                    var msg = $"<#{tile}/>";
                    ChatController.AddToInputChat(msg, true);
                }
            }

            if (optionalThing != null) return true;
            if (!(optionalWorldObject is CaravanOnline OCWO) || string.IsNullOrEmpty(OCWO.OnlinePlayerLogin)) return true;

            const float size = 100f;
            var iconArea = new Rect(rect.width - size, iconCopy.y + iconCopy.height, size, size);

            // ОПТИМІЗАЦІЯ: використання кешованого ключа аватарки
            var iconImage = GeneralTexture.Get.ByName(GameInterfaceHelper.GetPlayerIconKey(OCWO.OnlinePlayerLogin));
            if (iconImage != null && iconImage != GeneralTexture.Null)
            {
                GUI.DrawTexture(iconArea, iconImage);
            }

            return true;
        }
    }

    /// <summary>
    /// Відображає аватарку гравця на панелі огляду його каравану чи бази.
    /// </summary>
    [HarmonyPatch(typeof(RimWorld.InspectPaneFiller))]
    [HarmonyPatch("DrawInspectStringFor")]
    internal class InspectPaneFiller_DrawInspectStringFor_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ISelectable sel, ref Rect rect)
        {
            if (!SessionClient.Get.IsLogined) return true;

            if (sel is CaravanOnline OCWO && !string.IsNullOrEmpty(OCWO.OnlinePlayerLogin))
            {
                const float size = 100f;
                var iconArea = new Rect(rect.width - size, 0f, size, size);

                var iconImage = GeneralTexture.Get.ByName(GameInterfaceHelper.GetPlayerIconKey(OCWO.OnlinePlayerLogin));
                if (iconImage != null && iconImage != GeneralTexture.Null)
                {
                    GUI.DrawTexture(iconArea, iconImage);
                    rect.width -= iconArea.width;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Додає кнопку вставки виділеного предмета в чат на панелі інспектора карти.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect))]
    [HarmonyPatch("DoInspectPaneButtons")]
    internal class MainTabWindow_Inspect_DoInspectPaneButtons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, ref float lineEndWidth)
        {
            if (!SessionClient.Get.IsLogined) return;
            if (Find.Selector.NumSelected != 1) return;

            Thing singleSelectedThing = Find.Selector.SingleSelectedThing;
            if (singleSelectedThing == null) return;

            lineEndWidth += 30f;
            var iconCopy = new Rect(rect.width - lineEndWidth, -2f, 30f, 30f);
            if (Widgets.ButtonImage(iconCopy, GeneralTexture.OCToChat))
            {
                var msg = $"<!{singleSelectedThing.def.defName}/>";
                ChatController.AddToInputChat(msg, true);
            }
        }
    }

    /// <summary>
    /// Додає кнопку вставки виділеного об'єкта або тайла планети в чат на панелі інспектора планети.
    /// </summary>
    [HarmonyPatch(typeof(WorldInspectPane))]
    [HarmonyPatch("DoInspectPaneButtons")]
    internal class WorldInspectPane_DoInspectPaneButtons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, ref float lineEndWidth)
        {
            if (!SessionClient.Get.IsLogined) return;

            WorldObject singleSelectedObject = Find.WorldSelector.SingleSelectedObject;
            if (singleSelectedObject != null || Find.WorldSelector.selectedTile >= 0)
            {
                lineEndWidth += 30f;
                var iconCopy = new Rect(rect.width - lineEndWidth, -2f, 30f, 30f);
                if (Widgets.ButtonImage(iconCopy, GeneralTexture.OCToChat))
                {
                    long? serverId = (singleSelectedObject as WorldObjectBaseOnline)?.Place?.PlaceServerId;

                    // ОПТИМІЗАЦІЯ: безпечна перевірка на null у Faction?.IsPlayer
                    if (serverId == null && singleSelectedObject != null && singleSelectedObject.Faction?.IsPlayer == true)
                    {
                        serverId = UpdateWorldController.GetMyByLocalId(singleSelectedObject.ID)?.PlaceServerId;
                    }

                    if (serverId != null)
                    {
                        var msg = $"<&{serverId.Value}/>";
                        ChatController.AddToInputChat(msg, true);
                    }
                    else
                    {
                        int tile = singleSelectedObject != null ? singleSelectedObject.Tile : Find.WorldSelector.selectedTile;
                        var msg = $"<#{tile}/>";
                        ChatController.AddToInputChat(msg, true);
                    }
                }
            }
        }
    }
}