using Model;
using OCUnion;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity.UI
{
    public class PanelChat : DialogControlBase
    {
        private DateTime DataLastChatsTime; // час отриманого пакета даних чату від сервера
        private DateTime DataLastChatsTimeUpdateTime; // час останнього оновлення списків в інтерфейсі (раз на 5 сек)
        private ListBox<string> lbCannals;
        private int lbCannalsLastSelectedIndex = -1;
        private bool NeedUpdateChat;
        private float lbCannalsHeight = 0;
        private ListBox<ListBoxPlayerItem> lbPlayers;
        private readonly TextImageBox ChatBox = new TextImageBox();

        private float PanelLastHeight = 0;
        private long UpdateLogHash;
        private string lbCannalsGoToChat;
        private bool NeedFockus = true;
        private bool ChatScrollToDown = false;
        private DateTime ChatLastPostTime;
        public string ChatInputText = "";

        // Кешовані рядки інтерфейсу (Zero-GC в OnGUI)
        private static string CachedPlayersLabel;
        private static string CachedChannelCreateTip;
        private static string CachedChannelCloseTip;
        private static string CachedOthersFunctionsTip;
        private static string CachedExchengeChatTitle;
        private static string CachedChannelOwnTip;
        private static string CachedChannelUserTip;
        private static string CachedGamersTitle;

        public class ListBoxPlayerItem
        {
            public string Login;
            public string Text;
            public string Tooltip;
            public bool InChat;
            public bool GroupTitle;
            public override string ToString() => Text;
        }

        public Chat SelectCannal
        {
            get
            {
                var chats = SessionClientController.Data?.Chats;
                if (lbCannals == null || chats == null || lbCannals.SelectedIndex < 0 || lbCannals.SelectedIndex >= chats.Count)
                    return null;
                return chats[lbCannals.SelectedIndex];
            }
        }

        private static void EnsureStaticLabels()
        {
            if (CachedPlayersLabel == null)
            {
                CachedPlayersLabel = "OCity_Dialog_Players".Translate().ToString();
                CachedChannelCreateTip = "OCity_Dialog_ChennelCreate".Translate().ToString();
                CachedChannelCloseTip = "OCity_Dialog_ChennelClose".Translate().ToString();
                CachedOthersFunctionsTip = "OCity_Dialog_OthersFunctions".Translate().ToString();
                CachedExchengeChatTitle = "OCity_Dialog_Exchenge_Chat".Translate().ToString();
                CachedChannelOwnTip = "OCity_Dialog_ChennelOwn".Translate().ToString();
                CachedChannelUserTip = "OCity_Dialog_ChennelUser".Translate().ToString();
                CachedGamersTitle = "OCity_Dialog_Exchenge_Gamers".Translate().ToString();
            }
        }

        public void Drow(Rect inRect)
        {
            EnsureStaticLabels();

            const float leftPanelWidth = 200f;
            const float iconWidth = 25f;
            const float iconWidthSpace = 30f;

            var chats = SessionClientController.Data?.Chats;
            if (chats == null) return;

            int chatsCount = 0;
            Chat selectCannal = null;
            int selectCannalId = 0;
            bool hasSelectedCannal = false;

            // 1. Блок швидкої синхронізації стану під lock (без важкого GUI-рендерингу)
            lock (chats)
            {
                chatsCount = chats.Count;

                if (SessionClientController.Data.ChatNotReadPost > 0)
                {
                    SessionClientController.Data.ChatNotReadPost = 0;
                }

                // Адаптивний розрахунок висоти панелі каналів без decimal
                if (lbCannalsHeight == 0 || PanelLastHeight != inRect.height)
                {
                    PanelLastHeight = inRect.height;
                    float textH = TextHeight > 0f ? TextHeight : 20f;
                    lbCannalsHeight = Mathf.Round(inRect.height / (2f * textH)) * textH;

                    if (lbCannals != null)
                    {
                        lbCannals.Area = new Rect(inRect.x, inRect.y + iconWidthSpace, leftPanelWidth, lbCannalsHeight);
                    }
                    if (lbPlayers != null)
                    {
                        lbPlayers.Area = new Rect(inRect.x, inRect.y + iconWidthSpace + lbCannalsHeight + 22f, leftPanelWidth, inRect.height - (iconWidthSpace + lbCannalsHeight + 22f));
                    }
                }

                if (lbCannals == null)
                {
                    lbCannals = new ListBox<string>
                    {
                        Area = new Rect(inRect.x, inRect.y + iconWidthSpace, leftPanelWidth, lbCannalsHeight)
                    };
                    lbCannals.OnClick += (index, text) => DataLastChatsTime = DateTime.MinValue;
                    lbCannals.SelectedIndex = 0;
                }

                if (lbPlayers == null)
                {
                    lbPlayers = new ListBox<ListBoxPlayerItem>
                    {
                        UsePanelText = true
                    };
                    lbPlayers.OnClick += (index, item) =>
                    {
                        lbPlayers.SelectedIndex = -1;
                        PlayerItemMenu(item);
                    };
                    lbPlayers.Tooltip = (item) => item.Tooltip;
                }

                if (NeedUpdateChat)
                {
                    lbCannalsLastSelectedIndex = -1;
                    NeedUpdateChat = false;
                }

                bool nowUpdateChat = DataLastChatsTime != SessionClientController.Data.ChatsTime.Time;
                if (nowUpdateChat)
                {
                    DataLastChatsTime = SessionClientController.Data.ChatsTime.Time;
                    lbCannalsLastSelectedIndex = -1;
                    NeedUpdateChat = true;
                }

                // Оновлення списків раз на 5 секунд або при надходженні нового пакета
                if (nowUpdateChat || DataLastChatsTimeUpdateTime < DateTime.UtcNow.AddSeconds(-5))
                {
                    DataLastChatsTimeUpdateTime = DateTime.UtcNow;

                    int totalPosts = 0;
                    for (int i = 0; i < chats.Count; i++)
                    {
                        if (chats[i].Posts != null) totalPosts += chats[i].Posts.Count;
                    }
                    long updateLogHash = chats.Count * 1000000L + totalPosts;

                    if (updateLogHash != UpdateLogHash)
                    {
                        UpdateLogHash = updateLogHash;
                        Loger.Log($"Client UpdateChats chats={chats.Count} players={SessionClientController.Data.Players?.Count ?? 0}");
                    }

                    var cannalNames = new List<string>(chats.Count);
                    for (int i = 0; i < chats.Count; i++)
                    {
                        cannalNames.Add(chats[i].Name);
                    }
                    lbCannals.DataSource = cannalNames;

                    if (lbCannalsGoToChat != null)
                    {
                        int targetIndex = lbCannals.DataSource.IndexOf(lbCannalsGoToChat);
                        if (targetIndex >= 0)
                        {
                            lbCannals.SelectedIndex = targetIndex;
                            lbCannalsGoToChat = null;
                        }
                    }

                    // Формування списку учасників чату
                    var playersData = new List<ListBoxPlayerItem>(32);
                    var alreadyLogin = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (lbCannals.SelectedIndex > 0 && chats.Count > lbCannals.SelectedIndex)
                    {
                        var sc = chats[lbCannals.SelectedIndex];

                        AddGroupTitle(playersData, alreadyLogin, CachedExchengeChatTitle);

                        // Власник каналу
                        var ownerItem = AddPlayerItem(playersData, alreadyLogin, sc.OwnerLogin,
                            $"<img pl_{sc.OwnerLogin}>" + FormatOnlineName(IsPlayerOnline(sc.OwnerLogin), "★ " + sc.OwnerLogin));
                        ownerItem.Tooltip += CachedChannelOwnTip;
                        ownerItem.InChat = true;

                        // Учасники каналу
                        if (sc.PartyLogin != null)
                        {
                            var partyList = new List<string>(sc.PartyLogin);
                            partyList.Sort(StringComparer.OrdinalIgnoreCase);

                            var offlineList = new List<string>(partyList.Count);
                            for (int i = 0; i < partyList.Count; i++)
                            {
                                string lo = partyList[i];
                                if (lo != "system" && lo != sc.OwnerLogin)
                                {
                                    if (IsPlayerOnline(lo))
                                    {
                                        var pi = AddPlayerItem(playersData, alreadyLogin, lo, $"<img pl_{lo}>" + FormatOnlineName(true, lo));
                                        pi.Tooltip += CachedChannelUserTip;
                                        pi.InChat = true;
                                    }
                                    else
                                    {
                                        offlineList.Add(lo);
                                    }
                                }
                            }

                            for (int i = 0; i < offlineList.Count; i++)
                            {
                                string lo = offlineList[i];
                                var pi = AddPlayerItem(playersData, alreadyLogin, lo, $"<img pl_{lo}>" + FormatOnlineName(false, lo));
                                pi.Tooltip += CachedChannelUserTip;
                                pi.InChat = true;
                            }
                        }
                    }

                    // Решта гравців із загального каналу
                    if (chats.Count > 0 && chats[0].PartyLogin != null)
                    {
                        var chat0Party = chats[0].PartyLogin;
                        var other = new List<string>(chat0Party.Count);
                        for (int i = 0; i < chat0Party.Count; i++)
                        {
                            string p = chat0Party[i];
                            if (!string.IsNullOrEmpty(p) && p != "system" && !alreadyLogin.Contains(p))
                            {
                                other.Add(p);
                            }
                        }

                        if (other.Count > 0)
                        {
                            other.Sort(StringComparer.OrdinalIgnoreCase);
                            AddGroupTitle(playersData, alreadyLogin, CachedGamersTitle);

                            var offlineOther = new List<string>(other.Count);
                            for (int i = 0; i < other.Count; i++)
                            {
                                string lo = other[i];
                                if (IsPlayerOnline(lo))
                                {
                                    AddPlayerItem(playersData, alreadyLogin, lo, $"<img pl_{lo}>" + FormatOnlineName(true, lo));
                                }
                                else
                                {
                                    offlineOther.Add(lo);
                                }
                            }

                            for (int i = 0; i < offlineOther.Count; i++)
                            {
                                AddPlayerItem(playersData, alreadyLogin, offlineOther[i], $"<img pl_{offlineOther[i]}>" + FormatOnlineName(false, offlineOther[i]));
                            }
                        }
                    }

                    lbPlayers.DataSource = playersData;
                }
            }

            // 2. Відмальовка списків каналів і гравців
            Widgets.Label(new Rect(inRect.x, inRect.y + iconWidthSpace + lbCannalsHeight, leftPanelWidth, 22f), CachedPlayersLabel);

            lbCannals?.Drow();
            lbPlayers?.Drow();

            // Кнопки дій із каналами (підказки викликаються лише при Mouse.IsOver)
            var iconRect = new Rect(inRect.x, inRect.y, iconWidth, iconWidth);
            if (Mouse.IsOver(iconRect)) TooltipHandler.TipRegion(iconRect, CachedChannelCreateTip);
            if (Widgets.ButtonImage(iconRect, GeneralTexture.IconAddTex))
            {
                CannalAdd();
            }

            if (lbCannals != null && lbCannals.SelectedIndex > 0 && chatsCount > lbCannals.SelectedIndex)
            {
                iconRect.x += iconWidthSpace;
                if (Mouse.IsOver(iconRect)) TooltipHandler.TipRegion(iconRect, CachedChannelCloseTip);
                if (Widgets.ButtonImage(iconRect, GeneralTexture.IconDelTex))
                {
                    CannalDelete();
                }
            }

            if (lbCannals != null && lbCannals.SelectedIndex >= 0 && chatsCount > lbCannals.SelectedIndex)
            {
                iconRect.x += iconWidthSpace;
                if (Mouse.IsOver(iconRect)) TooltipHandler.TipRegion(iconRect, CachedOthersFunctionsTip);
                if (Widgets.ButtonImage(iconRect, GeneralTexture.IconSubMenuTex))
                {
                    CannalsMenuShow();
                }
            }

            // 3. Перевірка та оновлення стрічки повідомлень активного каналу під коротким lock
            lock (chats)
            {
                if (lbCannals != null && lbCannals.SelectedIndex >= 0 && chats.Count > lbCannals.SelectedIndex)
                {
                    selectCannal = chats[lbCannals.SelectedIndex];
                    selectCannalId = selectCannal.Id;
                    hasSelectedCannal = true;

                    if (lbCannalsLastSelectedIndex != lbCannals.SelectedIndex)
                    {
                        lbCannalsLastSelectedIndex = lbCannals.SelectedIndex;
                        if (selectCannal.Posts != null && selectCannal.Posts.Count > 0)
                        {
                            ChatLastPostTime = selectCannal.Posts[selectCannal.Posts.Count - 1].Time;
                            BuildChatBoxText(selectCannal);
                            ChatScrollToDown = true;
                        }
                        else
                        {
                            ChatBox.Text = string.Empty;
                        }
                    }
                    else if (selectCannal.Posts != null && selectCannal.Posts.Count > 0)
                    {
                        var lastTime = selectCannal.Posts[selectCannal.Posts.Count - 1].Time;
                        if (ChatLastPostTime != lastTime)
                        {
                            ChatLastPostTime = lastTime;
                            BuildChatBoxText(selectCannal);
                            ChatScrollToDown = true;
                        }
                    }
                }
                else
                {
                    if (lbCannalsLastSelectedIndex != -1)
                    {
                        lbCannalsLastSelectedIndex = -1;
                        ChatBox.Text = string.Empty;
                    }
                }
            }

            // 4. Відмальовка чату та робота з полем вводу БЕЗ утримання блокування
            if (hasSelectedCannal)
            {
                var chatAreaOuter = new Rect(inRect.x + leftPanelWidth + 10f, inRect.y, inRect.width - leftPanelWidth - 10f, inRect.height - 30f);
                Text.Font = GameFont.Small;
                ChatBox.Drow(chatAreaOuter, ChatScrollToDown);
                ChatScrollToDown = false;

                var rrect = new Rect(inRect.x + inRect.width - 25f, inRect.y + inRect.height - 25f, 25f, 25f);
                var anchor = Text.Anchor;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rrect, "▶");
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                bool rrcklick = Widgets.ButtonInvisible(rrect);
                Text.Anchor = anchor;

                if (!string.IsNullOrEmpty(ChatInputText))
                {
                    if (Mouse.IsOver(rrect))
                    {
                        Widgets.DrawHighlight(rrect);
                    }

                    var ev = Event.current;
                    if ((ev.isKey && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Return) || rrcklick)
                    {
                        var textToSend = ChatInputText;
                        ChatInputText = string.Empty;
                        ev.Use();

                        SessionClientController.Command((connect) =>
                        {
                            connect.PostingChat(selectCannalId, textToSend);
                        });
                    }
                }

                GUI.SetNextControlName("StartTextField");
                ChatInputText = GUI.TextField(
                    new Rect(inRect.x + leftPanelWidth + 10f, inRect.y + inRect.height - 25f, inRect.width - leftPanelWidth - 10f - 30f, 25f),
                    ChatInputText,
                    10000);

                if (NeedFockus)
                {
                    NeedFockus = false;
                    GUI.FocusControl("StartTextField");
                }
            }
        }

        /// <summary>
        /// Формування тексту чату в межах ліміту 5000 символів.
        /// ОПТИМІЗАЦІЯ: повне усунення LINQ Reverse().Where().Aggregate() на користь прямого StringBuilder.
        /// </summary>
        private void BuildChatBoxText(Chat targetCannal)
        {
            var posts = targetCannal.Posts;
            if (posts == null || posts.Count == 0)
            {
                ChatBox.Text = string.Empty;
                return;
            }

            int startIndex = posts.Count - 1;
            int totalLength = 0;
            while (startIndex >= 0)
            {
                int msgLen = posts[startIndex].Message?.Length ?? 0;
                if (totalLength + msgLen >= 5000)
                {
                    startIndex++;
                    break;
                }
                totalLength += msgLen;
                startIndex--;
            }
            if (startIndex < 0) startIndex = 0;

            var sb = new StringBuilder(totalLength + (posts.Count - startIndex) * 64);
            for (int i = startIndex; i < posts.Count; i++)
            {
                var cp = posts[i];
                if (sb.Length > 0) sb.AppendLine();
                sb.Append('[');
                sb.Append(cp.Time.ToGoodUtcString("HH:mm "));
                sb.Append(ChatController.PrepareShortTag("<@" + cp.OwnerLogin + ">"));
                sb.Append("]: ");
                sb.Append(cp.Message);
            }
            ChatBox.Text = sb.ToString();
        }

        private static ListBoxPlayerItem AddPlayerItem(List<ListBoxPlayerItem> dataSource, HashSet<string> alreadyLogin, string login, string text)
        {
            if (login != null) alreadyLogin.Add(login);
            var item = new ListBoxPlayerItem
            {
                Login = login,
                Text = text,
                Tooltip = login
            };
            dataSource.Add(item);
            return item;
        }

        private static void AddGroupTitle(List<ListBoxPlayerItem> dataSource, HashSet<string> alreadyLogin, string text)
        {
            if (dataSource.Count > 0)
            {
                var spacer = AddPlayerItem(dataSource, alreadyLogin, null, " ");
                spacer.GroupTitle = true;
            }
            var title = AddPlayerItem(dataSource, alreadyLogin, null, " <i>– " + text + " –</i> ");
            title.GroupTitle = true;
        }

        private static bool IsPlayerOnline(string login)
        {
            if (login == SessionClientController.My?.Login) return true;
            if (SessionClientController.Data?.Players != null &&
                SessionClientController.Data.Players.TryGetValue(login, out var pClient))
            {
                return pClient.Online;
            }
            return false;
        }

        private static string FormatOnlineName(bool online, string txt)
        {
            return online ? "<b>" + txt + " </b>" : "<color=#888888ff>" + txt + "</color>";
        }

        private void CannalsMenuShow()
        {
            var listMenu = new List<FloatMenuOption>(3);

            listMenu.Add(new FloatMenuOption("OCity_Dialog_ChennelCreate2".Translate(), CannalAdd));

            if (lbCannals != null && lbCannals.SelectedIndex > 0)
                listMenu.Add(new FloatMenuOption("OCity_Dialog_ChennelLeave".Translate(), CannalDelete));

            if (lbCannals != null && lbCannals.SelectedIndex > 0)
                listMenu.Add(new FloatMenuOption("OCity_Dialog_ChennelRen".Translate(), CannalRename));

            if (listMenu.Count == 0) return;
            Find.WindowStack.Add(new FloatMenu(listMenu));
        }

        private void CannalAdd()
        {
            var form = new Dialog_Input("OCity_Dialog_ChennelCreating".Translate(), "OCity_Dialog_ChennelCreateName".Translate(), "");
            form.PostCloseAction = () =>
            {
                if (form.ResultOK && !string.IsNullOrEmpty(form.InputText) && form.InputText.Trim().Length > 0)
                {
                    var currentChats = SessionClientController.Data?.Chats;
                    if (currentChats != null && currentChats.Count > 0)
                    {
                        var mainCannal = currentChats[0];
                        SessionClientController.Command((connect) =>
                        {
                            connect.PostingChat(mainCannal.Id, "/createChat '" + form.InputText.Replace("'", "''") + "'");
                        });
                    }
                }
            };
            Find.WindowStack.Add(form);
        }

        private void CannalDelete()
        {
            var currentChats = SessionClientController.Data?.Chats;
            if (currentChats == null || lbCannals == null || lbCannals.SelectedIndex < 0 || lbCannals.SelectedIndex >= currentChats.Count) return;
            var currentSelect = currentChats[lbCannals.SelectedIndex];

            var form = new Dialog_Input("OCity_Dialog_ChennelQuit".Translate(), "OCity_Dialog_ChennelQuitCheck".Translate());
            form.PostCloseAction = () =>
            {
                if (form.ResultOK)
                {
                    SessionClientController.Command((connect) =>
                    {
                        connect.PostingChat(currentSelect.Id, "/exitChat");
                    });
                }
            };
            Find.WindowStack.Add(form);
        }

        private void CannalRename()
        {
            var currentChats = SessionClientController.Data?.Chats;
            if (currentChats == null || lbCannals == null || lbCannals.SelectedIndex < 0 || lbCannals.SelectedIndex >= currentChats.Count) return;
            var currentSelect = currentChats[lbCannals.SelectedIndex];

            var form = new Dialog_Input("OCity_Dialog_ChennelRenLabel".Translate(), "OCity_Dialog_ChennelNewName".Translate() + currentSelect.Name, "");
            form.PostCloseAction = () =>
            {
                if (form.ResultOK && form.InputText != null)
                {
                    SessionClientController.Command((connect) =>
                    {
                        connect.PostingChat(currentSelect.Id, "/renameChat '" + form.InputText.Replace("'", "''") + "'");
                    });
                }
            };
            Find.WindowStack.Add(form);
        }

        private void PlayerItemMenu(ListBoxPlayerItem item)
        {
            if (item.GroupTitle) return;

            var myLogin = SessionClientController.My?.Login;
            if (string.IsNullOrEmpty(myLogin)) return;

            var listMenu = new List<FloatMenuOption>(4);

            if (SessionClientController.Data?.Players != null && SessionClientController.Data.Players.ContainsKey(item.Login))
            {
                listMenu.Add(new FloatMenuOption("OCity_Dialog_ChennelPlayerInfo".Translate(), () =>
                {
                    Dialog_InfoPlayer.ShowInfo(item.Login);
                }));
            }

            if (item.Login != myLogin)
            {
                listMenu.Add(new FloatMenuOption("OCity_Dialog_PrivateMessage".Translate(), () =>
                {
                    var privateChat = string.Compare(myLogin, item.Login, StringComparison.Ordinal) < 0
                        ? myLogin + " · " + item.Login
                        : item.Login + " · " + myLogin;

                    if (lbCannals?.DataSource != null)
                    {
                        int index = lbCannals.DataSource.IndexOf(privateChat);
                        if (index >= 0)
                        {
                            lbCannals.SelectedIndex = index;
                            return;
                        }
                    }

                    var currentChats = SessionClientController.Data?.Chats;
                    if (currentChats != null && currentChats.Count > 0)
                    {
                        var mainCannal = currentChats[0];
                        SessionClientController.Command((connect) =>
                        {
                            connect.PostingChat(mainCannal.Id, "/createChat '" + privateChat.Replace("'", "''") + "' '" + item.Login.Replace("'", "''") + "'");
                        });
                    }

                    lbCannalsGoToChat = privateChat;
                }));
            }

            var chatsNow = SessionClientController.Data?.Chats;
            if (chatsNow != null && lbCannals != null && lbCannals.SelectedIndex > 0 && chatsNow.Count > lbCannals.SelectedIndex)
            {
                var currentSelect = chatsNow[lbCannals.SelectedIndex];

                if (!item.InChat)
                {
                    listMenu.Add(new FloatMenuOption("OCity_Dialog_ChennelAddUser".Translate(), () =>
                    {
                        SessionClientController.Command((connect) =>
                        {
                            connect.PostingChat(currentSelect.Id, "/addPlayer '" + item.Login.Replace("'", "''") + "'");
                        });
                    }));
                }
            }

            if (listMenu.Count == 0) return;
            Find.WindowStack.Add(new FloatMenu(listMenu));
        }
    }
}