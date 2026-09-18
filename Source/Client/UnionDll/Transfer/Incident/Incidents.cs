using System;
using System.Collections.Generic;
using Transfer.ModelMails;

namespace OCUnion
{
    public static class Incidents
    {
        public static List<IncidentMetadata> AllIncidents => _incidents;

        private static readonly List<IncidentMetadata> _incidents;
        private static readonly Dictionary<string, IncidentMetadata> _incidentsByName;

        static Incidents()
        {
            _incidents = new List<IncidentMetadata>
            {
                new IncidentMetadata
                {
                    NumberOrder = 3,
                    OrderLabel = "OC_Incidents_Hire_label",
                    Enable = true,
                    IncidentType = IncidentTypes.Infistation,
                    IncidentTypeName = "inf",
                    Label = "OC_Bugs",
                    DelayBeforeStart = (mail) => mail.IncidentMult >= 5,
                    DelayMessageType = (_) => ModelMailMessadge.MessadgeTypes.ThreatBig,
                    DelayMessageLabel = (_) => "OC_Incidents_Raid_Warning_label",
                    DelayMessageText = (_) => "OC_Incidents_Inf_Warning_Text_inf",
                    CalcCostMult = (_) => 3f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 3,
                    OrderLabel = "OC_Incidents_Hire_label",
                    Enable = true,
                    IncidentType = IncidentTypes.Raid,
                    IncidentTypeName = "raid",
                    Label = "OC_Raid",
                    DelayBeforeStart = (mail) => mail.IncidentMult >= 5,
                    DelayMessageType = (_) => ModelMailMessadge.MessadgeTypes.ThreatBig,
                    DelayMessageLabel = (_) => "OC_Incidents_Raid_Warning_label",
                    DelayMessageText = (mail) =>
                    {
                        var factionParam = GetParamSafe(mail.IncidentParams, 1);
                        return string.Equals(factionParam, "mech", StringComparison.OrdinalIgnoreCase)
                            ? "OC_Incidents_Raid_Warning_Text_mech"
                            : "OC_Incidents_Raid_Warning_Text_human";
                    },
                    CalcCostMult = (incidentParams) =>
                    {
                        var arrivalModes = GetParamSafe(incidentParams, 0);
                        var faction = GetParamSafe(incidentParams, 1);

                        float arrMult = 1f;
                        if (string.Equals(arrivalModes, "random", StringComparison.OrdinalIgnoreCase))
                        {
                            arrMult = 1.5f;
                        }
                        else if (string.Equals(arrivalModes, "air", StringComparison.OrdinalIgnoreCase))
                        {
                            arrMult = 1.2f;
                        }

                        float facMult = 1f;
                        if (string.Equals(faction, "mech", StringComparison.OrdinalIgnoreCase))
                        {
                            facMult = 3.5f;
                        }
                        else if (string.Equals(faction, "pirate", StringComparison.OrdinalIgnoreCase))
                        {
                            facMult = 2f;
                        }
                        else if (string.Equals(faction, "randy", StringComparison.OrdinalIgnoreCase))
                        {
                            facMult = 2.5f;
                        }

                        return facMult * arrMult;
                    }
                },
                new IncidentMetadata
                {
                    NumberOrder = 3,
                    OrderLabel = "OC_Incidents_Hire_label",
                    Enable = false,
                    IncidentType = IncidentTypes.Bombing,
                    IncidentTypeName = "bomb",
                    Label = "Bombing",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label",
                    Enable = true,
                    IncidentType = IncidentTypes.Acid,
                    IncidentTypeName = "acid",
                    Label = "OC_Acid_Rain",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 2f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label",
                    Enable = true,
                    IncidentType = IncidentTypes.Plague,
                    IncidentTypeName = "plague",
                    Label = "OC_Plague",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label",
                    Enable = true,
                    IncidentType = IncidentTypes.EMP,
                    IncidentTypeName = "emp",
                    Label = "OC_EMP",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 1.2f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label",
                    Enable = true,
                    IncidentType = IncidentTypes.Eclipse,
                    IncidentTypeName = "eclipse",
                    Label = "OC_Eclipse",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 0.5f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label",
                    Enable = false,
                    IncidentType = IncidentTypes.Storm,
                    IncidentTypeName = "storm",
                    Label = "OC_Storm",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 1,
                    OrderLabel = "",
                    Enable = false,
                    IncidentType = IncidentTypes.Caravan,
                    IncidentTypeName = "caravan",
                    Label = "Caravan",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 1,
                    OrderLabel = "",
                    Enable = false,
                    IncidentType = IncidentTypes.ChunkDrop,
                    IncidentTypeName = "chunkdrop",
                    Label = "ChunkDrop",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 1,
                    OrderLabel = "",
                    Enable = false,
                    IncidentType = IncidentTypes.Quest,
                    IncidentTypeName = "quest",
                    Label = "Quest",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata
                {
                    NumberOrder = 0,
                    OrderLabel = "",
                    Enable = false,
                    IncidentType = IncidentTypes.Def,
                    IncidentTypeName = "def",
                    Label = "def",
                    DelayBeforeStart = (_) => false,
                    CalcCostMult = (_) => 0f,
                }
            };

            _incidentsByName = new Dictionary<string, IncidentMetadata>(_incidents.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _incidents.Count; i++)
            {
                var inc = _incidents[i];
                if (!string.IsNullOrEmpty(inc.IncidentTypeName))
                {
                    _incidentsByName[inc.IncidentTypeName] = inc;
                }
            }
        }

        public static IncidentMetadata ParseIncidentName(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return null;
            _incidentsByName.TryGetValue(arg.Trim(), out var result);
            return result;
        }

        private static string GetParamSafe(List<string> parameters, int index)
        {
            if (parameters == null || index < 0 || index >= parameters.Count)
            {
                return string.Empty;
            }
            return parameters[index]?.Trim() ?? string.Empty;
        }
    }

    public class IncidentMetadata
    {
        public int NumberOrder { get; set; }
        public string OrderLabel { get; set; }
        public bool Enable { get; set; }

        public IncidentTypes IncidentType { get; set; }
        public string IncidentTypeName { get; set; }
        public string Label { get; set; }

        public Func<ModelMailStartIncident, bool> DelayBeforeStart { get; set; }
        public Func<ModelMailStartIncident, ModelMailMessadge.MessadgeTypes> DelayMessageType { get; set; }
        public Func<ModelMailStartIncident, string> DelayMessageLabel { get; set; }
        public Func<ModelMailStartIncident, string> DelayMessageText { get; set; }

        public Func<List<string>, float> CalcCostMult { get; set; }
    }

    public enum IncidentTypes
    {
        Raid,
        Caravan,
        ChunkDrop,
        Infistation,
        Quest,
        Bombing,
        Acid,
        EMP,
        Pack,
        Eclipse,
        Storm,
        Plague,
        Def,
    }
}
