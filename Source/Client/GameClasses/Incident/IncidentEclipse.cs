using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    class IncidentEclipse : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            Map map = GetTarget();
            if (map == null) return false;

            int duration = Mathf.RoundToInt(2 * hour * mult);
            var eclipse = (GameCondition_NoSunlight)GameConditionMaker.MakeCondition(GameConditionDefOf.Eclipse, duration);

            string label = "OC_Incidents_Eclipse_Label".Translate();
            if (label == "OC_Incidents_Eclipse_Label") label = "Eclipse";

            string text = "OC_Incidents_Eclipse_Text".Translate() + ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NegativeEvent);
            map.gameConditionManager.RegisterCondition(eclipse);

            return true;
        }
    }
}