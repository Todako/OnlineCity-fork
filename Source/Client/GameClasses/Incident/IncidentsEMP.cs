using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    class IncidentEMP : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            Map map = GetTarget();
            if (map == null) return false;

            int duration = Mathf.RoundToInt(hour * mult);
            GameCondition_DisableElectricity emp = (GameCondition_DisableElectricity)GameConditionMaker.MakeCondition(GameConditionDefOf.SolarFlare, duration);

            string label = "OC_Incidents_EMP_Label".Translate();
            string text = "OC_Incidents_EMP_Text".Translate() + ". " + "OC_Incident_Atacker".Translate() + " " + attacker;

            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NegativeEvent);
            map.gameConditionManager.RegisterCondition(emp);

            return true;
        }
    }
}