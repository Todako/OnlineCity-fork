using RimWorld;
using Verse;

namespace RimWorldOnlineCity
{
    public class IncidentRaid : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            var parms = GetParms();
            if (parms == null) return false;

            if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
            {
                Messages.Message("OC_Incidents_FailedRaid".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            return true;
        }

        private IncidentParms GetParms()
        {
            var target = GetTarget();
            if (target == null) return null;

            var arrivalParam = ParseArrivalMode(incidentParams != null && incidentParams.Count > 0 ? incidentParams[0] : "walk");
            var factionParam = ParseFaction(incidentParams != null && incidentParams.Count > 1 ? incidentParams[1] : null);

            var incidentParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatSmall, target);
            incidentParms.raidStrategy = GetStrategy();
            incidentParms.raidArrivalMode = GetArrivalMode(arrivalParam);
            incidentParms.customLetterLabel = "OC_Incidents_Raid_Label".Translate();
            incidentParms.customLetterText = "OC_Incidents_Raid_Text".Translate() + ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            incidentParms.biocodeApparelChance = 1f;
            incidentParms.biocodeWeaponsChance = 1f;
            incidentParms.dontUseSingleUseRocketLaunchers = false;
            incidentParms.generateFightersOnly = false;
            incidentParms.raidNeverFleeIndividual = true;
            incidentParms.faction = GetFaction(factionParam);
            incidentParms.forced = true;
            incidentParms.target = target;
            incidentParms.points = CalculatePoints();

            this.parms = incidentParms;
            return incidentParms;
        }

        public static string ParseArrivalMode(string arg)
        {
            var clean = (arg ?? string.Empty).ToLower().Trim();
            switch (clean)
            {
                case "random":
                case "air":
                case "walk":
                    return clean;
                default:
                    return "walk";
            }
        }

        public static string ParseFaction(string arg)
        {
            var clean = (arg ?? string.Empty).ToLower().Trim();
            switch (clean)
            {
                case "mech":
                case "pirate":
                case "tribe":
                case "randy":
                    return clean;
                default:
                    return "tribe";
            }
        }

        public static RaidStrategyDef GetStrategy()
        {
            return RaidStrategyDefOf.ImmediateAttack;
        }

        public static PawnsArrivalModeDef GetArrivalMode(string arriveParam)
        {
            switch (arriveParam)
            {
                case "random":
                    return PawnsArrivalModeDefOf.RandomDrop;
                case "air":
                    return PawnsArrivalModeDefOf.CenterDrop;
                case "walk":
                default:
                    return PawnsArrivalModeDefOf.EdgeWalkIn;
            }
        }
    }
}