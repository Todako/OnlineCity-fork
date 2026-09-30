using HarmonyLib;
using OCUnion;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;

namespace RimWorldOnlineCity
{
    public static class GameStarter
    {
        public static Scenario SetScenario = null;
        public static string SetScenarioName = null;
        public static int SetMapSize = 0;
        public static float SetPlanetCoverage = 0;
        public static string SetSeed = null;
        public static OverallRainfall SetOverallRainfall = OverallRainfall.Normal;
        public static OverallTemperature SetOverallTemperature = OverallTemperature.Normal;
        public static string SetDifficulty = null;
        public static Action AfterStart = null;
        public static List<Pawn> SetPawns = null;

        public static void GoToMainMenu()
        {
            SceneManager.LoadScene("Entry");
        }

        public static void GameGeneration(bool withStart = true)
        {
            var quickStarterType = typeof(Root).Assembly.GetType("Verse.QuickStarter");
            if (quickStarterType == null)
            {
                Loger.Log("Client Verse.QuickStarter type not found");
                return;
            }
            var quickStartedField = AccessTools.Field(quickStarterType, "quickStarted");
            if (quickStartedField == null)
            {
                Loger.Log("Client QuickStarter.quickStarted field not found");
                return;
            }

            quickStartedField.SetValue(null, true);

            if (withStart)
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    Current.Game = null;
                }, "Play", "GeneratingMap", true, null);
            }
        }

        internal static Scenario ReplaceQuickstartScenarioIfNeeded(Scenario original)
        {
            return SetScenario ?? original;
        }

        internal static int ReplaceQuickstartMapSizeIfNeeded(int original)
        {
            return SetMapSize > 0 ? SetMapSize : original;
        }

        internal static World ReplaceQuickstartWorldIfNeeded(World world)
        {
            return world;
        }
    }

    [HarmonyPatch(typeof(TileFinder))]
    [HarmonyPatch("IsValidTileForNewSettlement")]
    internal static class TileFinder_IsValidTileForNewSettlement_Patch
    {
        public static bool Off = false;
        private static string CachedCityNotBuildReason;

        [HarmonyPostfix]
        public static void Postfix(ref bool __result, int tile, StringBuilder reason)
        {
            if (Off || !__result) return;
            if (SessionClient.Get?.IsLogined != true) return;

            WorldGrid worldGrid = Find.WorldGrid;
            if (worldGrid == null) return;

            var listWO = Find.WorldObjects?.AllWorldObjects;
            if (listWO == null) return;

            for (int i = 0; i < listWO.Count; i++)
            {
                if (listWO[i] is BaseOnline bo)
                {
                    var wot = bo.Tile;
                    if (wot == tile || worldGrid.IsNeighborOrSame(wot, tile))
                    {
                        if (reason != null)
                        {
                            if (CachedCityNotBuildReason == null)
                            {
                                CachedCityNotBuildReason = "OCity_Starter_CityNotBuild".Translate().ToString();
                            }
                            reason.Append(CachedCityNotBuildReason);
                        }
                        __result = false;
                        return;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(WorldGenerator))]
    [HarmonyPatch("GenerateWorld")]
    internal static class WorldGenerator_GenerateWorld_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ref float planetCoverage, ref string seedString, ref OverallRainfall overallRainfall, ref OverallTemperature overallTemperature)
        {
            Loger.Log("Client HarmonyPatch WorldGenerator.GenerateWorld()");
            if (GameStarter.SetPlanetCoverage > 0) planetCoverage = GameStarter.SetPlanetCoverage;
            if (!string.IsNullOrEmpty(GameStarter.SetSeed)) seedString = GameStarter.SetSeed;
            if (GameStarter.SetPlanetCoverage > 0 || !string.IsNullOrEmpty(GameStarter.SetSeed))
            {
                overallRainfall = GameStarter.SetOverallRainfall;
                overallTemperature = GameStarter.SetOverallTemperature;
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (GameStarter.SetPawns != null && Current.Game?.InitData != null)
            {
                Current.Game.InitData.startingAndOptionalPawns = GameStarter.SetPawns;
            }
        }
    }

    [HarmonyPatch(typeof(Game))]
    [HarmonyPatch("InitNewGame")]
    internal static class Game_InitNewGame_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var action = GameStarter.AfterStart;
            if (action != null)
            {
                GameStarter.AfterStart = null;
                Loger.Log("Client HarmonyPatch Game.InitNewGame()");
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Loger.Log("GameStarter.AfterStart error: " + ex.Message, Loger.LogLevel.ERROR);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Root_Play))]
    [HarmonyPatch("SetupForQuickTestPlay")]
    internal static class RootPlay_TestPlay_Patch
    {
        private static bool patchedScenario;
        private static bool patchedSize;

        [HarmonyPrepare]
        public static void Prepare()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (!patchedScenario || !patchedSize)
                {
                    Loger.Log("Client RootPlay_TestPlay_Patch was partial or unsuccessful: " + patchedScenario + ", " + patchedSize);
                }
            });
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            var action = GameStarter.AfterStart;
            if (action != null)
            {
                GameStarter.AfterStart = null;
                Loger.Log("Client HarmonyPatch Root_Play.SetupForQuickTestPlay()");
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Loger.Log("GameStarter.AfterStart error: " + ex.Message, Loger.LogLevel.ERROR);
                }
            }
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> InjectCustomQuickstartSettings(IEnumerable<CodeInstruction> instructions)
        {
            var gameSetScenarioMethod = AccessTools.Method(typeof(Game), "set_Scenario");
            var gameInitDataMapSizeField = AccessTools.Field(typeof(GameInitData), "mapSize");
            var gameSetWorldMethod = AccessTools.Method(typeof(Game), "set_World");

            if (gameSetScenarioMethod == null || gameInitDataMapSizeField == null || gameSetWorldMethod == null)
            {
                Loger.Log("Client Failed to reflect a required member: " + Environment.StackTrace);
            }

            foreach (var inst in instructions)
            {
                if (inst.opcode == OpCodes.Callvirt && Equals(inst.operand, gameSetWorldMethod))
                {
                    yield return new CodeInstruction(OpCodes.Call, ((Func<World, World>)GameStarter.ReplaceQuickstartWorldIfNeeded).Method);
                }
                else if (inst.opcode == OpCodes.Callvirt && Equals(inst.operand, gameSetScenarioMethod))
                {
                    yield return new CodeInstruction(OpCodes.Call, ((Func<Scenario, Scenario>)GameStarter.ReplaceQuickstartScenarioIfNeeded).Method);
                    patchedScenario = true;
                }
                else if (inst.opcode == OpCodes.Stfld && Equals(inst.operand, gameInitDataMapSizeField))
                {
                    yield return new CodeInstruction(OpCodes.Call, ((Func<int, int>)GameStarter.ReplaceQuickstartMapSizeIfNeeded).Method);
                    patchedSize = true;
                }
                yield return inst;
            }
        }
    }

    [HarmonyPatch(typeof(Autosaver))]
    [HarmonyPatch("DoAutosave")]
    internal static class Autosaver_DoAutosave_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (SessionClient.Get?.IsLogined == true)
            {
                Loger.Log("Client HarmonyPatch Autosaver.DoAutosave cancel");
                return false;
            }
            return true;
        }
    }
}