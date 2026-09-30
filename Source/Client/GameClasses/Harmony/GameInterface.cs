using HarmonyLib;
using System;
using System.Collections.Generic;

namespace RimWorldOnlineCity.GameClasses
{
    internal static class GameInterfaceHelper
    {
        private static readonly Dictionary<string, string> PlayerIconKeyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        public static Texture2D GetPlayerIcon(string login)
        {
            if (string.IsNullOrEmpty(login)) return null;
            var key = GetPlayerIconKey(login);
            var icon = GeneralTexture.Get?.ByName(key);
            return (icon != null && icon != GeneralTexture.Null) ? icon : null;
        }
    }

    [HarmonyPatch(typeof(StatsReportUtility))]
    [HarmonyPatch("DrawStatsWorker")]
    internal static class StatsReportUtility_DrawStatsWorker_Patch
    {
        private static string CachedInsertChatText;
        private static string InsertChatText => CachedInsertChatText ?? (CachedInsertChatText = "OCity_GameInterface_InsertIntoChat".Translate());

        [HarmonyPrefix]
        public static bool Prefix(ref Rect rect, Thing optionalThing, WorldObject optionalWorldObject)
        {
            if (SessionClient.Get?.IsLogined != true) return true;

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

            if (Widgets.ButtonImage(iconCopy, GeneralTexture.OCToChat))
            {
                if (optionalThing != null)
                {
                    var msg = $"<!{optionalThing.def?.defName ?? "Thing"}/>";
                    ChatController.AddToInputChat(msg, true);
                }
                else
                {
                    long? serverId = (optionalWorldObject as WorldObjectBaseOnline)?.Place?.PlaceServerId;
                    if (serverId != null)
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
            }

            if (optionalThing != null) return true;

            string ownerLogin = optionalWorldObject is CaravanOnline co
                ? co.OnlinePlayerLogin
                : (optionalWorldObject is BaseOnline bo ? bo.OnlinePlayerLogin : null);

            if (string.IsNullOrEmpty(ownerLogin)) return true;

            const float size = 100f;
            var iconArea = new Rect(rect.width - size, iconCopy.y + iconCopy.height, size, size);

            var iconImage = GameInterfaceHelper.GetPlayerIcon(ownerLogin);
            if (iconImage != null)
            {
                GUI.DrawTexture(iconArea, iconImage);
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RimWorld.InspectPaneFiller))]
    [HarmonyPatch("DrawInspectStringFor")]
    internal static class InspectPaneFiller_DrawInspectStringFor_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ISelectable sel, ref Rect rect)
        {
            if (SessionClient.Get?.IsLogined != true) return true;

            string ownerLogin = sel is CaravanOnline co
                ? co.OnlinePlayerLogin
                : (sel is BaseOnline bo ? bo.OnlinePlayerLogin : null);

            if (!string.IsNullOrEmpty(ownerLogin))
            {
                var iconImage = GameInterfaceHelper.GetPlayerIcon(ownerLogin);
                if (iconImage != null)
                {
                    const float size = 100f;
                    var iconArea = new Rect(rect.width - size, 0f, size, size);
                    GUI.DrawTexture(iconArea, iconImage);
                    rect.width -= iconArea.width;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Inspect))]
    [HarmonyPatch("DoInspectPaneButtons")]
    internal static class MainTabWindow_Inspect_DoInspectPaneButtons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, ref float lineEndWidth)
        {
            if (SessionClient.Get?.IsLogined != true) return;
            if (Find.Selector.NumSelected != 1) return;

            Thing singleSelectedThing = Find.Selector.SingleSelectedThing;
            if (singleSelectedThing?.def == null) return;

            lineEndWidth += 30f;
            var iconCopy = new Rect(rect.width - lineEndWidth, -2f, 30f, 30f);
            if (Widgets.ButtonImage(iconCopy, GeneralTexture.OCToChat))
            {
                var msg = $"<!{singleSelectedThing.def.defName}/>";
                ChatController.AddToInputChat(msg, true);
            }
        }
    }

    [HarmonyPatch(typeof(WorldInspectPane))]
    [HarmonyPatch("DoInspectPaneButtons")]
    internal static class WorldInspectPane_DoInspectPaneButtons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, ref float lineEndWidth)
        {
            if (SessionClient.Get?.IsLogined != true) return;

            WorldObject singleSelectedObject = Find.WorldSelector.SingleSelectedObject;
            if (singleSelectedObject != null || Find.WorldSelector.selectedTile >= 0)
            {
                lineEndWidth += 30f;
                var iconCopy = new Rect(rect.width - lineEndWidth, -2f, 30f, 30f);
                if (Widgets.ButtonImage(iconCopy, GeneralTexture.OCToChat))
                {
                    long? serverId = (singleSelectedObject as WorldObjectBaseOnline)?.Place?.PlaceServerId;

                    if (serverId == null && singleSelectedObject?.Faction?.IsPlayer == true)
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