using OCUnion;
using RimWorld;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    public class Dialog_MainOnlineCity : Window
    {
        public string ScenarioToGen;

        private int TabIndex = 0;

        private readonly PanelChat panelChat;
        private readonly PanelProfilePlayer panelProfilePlayer;
        private readonly PanelViewInfo panelViewStates;

        private static string _aboutGeneralText;
        public static string AboutGeneralText
        {
            get
            {
                if (_aboutGeneralText == null)
                {
                    _aboutGeneralText = MainHelper.VersionInfo + " "
                        + "OCity_AboutTabText".Translate() + Environment.NewLine + Environment.NewLine
                        + "OCity_AboutGeneralText".Translate();
                }
                return _aboutGeneralText;
            }
        }

        private static TextBox AboutBox;

        public override Vector2 InitialSize => LastInitialSize;

        private static Dialog_MainOnlineCity IsShow = null;
        private static Vector2 LastInitialSize = new Vector2(750f, 682f);
        private static Vector2 LastInitialPos = new Vector2(0f, 0f);

        // Кешовані об'єкти вкладок для ліквідації щокадрових виділень пам'яті
        private List<TabRecord> _tabsList;
        private TabRecord _tabChat;
        private TabRecord _tabSettings;
        private TabRecord _tabAbout;

        // Кешування тексту статусу підключення та пінгу
        private string _cachedLoginStatus;
        private int _lastPing = -1;
        private bool _lastConnectFail = false;
        private string _lastLogin = null;
        private long _lastStatusUpdateTicks = 0;

        private List<ListableOption> _aboutOptions;

        public Dialog_MainOnlineCity()
        {
            closeOnCancel = true;
            closeOnAccept = false;
            doCloseButton = false;
            doCloseX = true;
            resizeable = true;
            draggable = true;
            panelChat = new PanelChat();
            panelProfilePlayer = new PanelProfilePlayer();
            panelViewStates = new PanelViewInfo();

            ChatController.MainPanelChat = panelChat;

            if (AboutBox == null)
            {
                AboutBox = new TextBox { Text = AboutGeneralText };
            }
        }

        public static void ShowHide()
        {
            if (IsShow == null || !Find.WindowStack.IsOpen<Dialog_MainOnlineCity>())
            {
                IsShow = new Dialog_MainOnlineCity();
                Find.WindowStack.Add(IsShow);
            }
            else
            {
                IsShow.Close();
                IsShow = null;
            }
        }

        public static void ShowChat()
        {
            if (IsShow == null || !Find.WindowStack.IsOpen<Dialog_MainOnlineCity>())
            {
                ShowHide();
            }

            if (IsShow != null)
            {
                IsShow.TabIndex = 0;
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            windowRect.Set(LastInitialPos.x, LastInitialPos.y, windowRect.width, windowRect.height);
        }

        public override void PostClose()
        {
            IsShow = null;
        }

        private bool DevTest = false;

        private void EnsureTabs()
        {
            if (_tabsList == null)
            {
                _tabChat = new TabRecord("OCity_Dialog_ListChat".Translate(), () => { TabIndex = 0; }, TabIndex == 0);
                _tabSettings = new TabRecord("OCity_Dialog_Settings".Translate(), () => { TabIndex = 2; }, TabIndex == 2);
                _tabAbout = new TabRecord("OCity_Dialog_ListAbout".Translate(), () => { TabIndex = 3; }, TabIndex == 3);
                _tabsList = new List<TabRecord>(3) { _tabChat, _tabSettings, _tabAbout };
            }
            _tabChat.selected = (TabIndex == 0);
            _tabSettings.selected = (TabIndex == 2);
            _tabAbout.selected = (TabIndex == 3);
        }

        private string GetLoginStatusText()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            var data = SessionClientController.Data;
            if (data == null) return string.Empty;

            bool fail = data.LastServerConnectFail;
            int ping = (int)data.Ping.TotalMilliseconds;
            string login = SessionClientController.My?.Login ?? string.Empty;

            if (_cachedLoginStatus == null
                || fail != _lastConnectFail
                || login != _lastLogin
                || (nowTicks - _lastStatusUpdateTicks > TimeSpan.TicksPerSecond && ping != _lastPing))
            {
                _lastConnectFail = fail;
                _lastPing = ping;
                _lastLogin = login;
                _lastStatusUpdateTicks = nowTicks;

                if (fail)
                {
                    _cachedLoginStatus = "OCity_Dialog_Connecting".Translate();
                }
                else
                {
                    _cachedLoginStatus = "OCity_Dialog_Login".Translate() + login + " " + ping + "ms";
                }
            }
            return _cachedLoginStatus;
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                LastInitialSize = new Vector2(windowRect.width, windowRect.height);
                LastInitialPos = new Vector2(windowRect.x, windowRect.y);

                if (MainHelper.DebugMode)
                {
                    if (!DevTest && new DevelopTest().Run())
                    {
                        DevTest = true;
                        Close();
                    }
                    if (DevTest) return;
                }

                if (!SessionClient.Get.IsLogined)
                {
                    Close();
                    SessionClientController.Disconnected(null);
                    return;
                }

                var screenRect = new Rect(inRect.x, inRect.y + 31f, 500f, 0);
                var tabRect = new Rect(inRect.x, inRect.y + 31f, inRect.width, inRect.height - 31f);

                EnsureTabs();
                TabDrawer.DrawTabs(screenRect, _tabsList);

                if (TabIndex == 0) DoTab0Contents(tabRect);
                else if (TabIndex == 1) DoTab1Contents(tabRect);
                else if (TabIndex == 2) DoTab2Contents(tabRect);
                else if (TabIndex == 3) DoTab3Contents(tabRect);

                Text.Font = GameFont.Small;
                var loginRect = new Rect(inRect.width - 220f, -2f, 220f, 30f);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(loginRect, GetLoginStatusText());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch (Exception e)
            {
                Loger.Log("Dialog_MainOnlineCity Exception: " + e.Message + Environment.NewLine + e, Loger.LogLevel.ERROR);
            }
        }

        public void DoTab0Contents(Rect inRect)
        {
            panelChat.Drow(inRect);
        }

        public void DoTab1Contents(Rect inRect)
        {
            panelViewStates.Drow(inRect);
        }

        public void DoTab2Contents(Rect inRect)
        {
            panelProfilePlayer.Drow(inRect);
        }

        public void DoTab3Contents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(inRect, "OCity_Dialog_AboutMode".Translate());

            Text.Font = GameFont.Small;
            var chatAreaOuter = new Rect(inRect.x + 150f, inRect.y + 40f, inRect.width - 150f, inRect.height - 30f - 40f);
            AboutBox.Drow(chatAreaOuter);

            var rect2 = new Rect(inRect.x, inRect.y + 30f, 150f, 80f);
            Text.Font = GameFont.Small;

            if (_aboutOptions == null)
            {
                _aboutOptions = new List<ListableOption>(2)
                {
                    new ListableOption_WebLink("OCity_Dialog_Regame".Translate(), () =>
                    {
                        var form = new Dialog_Input("OCity_Dialog_DeleteData".Translate(), "OCity_Dialog_DeleteDataCheck".Translate());
                        form.PostCloseAction = () =>
                        {
                            if (form.ResultOK)
                            {
                                var chats = SessionClientController.Data?.Chats;
                                if (chats != null && chats.Count > 0)
                                {
                                    var mainCannal = chats[0];
                                    SessionClientController.Command((connect) =>
                                    {
                                        var res = connect.PostingChat(mainCannal.Id, "/killmyallplease");
                                        if (res != null && res.Status == 0)
                                        {
                                            SessionClientController.Disconnected("OCity_Dialog_DeletedData".Translate());
                                        }
                                    });
                                }
                            }
                        };
                        Find.WindowStack.Add(form);
                    }, GeneralTexture.IconDelTex),
                    new ListableOption_WebLink("OCity_Dialog_AutorPage".Translate(), "https://steamcommunity.com/sharedfiles/filedetails/?id=1908437382", GeneralTexture.IconForums)
                };
            }

            float num = OptionListingUtility.DrawOptionListing(rect2, _aboutOptions);
            GUI.BeginGroup(rect2);
            if (Current.ProgramState == ProgramState.Entry && Widgets.ButtonImage(new Rect(0f, num + 10f, 64f, 32f), LanguageDatabase.activeLanguage.icon))
            {
                var list3 = new List<FloatMenuOption>();
                foreach (LoadedLanguage current in LanguageDatabase.AllLoadedLanguages)
                {
                    LoadedLanguage localLang = current;
                    list3.Add(new FloatMenuOption(localLang.FriendlyNameNative, delegate
                    {
                        LanguageDatabase.SelectLanguage(localLang);
                        Prefs.Save();
                    }, MenuOptionPriority.Default, null, null, 0f, null, null));
                }
                Find.WindowStack.Add(new FloatMenu(list3));
            }
            GUI.EndGroup();
        }
    }
}