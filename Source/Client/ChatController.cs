using Model;
using OCUnion;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Transfer;
using RimWorld;
using Verse;
using RimWorld.Planet;
using UnityEngine;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Контролер обробки чату, текстових тегів, локалізації повідомлень сервера та команд.
    /// </summary>
    internal static class ChatController
    {
        private static bool InOnlineGame;
        internal static PanelChat MainPanelChat;

        public const int MaxChatPostsPerChannel = 500;

        // Повністю безблокувальні кеші для розбору розмітки та перекладів токенів
        private static readonly ConcurrentDictionary<string, string> TranslatedTokenCache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, string> ShortTagEmojiCache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, string> ShortTagPlayerCache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, string> ShortTagDefCache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<int, string> ShortTagTileCache =
            new ConcurrentDictionary<int, string>();

        public static void Init(bool inOnlineGame)
        {
            var connect = SessionClient.Get;
            InOnlineGame = inOnlineGame;
            connect.OnPostingChatAfter = After;
            connect.OnPostingChatBefore = Before;

            ShortTagTileCache.Clear();
        }

        /// <summary>
        /// Обмежує кількість повідомлень у каналі, запобігаючи переповненню пам'яті під час тривалих сесій.
        /// </summary>
        public static void PruneOldPosts(Chat chat, int maxCount = MaxChatPostsPerChannel)
        {
            if (chat?.Posts == null || chat.Posts.Count <= maxCount) return;

            lock (chat.Posts)
            {
                int toRemove = chat.Posts.Count - maxCount;
                if (toRemove > 0)
                {
                    chat.Posts.RemoveRange(0, toRemove);
                }
            }
        }

        public static void AddToInputChat(string text, bool activeChat = false)
        {
            if (activeChat) Dialog_MainOnlineCity.ShowChat();
            if (MainPanelChat == null || string.IsNullOrEmpty(text)) return;

            if (string.IsNullOrEmpty(MainPanelChat.ChatInputText))
            {
                MainPanelChat.ChatInputText = text;
                return;
            }

            if (MainPanelChat.ChatInputText[MainPanelChat.ChatInputText.Length - 1] == ' ')
            {
                MainPanelChat.ChatInputText += text;
            }
            else
            {
                MainPanelChat.ChatInputText += " " + text;
            }
        }

        public static string ServerTranslate(this string textChat, bool onlyTranslate = false)
            => ServerCharTranslate(textChat, onlyTranslate);

        public static string ServerCharTranslate(string textChat, bool onlyTranslate = false)
        {
            if (string.IsNullOrEmpty(textChat)) return textChat;

            if (!onlyTranslate) textChat = PrepareShortTag(textChat);

            int pos = textChat.IndexOf("OC_", StringComparison.Ordinal);
            if (pos < 0) return textChat;

            var sb = new StringBuilder(textChat.Length + 32);
            int lastPos = 0;

            while (pos >= 0)
            {
                sb.Append(textChat, lastPos, pos - lastPos);

                int ep = pos + 3;
                while (ep < textChat.Length && !IsTokenDelimiter(textChat[ep]))
                {
                    ep++;
                }

                int len = ep - pos;
                var sub = textChat.Substring(pos, len);

                string tr = TranslatedTokenCache.GetOrAdd(sub, key =>
                {
                    var translated = key.Translate().ToString();
                    return translated.StartsWith("OC_", StringComparison.Ordinal) ? key : translated;
                });

                sb.Append(tr);

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

                if (pos + 2 < textChat.Length && textChat[pos + 1] == '!' && textChat[pos + 2] == '-')
                {
                    current = posE + 1;
                    continue;
                }

                char tagType = textChat[pos + 1];

                if (tagType != ':' && tagType != '@' && tagType != '#' && tagType != '!' && tagType != '&')
                {
                    current = pos + 1;
                    continue;
                }

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

            return ShortTagEmojiCache.GetOrAdd(content, c => $"<img Emoji/Emoji_{c}>");
        }

        private static string ShortTagPlayer(string content)
        {
            content = content.Trim();
            return ShortTagPlayerCache.GetOrAdd(content, c => $"<btn name=pl{c} class=player arg={c}><img pl_{c}> {c}</btn>");
        }

        private static string ShortTagTile(string content)
        {
            content = content.Trim();
            if (!int.TryParse(content, out int tile)) return null;
            if (Find.WorldGrid == null || tile < 0 || tile >= Find.WorldGrid.tiles.Count) return null;

            return ShortTagTileCache.GetOrAdd(tile, t =>
            {
                var biome = Find.WorldGrid[t].biome;
                Vector2 vector = Find.WorldGrid.LongLatOf(t);
                var coor = vector.y.ToStringLatitude() + " " + vector.x.ToStringLongitude();
                var msg = $"<img name=Waypoint />{coor} <l>{biome.defName}.label</l>";
                if (!biome.impassable)
                {
                    msg += $" (<l>{GameUtils.GetHillinessLabel(Find.WorldGrid[t].hilliness)}</l>)";
                }

                return $"<btn name=tile{t} class=tile d={t} arg={t}>{msg}</btn>";
            });
        }

        private static string ShortTagDef(string content)
        {
            content = content.Trim();
            return ShortTagDefCache.GetOrAdd(content, c => (c == "Human" ? "<img IconHuman />" : $"<img defName={c} />") + $"<l>{c}.label</l>");
        }

        private static string ShortTagServerId(string content)
        {
            content = content.Trim();
            if (!int.TryParse(content, out int serverId)) return null;

            int tile = 0;
            string player = null;
            var msg = "<img name=Waypoint />";
            var myObj = UpdateWorldController.GetMyByServerId(serverId);
            if (myObj != null)
            {
                tile = myObj.Tile;
                player = SessionClientController.My?.Login;
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

        private static bool StartsWithCommand(string msg, string command)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            int i = 0;
            while (i < msg.Length && char.IsWhiteSpace(msg[i])) i++;
            if (msg.Length - i < command.Length) return false;
            return string.Compare(msg, i, command, 0, command.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static ModelStatus Before(int chatId, string msg)
        {
            if (StartsWithCommand(msg, "/call"))
            {
                return BeforeStartIncident(chatId, msg);
            }
            if (StartsWithCommand(msg, "/debug"))
            {
                return Debug(chatId, msg);
            }
            return null;
        }

        private static void After(int chatId, string msg, ModelStatus stat)
        {
            if (StartsWithCommand(msg, "/call") && (stat == null || stat.Status != 0))
            {
                AfterStartIncident(chatId, msg, stat);
            }
        }

        #region Інструменти налагодження (/debug)
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
                var oldThings = AllThingsByMaps != null && AllThingsByMaps.TryGetValue(worldObject.ID, out var ot) ? ot : null;
                things = new Dictionary<string, int>(newThings.Count);
                foreach (var p in newThings)
                {
                    int oldVal = (oldThings != null && oldThings.TryGetValue(p.Key, out int ov)) ? ov : 0;
                    int diff = p.Value - oldVal;
                    if (diff > 0) things[p.Key] = diff;
                }
            }
            else if (needDiffOld)
            {
                var oldThings = AllThingsByMaps != null && AllThingsByMaps.TryGetValue(worldObject.ID, out var ot) ? ot : null;
                things = new Dictionary<string, int>(oldThings?.Count ?? 0);
                if (oldThings != null)
                {
                    foreach (var p in oldThings)
                    {
                        int newVal = newThings.TryGetValue(p.Key, out int nv) ? nv : 0;
                        int diff = p.Value - newVal;
                        if (diff > 0) things[p.Key] = diff;
                    }
                }
            }
            else
            {
                things = newThings;
            }

            int cntThings = 0;
            int allPawns = 0;
            int pawns = 0;

            foreach (var kvp in things)
            {
                cntThings += kvp.Value;
                if (kvp.Key.Contains("gender:")) allPawns++;
                if (kvp.Key.Contains("Colonist")) pawns++;
            }

            var serverId = UpdateWorldController.GetMyByLocalId(worldObject?.ID ?? 0)?.PlaceServerId;
            Loger.Log($"Debug {local} {{ " +
                $"woID={worldObject?.ID} " +
                $"ServerId={serverId} " +
                $"things={things.Count} " +
                $"cntThings={cntThings} " +
                $"allPawns={allPawns} " +
                $"pawns={pawns} " +
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

                var wObjects = ExchengeUtils.WorldObjectsPlayer();

                for (int i = 0; i < wObjects.Count; i++)
                {
                    if (wObjects[i] is Settlement settlement)
                    {
                        var m = settlement.Map;
                        if (!m.IsPlayerHome) continue;
                        var worldObject = m.Parent;

                        var allt = GameUtils.GetAllThings(m, true, false);
                        DebugGetThingsByWO(allt, newThingsByMaps, worldObject, "Map", needSave, needDiffNew, needDiffOld);
                    }
                    else if (wObjects[i] is Caravan caravan)
                    {
                        var allt = GameUtils.GetAllThings(caravan, true, false);
                        DebugGetThingsByWO(allt, newThingsByMaps, caravan, "Caravan", needSave, needDiffNew, needDiffOld);
                    }
                }

                if (needDiffOld && AllThingsByMaps != null)
                {
                    var presentIds = new HashSet<int>();
                    for (int i = 0; i < wObjects.Count; i++) presentIds.Add(wObjects[i].ID);

                    foreach (var pair in AllThingsByMaps)
                    {
                        if (!presentIds.Contains(pair.Key))
                        {
                            DebugGetThingsByWO(null, null, null, "removed WorldObject", false, false, false, pair.Value);
                        }
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
                return new ModelStatus { Status = 1 };
            }

            Loger.Log("IncidentLog ChatController.BeforeStartIncident ok");
            return null;
        }

        private static void AfterStartIncident(int chatId, string msg, ModelStatus stat)
        {
            var errMessage = stat?.Message ?? "Error call incident";
            Loger.Log("IncidentLog ChatController.AfterStartIncident Error: " + errMessage, Loger.LogLevel.ERROR);
            Find.WindowStack.Add(new Dialog_MessageBox(errMessage));
        }
        #endregion
    }
}