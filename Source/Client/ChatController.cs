using Model;
using OCUnion;
using OCUnion.Common;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Transfer;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Контролер обробки чату, текстових тегів, локалізації повідомлень сервера та команд.
    /// </summary>
    internal static class ChatController
    {
        private static bool InOnlineGame;
        internal static PanelChat MainPanelChat;

        /// <summary>
        /// Ініціалізація обробників чату.
        /// </summary>
        public static void Init(bool inOnlineGame)
        {
            var connect = SessionClient.Get;
            InOnlineGame = inOnlineGame;
            connect.OnPostingChatAfter = After;
            connect.OnPostingChatBefore = Before;
        }

        public static void AddToInputChat(string text, bool activeChat = false)
        {
            if (activeChat) Dialog_MainOnlineCity.ShowChat();
            if (MainPanelChat == null) return;
            if (MainPanelChat.ChatInputText == null) MainPanelChat.ChatInputText = "";
            MainPanelChat.ChatInputText += (MainPanelChat.ChatInputText.Length == 0 || MainPanelChat.ChatInputText[MainPanelChat.ChatInputText.Length - 1] == ' ' ? "" : " ")
                + text;
        }

        public static string ServerTranslate(this string textChat, bool onlyTranslate = false)
            => ServerCharTranslate(textChat, onlyTranslate);

        /// <summary>
        /// Локалізація серверних ключів OC_ у тексті повідомлення.
        /// ОПТИМІЗАЦІЯ: швидкий вихід, якщо префікса OC_ немає; відмова від 8 проміжних Replace().
        /// </summary>
        public static string ServerCharTranslate(string textChat, bool onlyTranslate = false)
        {
            if (string.IsNullOrEmpty(textChat)) return textChat;

            if (!onlyTranslate) textChat = PrepareShortTag(textChat);

            int pos = textChat.IndexOf("OC_", StringComparison.Ordinal);
            if (pos < 0) return textChat; // Якщо ключів перекладу немає — нуль виділень пам'яті

            var sb = new StringBuilder(textChat.Length + 32);
            int lastPos = 0;

            while (pos >= 0)
            {
                sb.Append(textChat, lastPos, pos - lastPos);

                // Знаходження кінця токена без створення копій рядків
                int ep = pos + 3;
                while (ep < textChat.Length && !IsTokenDelimiter(textChat[ep]))
                {
                    ep++;
                }

                var sub = textChat.Substring(pos, ep - pos);
                var tr = sub.Translate().ToString();

                if (!tr.StartsWith("OC_", StringComparison.Ordinal))
                {
                    sb.Append(tr);
                }
                else
                {
                    sb.Append(sub);
                }

                lastPos = ep;
                pos = textChat.IndexOf("OC_", lastPos, StringComparison.Ordinal);
            }

            if (lastPos < textChat.Length)
            {
                sb.Append(textChat, lastPos, textChat.Length - lastPos);
            }

            return sb.ToString();
        }

        private static bool IsTokenDelimiter(char c)
        {
            return c == ' ' || c == '\r' || c == '\n' || c == '\t' || c == ',' || c == '.' || c == ':' || c == '*' || c == '<' || c == '>';
        }

        /// <summary>
        /// Розбір та перетворення коротких тегів у формат розмітки RichText.
        /// ОПТИМІЗАЦІЯ: однопрохідна збірка через StringBuilder без повторних Substring() + Substring().
        /// </summary>
        public static string PrepareShortTag(string textChat)
        {
            if (string.IsNullOrEmpty(textChat) || textChat.IndexOf('<') < 0) return textChat;

            var sb = new StringBuilder(textChat.Length + 64);
            int lastPos = 0;
            int current = 0;

            while (current < textChat.Length)
            {
                int pos = textChat.IndexOf('<', current);
                if (pos < 0 || textChat.Length < pos + 4)
                {
                    break;
                }

                int posE = textChat.IndexOf('>', pos);
                if (posE < 0 || posE - pos < 3)
                {
                    current = pos + 1;
                    continue;
                }

                // Перевірка коментаря: <!-- без виділення підрядків
                if (pos + 2 < textChat.Length && textChat[pos + 1] == '!' && textChat[pos + 2] == '-')
                {
                    current = posE + 1;
                    continue;
                }

                char tagType = textChat[pos + 1];
                bool isSelfClosing = textChat[posE - 1] == '/';
                int contentStart = pos + 2;
                int contentLength = posE - contentStart - (isSelfClosing ? 1 : 0);

                if (contentLength <= 0)
                {
                    current = posE + 1;
                    continue;
                }

                string content = textChat.Substring(contentStart, contentLength);
                string replace = null;

                switch (tagType)
                {
                    case ':': replace = ShortTagEmoji(content); break;
                    case '@': replace = ShortTagPlayer(content); break;
                    case '#': replace = ShortTagTile(content); break;
                    case '!': replace = ShortTagDef(content); break;
                    case '&': replace = ShortTagServerId(content); break;
                }

                if (replace != null)
                {
                    sb.Append(textChat, lastPos, pos - lastPos);
                    sb.Append(replace);
                    lastPos = posE + 1;
                    current = lastPos;
                }
                else
                {
                    current = pos + 1;
                }
            }

            // Якщо жодного тегу не замінено — повертаємо оригінальний рядок
            if (lastPos == 0)
            {
                return textChat;
            }

            if (lastPos < textChat.Length)
            {
                sb.Append(textChat, lastPos, textChat.Length - lastPos);
            }

            return sb.ToString();
        }

        private static string ShortTagEmoji(string content)
        {
            if (content.EndsWith(":")) content = content.Substring(0, content.Length - 1);
            content = content.Trim();
            return $"<img Emoji/Emoji_{content}>";
        }

        private static string ShortTagPlayer(string content)
        {
            content = content.Trim();
            return $"<btn name=pl{content} class=player arg={content}><img pl_{content}> " + content + "</btn>";
        }

        private static string ShortTagTile(string content)
        {
            content = content.Trim();
            if (!int.TryParse(content, out int tile)) return null;
            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.tiles.Count) return null;

            var biome = Find.WorldGrid[tile].biome;
            Vector2 vector = Find.WorldGrid.LongLatOf(tile);
            var coor = vector.y.ToStringLatitude() + " " + vector.x.ToStringLongitude();
            var msg = $"<img name=Waypoint />{coor} <l>{biome.defName}.label</l>";
            if (!biome.impassable)
            {
                msg += $" (<l>{GameUtils.GetHillinessLabel(Find.WorldGrid[tile].hilliness)}</l>)";
            }

            return $"<btn name=tile{tile} class=tile d={tile} arg={tile}>" + msg + "</btn>";
        }

        private static string ShortTagDef(string content)
        {
            content = content.Trim();
            return (content == "Human" ? "<img IconHuman />" : $"<img defName={content} />")
                + $"<l>{content}.label</l>";
        }

        private static string ShortTagServerId(string content)
        {
            content = content.Trim();
            if (!int.TryParse(content, out int serverId)) return null;

            var tile = 0;
            string player = null;
            var msg = "<img name=Waypoint />";
            var myObj = UpdateWorldController.GetMyByServerId(serverId);
            if (myObj != null)
            {
                tile = myObj.Tile;
                player = SessionClientController.My.Login;
                msg += "<img ColonyOnExpanding> " + myObj.Name;
            }
            else
            {
                var otherObj = UpdateWorldController.GetOtherByServerIdDirtyRead(serverId);
                if (otherObj == null) return null;

                tile = otherObj.Tile;
                if (otherObj is CaravanOnline bo) player = bo.OnlinePlayerLogin;
                msg += $"<img name={otherObj.ExpandingIconName} /> " + otherObj.LabelCap;
            }

            return $"<btn name=tile&{serverId} class=tile d={tile} arg=&{serverId}>" + msg + "</btn>"
                + (player == null ? "" : " " + ShortTagPlayer(player));
        }

        private static ModelStatus Before(int chatId, string msg)
        {
            var trimmed = msg.TrimStart();
            if (trimmed.StartsWith("/call", StringComparison.OrdinalIgnoreCase))
            {
                return BeforeStartIncident(chatId, msg);
            }
            if (trimmed.StartsWith("/debug", StringComparison.OrdinalIgnoreCase))
            {
                return Debug(chatId, msg);
            }
            return null;
        }

        private static void After(int chatId, string msg, ModelStatus stat)
        {
            if (msg.TrimStart().StartsWith("/call", StringComparison.OrdinalIgnoreCase))
            {
                AfterStartIncident(chatId, msg, stat);
            }
        }

        #region Для налагодження (/debug)
        private static Dictionary<int, Dictionary<string, int>> AllThingsByMaps = null;

        private static void DebugGetThingsByWO(List<Thing> allt, Dictionary<int, Dictionary<string, int>> newThingsByMaps,
            WorldObject worldObject, string local, bool needSave, bool needDiffNew, bool needDiffOld,
            Dictionary<string, int> newThings = null)
        {
            if (newThings == null)
            {
                newThings = GameUtils.TransferableOneWaysToDictionary(GameUtils.DistinctToTransferableOneWays(allt), true)
                    .GroupBy(p =>
                    {
                        var tt = ThingTrade.CreateTrade(p.Key, 1);
                        var stringKey = tt.ToString().Replace(" conc", "") + " " + tt.PawnParam;
                        return stringKey.Trim();
                    })
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
                newThingsByMaps.Add(worldObject.ID, newThings);
            }

            Dictionary<string, int> things;
            if (needDiffNew)
            {
                var oldThings = AllThingsByMaps != null && AllThingsByMaps.ContainsKey(worldObject.ID)
                    ? AllThingsByMaps[worldObject.ID]
                    : new Dictionary<string, int>();
                things = newThings
                    .Select(p => new { p.Key, Value = p.Value - (oldThings.ContainsKey(p.Key) ? oldThings[p.Key] : 0) })
                    .Where(p => p.Value > 0)
                    .ToDictionary(p => p.Key, p => p.Value);
            }
            else if (needDiffOld)
            {
                var oldThings = AllThingsByMaps != null && AllThingsByMaps.ContainsKey(worldObject.ID)
                    ? AllThingsByMaps[worldObject.ID]
                    : new Dictionary<string, int>();
                things = oldThings
                    .Select(p => new { p.Key, Value = p.Value - (newThings.ContainsKey(p.Key) ? newThings[p.Key] : 0) })
                    .Where(p => p.Value > 0)
                    .ToDictionary(p => p.Key, p => p.Value);
            }
            else
            {
                things = newThings;
            }

            var serverId = UpdateWorldController.GetMyByLocalId(worldObject?.ID ?? 0)?.PlaceServerId;
            Loger.Log($"Debug {local} {{ " +
                $"woID={worldObject?.ID} " +
                $"ServerId={serverId} " +
                $"things={things.Count} " +
                $"cntThings={things.Values.Sum()} " +
                $"allPawns={things.Keys.Count(t => t.Contains("gender:"))} " +
                $"pawns={things.Keys.Count(t => t.Contains("Colonist"))} " +
                $"Name={worldObject?.LabelShortCap}");

            if (!needSave || needDiffNew || needDiffOld)
            {
                foreach (var thing in things
                    .OrderBy(t => (t.Key.Contains("gender:") ? "0" : "1") + (t.Key.Contains("Colonist") ? "0" : "1") + t.Key))
                {
                    Loger.Log("Debug    " + thing.Key + " x" + thing.Value);
                }
            }
            Loger.Log($"Debug {local} }}");
        }

        private static ModelStatus Debug(int chatId, string msg)
        {
            var needSave = msg.IndexOf("save", StringComparison.OrdinalIgnoreCase) >= 0;
            var needDiffNew = msg.IndexOf("new", StringComparison.OrdinalIgnoreCase) >= 0;
            var needDiffOld = msg.IndexOf("old", StringComparison.OrdinalIgnoreCase) >= 0;
            Loger.Log("Debug start {{{ " + (needDiffOld ? "diffOld" : "") + (needDiffNew ? "diffNew" : "") + (needSave ? "save" : ""));
            try
            {
                if ((needDiffNew || needDiffOld) && AllThingsByMaps == null)
                {
                    Loger.Log("Debug diff fail - not save list");
                    Loger.Log("Debug finish }}}");
                }
                var newThingsByMaps = new Dictionary<int, Dictionary<string, int>>();

                var allWorldObjects = GameUtils.GetAllWorldObjects();
                var wObjects = allWorldObjects
                    .Where(o => (o.Faction?.IsPlayer ?? false) && (o is Settlement || o is Caravan))
                    .ToList();

                for (int i = 0; i < wObjects.Count; i++)
                {
                    if (!(wObjects[i] is Settlement settlement)) continue;
                    var m = settlement.Map;
                    if (!m.IsPlayerHome) continue;
                    var worldObject = m.Parent;

                    var allt = GameUtils.GetAllThings(m, true, false);
                    DebugGetThingsByWO(allt, newThingsByMaps, worldObject, "Map", needSave, needDiffNew, needDiffOld);
                }
                for (int i = 0; i < wObjects.Count; i++)
                {
                    if (!(wObjects[i] is Caravan caravan)) continue;
                    var allt = GameUtils.GetAllThings(caravan, true, false);
                    DebugGetThingsByWO(allt, newThingsByMaps, caravan, "Caravan", needSave, needDiffNew, needDiffOld);
                }

                if (needDiffOld && AllThingsByMaps != null)
                {
                    var removed = AllThingsByMaps.Keys.Where(k => !wObjects.Any(wo => wo.ID == k)).ToList();
                    foreach (var woID in removed)
                    {
                        DebugGetThingsByWO(null, null, null, "removed WorldObject", false, false, false, AllThingsByMaps[woID]);
                    }
                }

                if (needSave) AllThingsByMaps = newThingsByMaps;
            }
            catch (Exception exp)
            {
                Loger.Log("Debug Exception " + exp);
            }
            Loger.Log("Debug finish }}}");

            return new ModelStatus { Status = 1 };
        }
        #endregion

        #region Виклик інцидентів (/call)
        private static ModelStatus BeforeStartIncident(int chatId, string msg)
        {
            Loger.Log("IncidentLog ChatController.BeforeStartIncident 1 msg:" + msg);
            OCIncident.GetCostOnGameByCommand(msg, false, out string error);
            if (error != null)
            {
                Loger.Log("IncidentLog ChatController.BeforeStartIncident errorMessage:" + error, Loger.LogLevel.ERROR);
                Find.WindowStack.Add(new Dialog_MessageBox(error));
            }
            else
            {
                Loger.Log("IncidentLog ChatController.BeforeStartIncident ok");
            }
            return new ModelStatus { Status = 1 };
        }

        private static void AfterStartIncident(int chatId, string msg, ModelStatus stat)
        {
            Loger.Log("IncidentLog ChatController.AfterStartIncident Error call incident!", Loger.LogLevel.ERROR);
            Find.WindowStack.Add(new Dialog_MessageBox("Error call incident"));
        }
        #endregion
    }
}