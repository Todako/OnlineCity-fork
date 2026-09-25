using Model;
using OCUnion;
using OCUnion.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldOnlineCity
{
    public abstract class OCIncident
    {
        public string attacker;
        public int mult = 1;
        public List<string> incidentParams;
        public IncidentParms parms;
        public WorldObject place;

        public const float hour = 2500f;
        public const float day = 60000f;

        public abstract bool TryExecuteEvent();

        protected Map GetTarget()
        {
            return (place as Settlement)?.Map ?? Find.CurrentMap;
        }

        /// <summary>
        /// Пошук ворожої фракції для інциденту з надійним fallback-механізмом.
        /// </summary>
        public Faction GetFaction(string factionParam)
        {
            try
            {
                var param = (factionParam ?? string.Empty).ToLower().Trim();
                switch (param)
                {
                    case "mech":
                        return Find.FactionManager.OfMechanoids;

                    case "pirate":
                        {
                            var factions = Find.FactionManager.AllFactionsListForReading;
                            for (int i = 0; i < factions.Count; i++)
                            {
                                var f = factions[i];
                                if (f != null && f.def.defName == "Pirate" && f.def.permanentEnemy && f.def.humanlikeFaction
                                    && f.def.techLevel >= TechLevel.Industrial && f.def.techLevel < TechLevel.Archotech)
                                {
                                    return f;
                                }
                            }
                            return Find.FactionManager.RandomEnemyFaction(false, false, true, TechLevel.Industrial);
                        }

                    case "randy":
                        return Find.FactionManager.RandomEnemyFaction(false, true, true);

                    case "tribe":
                    default:
                        {
                            var factions = Find.FactionManager.AllFactionsListForReading;
                            for (int i = 0; i < factions.Count; i++)
                            {
                                var f = factions[i];
                                if (f != null && f.def.permanentEnemy && f.def.humanlikeFaction && f.def.techLevel <= TechLevel.Medieval)
                                {
                                    return f;
                                }
                            }
                            return Find.FactionManager.RandomEnemyFaction(false, false, true, TechLevel.Neolithic);
                        }
                }
            }
            catch (Exception ex)
            {
                Loger.Log("IncidentGenerate Error finding faction: " + ex.Message, Loger.LogLevel.WARNING);
                return null;
            }
        }

        /// <summary>
        /// Розраховує вартість інциденту та списує необхідне золото.
        /// </summary>
        public static string GetCostOnGameByCommand(string command, bool onliCheck, out string error)
        {
            Loger.Log("IncidentLog OCIncident.GetCostOnGameByCommand command: " + command);

            ChatUtils.ParceCommand(command, out _, out var args);

            if (args == null || args.Count < 3)
            {
                error = "OC_Incidents_OCIncident_WrongArg".Translate().ToString();
                return null;
            }

            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != null && args[i].IndexOf("cost=", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    error = "OC_Incidents_OCIncident_WrongArg".Translate().ToString();
                    return null;
                }
            }

            if (!long.TryParse(args[2], out long serverId))
            {
                error = "OC_Incidents_OCIncident_WrongArg".Translate().ToString();
                return null;
            }

            int mult = 1;
            if (args.Count > 3 && !int.TryParse(args[3], out mult))
            {
                mult = 1;
            }

            var parameters = new List<string>(args.Count > 4 ? args.Count - 4 : 0);
            for (int i = 4; i < args.Count; i++)
            {
                parameters.Add((args[i] ?? string.Empty).ToLower().Trim());
            }

            int cost = CalculateRaidCost(args[0].ToLower(), serverId, mult, parameters);
            int gold = -1;
            int goldClient = -1;
            int goldServer = -1;

            if (cost > 0)
            {
                SessionClientController.Command((connect) =>
                {
                    goldServer = connect.ExchengeInfo_GetCountThing(ThingTrade.CreateTrade(ThingDefOf.Gold, 1, 0, 1));
                });

                goldClient = GameUtils.FindThings(ThingDefOf.Gold, 0, true);

                if (goldClient >= 0 && goldServer >= 0)
                {
                    gold = goldClient + goldServer;
                }
            }
            else if (cost == 0)
            {
                goldClient = 0;
                goldServer = 0;
                gold = 0;
            }

            if (cost < 0 || gold < 0 || gold < cost)
            {
                error = cost < 0 || gold < 0
                    ? "OC_Incidents_OCIncident_WealthErr".Translate().ToString() + $" cost={cost} gold={gold}"
                    : "OC_Incidents_OCIncident_GoldErr".Translate(gold, cost, cost - gold).ToString();
                return null;
            }

            if (onliCheck)
            {
                error = null;
                return "OC_Incidents_OCIncident_NotEnoughGold".Translate(cost);
            }

            Loger.Log("IncidentLog OCIncident.GetCostOnGameByCommand cost=" + cost);
            Loger.TransLog("IncidentLog cost=" + cost + " Command: " + command);

            Action goRaid = () =>
            {
                try
                {
                    var chats = SessionClientController.Data?.Chats;
                    if (chats == null || chats.Count == 0) return;

                    var mainCannal = chats[0];
                    SessionClientController.Command((connect) =>
                    {
                        var res = connect.PostingChat(mainCannal.Id, command + " cost=" + cost, true);

                        if (res != null && res.Status == 0)
                        {
                            Find.WindowStack.Add(new Dialog_MessageBox("OC_Incidents_OCIncident_GoldPay".Translate(cost)));
                        }
                        else
                        {
                            var errorMessage = string.IsNullOrEmpty(res?.Message) ? "Error call" : res.Message.ServerTranslate().ToString();
                            Find.WindowStack.Add(new Dialog_MessageBox(errorMessage));
                        }
                    });
                }
                catch (Exception exp)
                {
                    Loger.Log("IncidentLog Raid Exception " + exp);
                }
            };

            var countToServer = cost - goldServer;
            if (countToServer > 0)
            {
                var inGame = GameUtils.FindThings(ThingDefOf.Gold, countToServer, true, out var thingSelect);
                if (inGame < countToServer || thingSelect == null || thingSelect.Count == 0)
                {
                    error = "OCity_OCIncident_NotEnoughThingsOnMap".Translate() + " " + inGame + ". " + "OCity_OCIncident_Required".Translate() + " " + countToServer;
                    return null;
                }

                Map map = null;
                foreach (var k in thingSelect.Keys)
                {
                    if (k?.Map != null) { map = k.Map; break; }
                }

                if (map?.Parent == null)
                {
                    error = "Error finding source map";
                    return null;
                }

                var fromWorldObject = map.Parent;
                var toWorldObject = new TradeThingsOnline { Tile = fromWorldObject.Tile };
                ExchengeUtils.MoveSelectThings(fromWorldObject, toWorldObject, thingSelect, () =>
                {
                    goRaid();
                });
            }
            else
            {
                goRaid();
            }

            error = null;
            return null;
        }

        public float CalculatePoints()
        {
            var target = GetTarget();
            if (target == null) return 100f;

            float points = StorytellerUtility.DefaultThreatPointsNow(target);
            float powerPercent = SessionClientController.Data != null
                ? (float)SessionClientController.Data.GeneralSettings.IncidentPowerPrecent
                : 100f;

            var resultPoints = points * mult * (powerPercent / 100f);
            Loger.Log($"CalculatePoints(). points={(int)points} resultPoints={resultPoints}");
            return resultPoints;
        }

        public static int CalculateRaidCost(string type, long serverId, int mult, List<string> incidentParams)
        {
            var incident = Incidents.ParseIncidentName(type);
            if (incident == null) return -1;

            var type_mult = incident.CalcCostMult != null ? incident.CalcCostMult(incidentParams) : 1f;
            if (type_mult == 0f) return 0;

            var target = UpdateWorldController.GetOtherByServerId(serverId) as BaseOnline;
            if (target == null) return -1;

            var costs = target.Player?.CostWorldObjects(serverId);
            if (costs == null) return -1;

            var cost = costs.MarketValueTotal;
            if (cost <= 0) return -1;

            float incidentCostPercent = SessionClientController.Data != null
                ? (float)SessionClientController.Data.GeneralSettings.IncidentCostPrecent
                : 100f;

            var options_mult = (float)Math.Pow(mult, 9.42f / 8f) * (incidentCostPercent / 100f) * type_mult;

            float raidCost = cost <= 500000f
                ? (int)(Math.Pow(cost / 100000f, 0.5f) * 246f)
                : cost <= 5000000f
                    ? (int)(Math.Pow(cost / 100000f, 2.2f / 5.05f) * 272.864f)
                    : 15000f;

            raidCost *= options_mult;

            if (raidCost > 100000f) raidCost = 100000f;
            if (raidCost > 0f && raidCost < 1f) raidCost = 100f;

            Loger.Log($"IncidentLog CalculateRaidCost({serverId}, {mult}). targetCost={(int)cost} raidCost={raidCost}");

            bool isAdminDev = SessionClientController.Data?.IsAdmin == true && Prefs.DevMode;
            return isAdminDev ? 1 : (int)raidCost;
        }
    }
}