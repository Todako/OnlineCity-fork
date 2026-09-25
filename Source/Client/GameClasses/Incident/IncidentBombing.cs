using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    public class IncidentBombing : OCIncident
    {
        public int cost;

        public override bool TryExecuteEvent()
        {
            var map = GetTarget();
            if (map == null) return false;

            if (!TryFindCell(out var cell, map))
            {
                return false;
            }

            int num = Mathf.Max(1, mult);
            int spreadRadius = Rand.Range(10, 30);
            ThingDef meteor = ThingDefOf.MeteoriteIncoming;

            for (int i = 0; i < num; i++)
            {
                IntVec3 targetCell = cell;

                // ОПТИМІЗАЦІЯ: безпечний підбір точки без ризику нескінченного циклу за межами карти
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    IntVec3 candidate = cell + (Rand.InsideUnitCircleVec3 * spreadRadius).ToIntVec3();
                    if (candidate.InBounds(map) && !candidate.Fogged(map))
                    {
                        targetCell = candidate;
                        break;
                    }
                }

                List<Thing> list = ThingSetMakerDefOf.Meteorite.root.Generate();
                SkyfallerMaker.SpawnSkyfaller(meteor, list, targetCell, map);
            }

            string label = "OC_Incidents_Bombing_Label".Translate();
            if (label == "OC_Incidents_Bombing_Label") label = "Take cover!";

            string text = "OC_Incidents_Bombing_Text".Translate();
            if (text == "OC_Incidents_Bombing_Text") text = "The orbital bombardment has begun";

            if (!string.IsNullOrEmpty(attacker))
            {
                text += ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            }

            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.ThreatBig, new TargetInfo(cell, map));
            return true;
        }

        private bool TryFindCell(out IntVec3 cell, Map map)
        {
            if (map == null)
            {
                cell = IntVec3.Invalid;
                return false;
            }

            int maxMineables = 1;
            return CellFinderLoose.TryFindSkyfallerCell(
                ThingDefOf.MeteoriteIncoming,
                map,
                out cell,
                10,
                default(IntVec3),
                -1,
                allowRoofedCells: true,
                allowCellsWithItems: false,
                allowCellsWithBuildings: true,
                colonyReachable: true,
                avoidColonistsIfExplosive: false,
                alwaysAvoidColonists: false,
                delegate (IntVec3 x)
                {
                    int num = Mathf.CeilToInt(Mathf.Sqrt(maxMineables));
                    CellRect cellRect = CellRect.CenteredOn(x, num, num);
                    int num2 = 0;
                    foreach (IntVec3 item in cellRect)
                    {
                        if (item.InBounds(map) && item.Standable(map))
                        {
                            num2++;
                        }
                    }
                    return num2 >= maxMineables;
                });
        }
    }
}