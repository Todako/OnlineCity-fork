using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    public class IncidentInfistation : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            var parms = GetParms();
            if (parms == null) return false;

            // 1. Спроба стандартного ванільного спавну (якщо є гора / ThickRoof)
            if (IncidentDefOf.Infestation.Worker.TryExecute(parms))
            {
                return true;
            }

            // 2. Резервний механізм (fallback) для рівнинних карт без товстого скельного даху
            var map = GetTarget();
            if (map != null)
            {
                // Шукаємо клітинку поруч із колонією гравця
                if (CellFinder.TryFindRandomCellNear(map.Center, map, 40, c => c.Standable(map) && !c.Fogged(map), out var spawnCell))
                {
                    var spawnerDef = ThingDef.Named("TunnelHiveSpawner");
                    if (spawnerDef != null)
                    {
                        GenSpawn.Spawn(spawnerDef, spawnCell, map, WipeMode.Vanish);

                        string label = "OC_Incidents_Inf_Label".Translate();
                        if (label == "OC_Incidents_Inf_Label") label = "Infestation!";

                        string text = "OC_Incidents_Inf_Text".Translate() + ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
                        Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.ThreatBig, new TargetInfo(spawnCell, map));
                        return true;
                    }
                }
            }

            Messages.Message("OC_Incidents_FailedInfestation".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        private IncidentParms GetParms()
        {
            var target = GetTarget();
            if (target == null) return null;

            var incidentParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatSmall, target);
            incidentParms.customLetterLabel = "OC_Incidents_Inf_Label".Translate();
            incidentParms.customLetterText = "OC_Incidents_Inf_Text".Translate() + ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            incidentParms.forced = true;
            incidentParms.faction = Find.FactionManager?.OfInsects ?? Faction.OfInsects;
            incidentParms.target = target;
            incidentParms.points = CalculatePoints();

            this.parms = incidentParms;
            return incidentParms;
        }
    }
}