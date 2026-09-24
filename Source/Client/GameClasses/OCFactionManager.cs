using HarmonyLib;
using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace GameClasses
{
    public static class OCFactionManager
    {
        // ОПТИМІЗАЦІЯ: швидкий виклик внутрішнього методу RecacheFactions без повільної рефлексії
        private static readonly Action<FactionManager> RecacheFactionsDelegate =
            AccessTools.MethodDelegate<Action<FactionManager>>(AccessTools.Method(typeof(FactionManager), "RecacheFactions"));

        public static void AddNewFaction(FactionOnline factionOnline)
        {
            FactionDef facDef = DefDatabase<FactionDef>.GetNamed(factionOnline.DefName);

            // Створюємо екземпляр перед передачею в генератор кольору
            Faction faction = new Faction();
            faction.def = facDef;
            faction.loadID = factionOnline.loadID;
            faction.colorFromSpectrum = FactionGenerator.NewRandomColorFromSpectrum(faction);
            faction.Name = factionOnline.Name;

            var allFactions = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < allFactions.Count; i++)
            {
                faction.TryMakeInitialRelationsWith(allFactions[i]);
            }
            faction.TryGenerateNewLeader();

            Find.FactionManager.Add(faction);

            RecacheFactionsDelegate?.Invoke(Find.FactionManager);
        }

        public static void DeleteFaction(Faction faction)
        {
            try
            {
                if (faction == null) return;

                List<Faction> list = Find.FactionManager.AllFactionsListForReading;
                if (!list.Contains(faction)) return;

                // ОПТИМІЗАЦІЯ: збираємо поселення у буферний список без важких виділень LINQ
                var settlements = Find.WorldObjects.Settlements;
                var toRemove = new List<Settlement>();
                for (int i = 0; i < settlements.Count; i++)
                {
                    var sett = settlements[i];
                    if (sett != null && sett.Faction == faction)
                    {
                        toRemove.Add(sett);
                    }
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    Find.WorldObjects.Remove(toRemove[i]);
                }

                List<Pawn> allMapsWorldAndTemporary_AliveOrDead = PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead;
                for (int i = 0; i < allMapsWorldAndTemporary_AliveOrDead.Count; i++)
                {
                    Pawn pawn = allMapsWorldAndTemporary_AliveOrDead[i];
                    if (pawn.Faction == faction && faction.leader != pawn)
                    {
                        pawn.SetFaction(null, null);
                    }
                }

                for (int j = 0; j < Find.Maps.Count; j++)
                {
                    Find.Maps[j].pawnDestinationReservationManager.Notify_FactionRemoved(faction);
                }

                Find.LetterStack.Notify_FactionRemoved(faction);
                faction.RemoveAllRelations();

                if (faction.leader != null)
                {
                    faction.leader.SetFaction(null, null);
                }

                list.Remove(faction);
            }
            catch (Exception e)
            {
                Log.Error("OnlineCity: Error DeleteFaction >> " + e);
            }
        }

        public static void UpdateFactionIDS(List<FactionOnline> factionOnlineList)
        {
            if (factionOnlineList == null || factionOnlineList.Count == 0) return;

            var factionList = Find.FactionManager.AllFactionsListForReading;

            // ОПТИМІЗАЦІЯ: заміна FirstOrDefault з замиканням на подвійний цикл for
            for (int i = 0; i < factionOnlineList.Count; i++)
            {
                var fOnline = factionOnlineList[i];
                if (fOnline == null) continue;

                for (int j = 0; j < factionList.Count; j++)
                {
                    var f = factionList[j];
                    if (f != null && ValidateFaction(fOnline, f))
                    {
                        f.loadID = fOnline.loadID;
                        Loger.Log("Successfully updated faction ID: " + f.def.LabelCap);
                        break;
                    }
                }
            }
        }

        private static bool ValidateFaction(FactionOnline fOnline1, Faction fOnline2)
        {
            return fOnline2?.def != null &&
                   fOnline1.DefName == fOnline2.def.defName &&
                   fOnline1.LabelCap == fOnline2.def.LabelCap &&
                   fOnline1.loadID != fOnline2.loadID;
        }
    }
}