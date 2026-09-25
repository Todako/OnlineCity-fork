using OCUnion;
using RimWorld;
using RimWorld.Planet;
using System;
using Verse;

namespace RimWorldOnlineCity
{
    class IncidentByDef : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            if (incidentParams == null || incidentParams.Count == 0)
            {
                Loger.Log("IncidentByDef Error: no parameters provided", Loger.LogLevel.WARNING);
                return false;
            }

            // Шукаємо валідний IncidentDef серед переданих параметрів
            IncidentDef incident = null;
            string paramName = null;

            for (int i = 0; i < incidentParams.Count; i++)
            {
                var candidate = incidentParams[i];
                if (string.IsNullOrEmpty(candidate)) continue;

                var def = DefDatabase<IncidentDef>.GetNamedSilentFail(candidate);
                if (def != null)
                {
                    incident = def;
                    paramName = candidate;
                    break;
                }
            }

            if (incident == null || incident.Worker == null)
            {
                Loger.Log("IncidentByDef Error: IncidentDef not found for params: " + string.Join(", ", incidentParams), Loger.LogLevel.WARNING);
                return false;
            }

            var target = GetTarget();
            if (target == null) return false;

            IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(incident.category ?? IncidentCategoryDefOf.Misc, target);
            incidentParms.forced = true;
            incidentParms.points = CalculatePoints();

            if (!incident.Worker.TryExecute(incidentParms))
            {
                Loger.Log("Error start IncidentDef: " + paramName, Loger.LogLevel.WARNING);
                return false;
            }

            Loger.Log("Start IncidentDef: " + paramName, Loger.LogLevel.INFO);
            return true;
        }
    }
}