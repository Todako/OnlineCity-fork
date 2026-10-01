using HarmonyLib;
using OCUnion;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    public static class MainMenu
    {
        public static bool HasClickMainMenuNetClick = false;

        public static void OnMainMenuNetClick()
        {
            HasClickMainMenuNetClick = true;

            var checkers = SessionClientController.ClientFileCheckers;
            if (checkers == null)
            {
                Loger.Log("Message wait UpdateModsWindow");
                Find.WindowStack.Add(new UpdateModsWindow());
                return;
            }

            // ВИПРАВЛЕНО CS1061: checkers.Length замість checkers.Count
            for (int i = 0; i < checkers.Length; i++)
            {
                var c = checkers[i];
                if (c == null || !c.Complete)
                {
                    Loger.Log("Message wait UpdateModsWindow");
                    Find.WindowStack.Add(new UpdateModsWindow());
                    return;
                }
            }

            Find.WindowStack.Add(new Dialog_LoginForm());
        }
    }

    [HarmonyPatch(typeof(OptionListingUtility))]
    [HarmonyPatch("DrawOptionListing")]
    [HarmonyPatch(new[] { typeof(Rect), typeof(List<ListableOption>) })]
    internal static class MainMenuDrawer_DoMainMenuControls_Patch
    {
        public static bool Inited = false;
        public static DateTime DontDisconnectTime;

        // Кешовані рядки кнопок для ліквідації перекладів на кожному кадрі OnGUI
        private static string CachedSave;
        private static string CachedLoadGame;
        private static string CachedReviewScenario;
        private static string CachedSaveAndQuitToMainMenu;
        private static string CachedSaveAndQuitToOS;
        private static string CachedConfirmQuit;
        private static string CachedQuitToMainMenu;
        private static string CachedQuitToOS;
        private static string CachedLanBtn;
        private static string CachedOnlineBtn;
        private static string CachedSavedBtn;
        private static string CachedWithdrawBtn;
        private static string CachedSurrenderBtn;

        private static void EnsureStrings()
        {
            if (CachedSave == null)
            {
                CachedSave = "Save".Translate().ToString();
                CachedLoadGame = "LoadGame".Translate().ToString();
                CachedReviewScenario = "ReviewScenario".Translate().ToString();
                CachedSaveAndQuitToMainMenu = "SaveAndQuitToMainMenu".Translate().ToString();
                CachedSaveAndQuitToOS = "SaveAndQuitToOS".Translate().ToString();
                CachedConfirmQuit = "ConfirmQuit".Translate().ToString();
                CachedQuitToMainMenu = "QuitToMainMenu".Translate().ToString();
                CachedQuitToOS = "QuitToOS".Translate().ToString();
                CachedLanBtn = "OCity_LAN_btn".Translate().ToString();
                CachedOnlineBtn = "OCity_MainMenu_Online".Translate().ToString();
                CachedSavedBtn = "OCity_MainMenu_Saved".Translate().ToString();
                CachedWithdrawBtn = "OCity_MainMenu_Withdraw".Translate().ToString();
                CachedSurrenderBtn = "OCity_MainMenu_Surrender".Translate().ToString();
            }
        }

        [HarmonyPrefix]
        public static void Prefix(Rect rect, List<ListableOption> optList)
        {
            EnsureStrings();

            if (optList.Count > 0 && optList[0].GetType() == typeof(ListableOption))
            {
                if (Current.ProgramState == ProgramState.Entry)
                {
                    if ((GameStarter.AfterStart != null || SessionClient.Get?.IsLogined == true)
                        && (DateTime.UtcNow - DontDisconnectTime).TotalSeconds >= 2d)
                    {
                        Loger.Log("Client MainMenu Disconnecting" + (GameStarter.AfterStart != null ? "." : ""));
                        GameStarter.AfterStart = null;
                        SessionClientController.Disconnected(null);
                    }

                    var item = new ListableOption(CachedLanBtn, delegate
                    {
                        MainMenu.OnMainMenuNetClick();
                    }, null);
                    optList.Insert(0, item);
                }
                else
                {
                    if (SessionClient.Get?.IsLogined == true)
                    {
                        for (int i = 0; i < optList.Count; i++)
                        {
                            var label = optList[i].label;
                            if (label == CachedSave
                                || label == CachedLoadGame
                                || label == CachedReviewScenario
                                || label == CachedSaveAndQuitToMainMenu
                                || label == CachedSaveAndQuitToOS
                                || label == CachedConfirmQuit
                                || label == CachedQuitToMainMenu
                                || label == CachedQuitToOS)
                            {
                                optList.RemoveAt(i--);
                            }
                        }

                        var item = new ListableOption(CachedOnlineBtn, delegate
                        {
                            Dialog_MainOnlineCity.ShowHide();
                        }, null);
                        optList.Add(item);

                        item = new ListableOption(CachedSave, delegate
                        {
                            Loger.Log("Client MainMenu Save");
                            SessionClientController.SaveGameNowInEvent();
                            Find.WindowStack.Add(new Dialog_Input(CachedSavedBtn, "", true));
                        }, null);
                        optList.Add(item);

                        if (SessionClientController.Data?.AttackModule != null)
                        {
                            item = new ListableOption(CachedWithdrawBtn, delegate
                            {
                                Loger.Log("Client MainMenu VictoryHost");
                                SessionClientController.Data.AttackModule.VictoryHostToHost = true;
                            }, null);
                            optList.Add(item);
                        }

                        if (SessionClientController.Data?.AttackUsModule != null)
                        {
                            item = new ListableOption(CachedSurrenderBtn, delegate
                            {
                                Loger.Log("Client MainMenu VictoryAttacker");
                                SessionClientController.Data.AttackUsModule.ConfirmedVictoryAttacker = true;
                            }, null);
                            optList.Add(item);
                        }

                        item = new ListableOption(CachedQuitToMainMenu, delegate
                        {
                            GameExit.TriggerBeforeExit();
                            GenScene.GoToMainMenu();
                        }, null);
                        optList.Add(item);

                        item = new ListableOption(CachedQuitToOS, delegate
                        {
                            GameExit.TriggerBeforeExit();
                            Root.Shutdown();
                        }, null);
                        optList.Add(item);
                    }
                }
            }

            if (Inited) return;
            Inited = true;
            SessionClientController.Init();
        }
    }
}