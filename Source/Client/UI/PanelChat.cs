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
        private int lbCannalsLastSelectedIndex;
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

        public class ListBoxPlayerItem
        {
            public string Login;
            public string Text;
            public string Tooltip;
            public bool InChat;
            public bool GroupTitle;
            public override string ToString() => Text;
        }

        public Chat SelectCannal => lbCannals == null || lbCannals.SelectedIndex < 0 || lbCannals.SelectedIndex >= SessionClientController.Data.Chats.Count
            ? null
            : SessionClientController.Data.Chats[lbCannals.SelectedIndex];

        public void Drow(Rect inRect)
        {
            float leftPanelWidth = 200f;
            float iconWidth = 25f;
            float iconWidthSpace = 30f;

            if (SessionClientController.Data.Chats != null)
            {
                lock (SessionClientController.Data.Chats)
                {
                    if (SessionClientController.Data.ChatNotReadPost > 0)
                    {
                        SessionClientController.Data.ChatNotReadPost = 0;
                    }

                    if (lbCannalsHeight == 0)
                    {
                        float textH = TextHeight;
                        lbCannalsHeight = (float)Math.Round((decimal)(inRect.height / 2f / textH)) * textH;
                    }
                    Widgets.Label(new Rect(inRect.x, inRect.y + iconWidthSpace + lbCannalsHeight, leftPanelWidth, 22f), "OCity_Dialog_Players".Translate());

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

                    if (PanelLastHeight != inRect.height)
                    {
                        PanelLastHeight = inRect.height;
                        lbPlayers.Area = new Rect(inRect.x, inRect.y + iconWidthSpace + lbCannalsHeight + 22f, leftPanelWidth, inRect.height - (iconWidthSpace + lbCannalsHeight + 22f));
                    }

                    if (NeedUpdateChat)
                    {
                        lbCannalsLastSelectedIndex = -1;
                        NeedUpdateChat = false;
                    }

                    bool nowUpdateChat = DataLastChatsTime != SessionClientController.Data.ChatsTime.Time;
                    if (nowUpdateChat)
                    {
                        Loger.Log("Client UpdateChats nowUpdateChat");
                        DataLastChatsTime = SessionClientController.Data.ChatsTime.Time;
                        lbCannalsLastSelectedIndex = -1;
                        NeedUpdateChat = true;
                    }

                    var chats = SessionClientController.Data.Chats;

                    // Оновлення списків каналів та гравців раз на 5 секунд або при появі нових повідомлень
                    if (nowUpdateChat || DataLastChatsTimeUpdateTime < DateTime.UtcNow.AddSeconds(-5))
                    {
                        DataLastChatsTimeUpdateTime = DateTime.UtcNow;

                        // ОПТИМІЗАЦІЯ: швидкий підрахунок хешу логу без LINQ Sum
                        int totalPosts = 0;
                        for (int i = 0; i < chats.Count; i++)
                        {
                            if (chats[i].Posts != null) totalPosts += chats[i].Posts.Count;
                        }
                        long updateLogHash = chats.Count * 1000000L + totalPosts;

                        if (updateLogHash != UpdateLogHash)
                        {
                            UpdateLogHash = updateLogHash;
                            Loger.Log($"Client UpdateChats chats={chats.Count} players={SessionClientController.Data.Players.Count}");
                        }

                        // ОПТИМІЗАЦІЯ: заповнення назв каналів без LINQ Select/ToList
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

                        // ОПТИМІЗАЦІЯ: заповнення списку гравців без виділення тимчасових Func/Action-делегатів
                        var playersData = new List<ListBoxPlayerItem>(32);
                        var alreadyLogin = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (lbCannals.SelectedIndex > 0 && chats.Count > lbCannals.SelectedIndex)
                        {
                            var selectCannal = chats[lbCannals.SelectedIndex];

                            AddGroupTitle(playersData, alreadyLogin, "OCity_Dialog_Exchenge_Chat".Translate());

                            // Власник каналу
                            var ownerItem = AddPlayerItem(playersData, alreadyLogin, selectCannal.OwnerLogin,
                                $"<img pl_{selectCannal.OwnerLogin}>" + FormatOnlineName(IsPlayerOnline(selectCannal.OwnerLogin), "★ " + selectCannal.OwnerLogin));
                            ownerItem.Tooltip += "OCity_Dialog_ChennelOwn".Translate();
                            ownerItem.InChat = true;

                            // Учасники каналу
                            if (selectCannal.PartyLogin != null)
                            {
                                var partyList = new List<string>(selectCannal.PartyLogin);
                                partyList.Sort(StringComparer.OrdinalIgnoreCase);

                                var offlineList = new List<string>(partyList.Count);
                                for (int i = 0; i < partyList.Count; i++)
                                {
                                    string lo = partyList[i];
                                    if (lo != "system" && lo != selectCannal.OwnerLogin)
                                    {
                                        if (IsPlayerOnline(lo))
                                        {
                                            var pi = AddPlayerItem(playersData, alreadyLogin, lo, $"<img pl_{lo}>" + FormatOnlineName(true, lo));
                                            pi.Tooltip += "OCity_Dialog_ChennelUser".Translate();
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
                                    pi.Tooltip += "OCity_Dialog_ChennelUser".Translate();
                                    pi.InChat = true;
                                }
                            }
                        }

                        // Решта гравців на сервері (із загального чату)
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
                                AddGroupTitle(playersData, alreadyLogin, "OCity_Dialog_Exchenge_Gamers".Translate());

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

                    lbCannals.Drow();
                    lbPlayers.Drow();

                    var iconRect = new Rect(inRect.x, inRect.y, iconWidth, iconWidth);
                    TooltipHandler.TipRegion(iconRect, "OCity_Dialog_ChennelCreate".Translate());
                    if (Widgets.ButtonImage(iconRect, GeneralTexture.IconAddTex))
                    {
                        CannalAdd();
                    }

                    if (lbCannals.SelectedIndex > 0 && chats.Count > lbCannals.SelectedIndex)
                    {
                        iconRect.x += iconWidthSpace;
                        TooltipHandler.TipRegion(iconRect, "OCity_Dialog_ChennelClose".Translate());
                        if (Widgets.ButtonImage(iconRect, GeneralTexture.IconDelTex))
                        {
                            CannalDelete();
                        }
                    }

                    if (lbCannals.SelectedIndex >= 0 && chats.Count > lbCannals.SelectedIndex)
                    {
                        iconRect.x += iconWidthSpace;
                        TooltipHandler.TipRegion(iconRect, "OCity_Dialog_OthersFunctions".Translate());
                        if (Widgets.ButtonImage(iconRect, GeneralTexture.IconSubMenuTex))
                        {
                            CannalsMenuShow();
                        }
                    }

                    // Оновлення тексту повідомлень активного каналу
                    if (lbCannalsLastSelectedIndex != lbCannals.SelectedIndex)
                    {
                        lbCannalsLastSelectedIndex = lbCannals.SelectedIndex;
                        if (lbCannals.SelectedIndex >= 0 && chats.Count > lbCannals.SelectedIndex)
                        {
                            var selectCannal = chats[lbCannals.SelectedIndex];
                            if (selectCannal.Posts != null && selectCannal.Posts.Count > 0)
                            {
                                var chatLastPostTime = selectCannal.Posts[selectCannal.Posts.Count - 1].Time;
                                if (ChatLastPostTime != chatLastPostTime)
                                {
                                    ChatLastPostTime = chatLastPostTime;
                                    BuildChatBoxText(selectCannal);
                                    ChatScrollToDown = true;
                                }
                            }
                            else
                            {
                                ChatBox.Text = string.Empty;
                            }
                        }
                        else
                        {
                            ChatBox.Text = string.Empty;
                        }
                    }

                    if (lbCannals.SelectedIndex >= 0 && chats.Count > lbCannals.SelectedIndex)
                    {
                        var selectCannal = chats[lbCannals.SelectedIndex];
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
                                SessionClientController.Command((connect) =>
                                {
                                    connect.PostingChat(selectCannal.Id, ChatInputText);
                                });
                                ChatInputText = string.Empty;
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
            }
        }

        /// <summary>
        /// Формування тексту чату в межах ліміту 5000 символів.
        /// ОПТИМІЗАЦІЯ: повне усунення LINQ Reverse().Where().Aggregate() на користь прямого StringBuilder.
        /// </summary>
        private void BuildChatBoxText(Chat selectCannal)
        {
            var posts = selectCannal.Posts;
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
            if (SessionClientController.Data.Players != null &&
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

            if (lbCannals.SelectedIndex > 0)
                listMenu.Add(new FloatMenuOption("OCity_Dialog_ChennelLeave".Translate(), CannalDelete));

            if (lbCannals.SelectedIndex > 0)
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
                    var mainCannal = SessionClientController.Data.Chats[0];
                    SessionClientController.Command((connect) =>
                    {
                        connect.PostingChat(mainCannal.Id, "/createChat '" + form.InputText.Replace("'", "''") + "'");
                    });
                }
            };
            Find.WindowStack.Add(form);
        }

        private void CannalDelete()
        {
            var form = new Dialog_Input("OCity_Dialog_ChennelQuit".Translate(), "OCity_Dialog_ChennelQuitCheck".Translate());
            form.PostCloseAction = () =>
            {
                if (form.ResultOK)
                {
                    var selectCannal = SessionClientController.Data.Chats[lbCannals.SelectedIndex];
                    SessionClientController.Command((connect) =>
                    {
                        connect.PostingChat(selectCannal.Id, "/exitChat");
                    });
                }
            };
            Find.WindowStack.Add(form);
        }

        private void CannalRename()
        {
            var selectCannal = SessionClientController.Data.Chats[lbCannals.SelectedIndex];
            var form = new Dialog_Input("OCity_Dialog_ChennelRenLabel".Translate(), "OCity_Dialog_ChennelNewName".Translate() + selectCannal.Name, "");
            form.PostCloseAction = () =>
            {
                if (form.ResultOK && form.InputText != null)
                {
                    SessionClientController.Command((connect) =>
                    {
                        connect.PostingChat(selectCannal.Id, "/renameChat '" + form.InputText.Replace("'", "''") + "'");
                    });
                }
            };
            Find.WindowStack.Add(form);
        }

        private void PlayerItemMenu(ListBoxPlayerItem item)
        {
            if (item.GroupTitle) return;

            var listMenu = new List<FloatMenuOption>();
            var myLogin = SessionClientController.My.Login;

            if (SessionClientController.Data.Players != null && SessionClientController.Data.Players.ContainsKey(item.Login))
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

                    int index = lbCannals.DataSource.IndexOf(privateChat);
                    if (index >= 0)
                    {
                        lbCannals.SelectedIndex = index;
                        return;
                    }

                    var mainCannal = SessionClientController.Data.Chats[0];
                    SessionClientController.Command((connect) =>
                    {
                        connect.PostingChat(mainCannal.Id, "/createChat '" + privateChat.Replace("'", "''") + "' '" + item.Login.Replace("'", "''") + "'");
                    });

                    lbCannalsGoToChat = privateChat;
                }));
            }

            if (lbCannals.SelectedIndex > 0 && SessionClientController.Data.Chats.Count > lbCannals.SelectedIndex)
            {
                var selectCannal = SessionClientController.Data.Chats[lbCannals.SelectedIndex];

                if (!item.InChat)
                {
                    listMenu.Add(new FloatMenuOption("OCity_Dialog_ChennelAddUser".Translate(), () =>
                    {
                        SessionClientController.Command((connect) =>
                        {
                            connect.PostingChat(selectCannal.Id, "/addPlayer '" + item.Login.Replace("'", "''") + "'");
                        });
                    }));
                }
            }

            if (listMenu.Count == 0) return;
            Find.WindowStack.Add(new FloatMenu(listMenu));
        }
    }
}