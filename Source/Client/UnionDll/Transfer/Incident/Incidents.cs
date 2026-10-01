using System;
using System.Collections.Generic;
using System.Linq;
using Transfer.ModelMails;

namespace OCUnion
{
    public static class Incidents
    {
        public static List<IncidentMetadata> AllIncidents
        {
            get
            {
                if (_Incidents == null) Init();
                return _Incidents;
            }
        }
        private static List<IncidentMetadata> _Incidents;

        private static void Init()
        {
            _Incidents = new List<IncidentMetadata>()
            {
                new IncidentMetadata()
                {
                    NumberOrder = 3,
                    OrderLabel = "OC_Incidents_Hire_label", // Найняти рейд — перша вкладка.
                    Enable = true,
                    IncidentType = IncidentTypes.Infistation,
                    IncidentTypeName = "inf",
                    Label = "OC_Bugs",
                    DelayBeforeStart = (mail) => mail.IncidentMult >= 5,
                    DelayMessageType = (mail) => ModelMailMessadge.MessadgeTypes.ThreatBig,
                    DelayMessageLabel = (mail) => "OC_Incidents_Raid_Warning_label",
                    DelayMessageText = (mail) => "OC_Incidents_Inf_Warning_Text_inf",
                    CalcCostMult = (_) => 3f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 3,
                    OrderLabel = "OC_Incidents_Hire_label", // Найняти рейд — перша вкладка.
                    Enable = true,
                    IncidentType = IncidentTypes.Raid,
                    IncidentTypeName = "raid",
                    Label = "OC_Raid",
                    DelayBeforeStart = (mail) => mail.IncidentMult >= 5,
                    DelayMessageType = (mail) => ModelMailMessadge.MessadgeTypes.ThreatBig,
                    DelayMessageLabel = (mail) => "OC_Incidents_Raid_Warning_label",
                    DelayMessageText = (mail) => mail.IncidentParams[1].ToLower().Trim() == "mech" ? "OC_Incidents_Raid_Warning_Text_mech" : "OC_Incidents_Raid_Warning_Text_human",
                    CalcCostMult = (incidentParams) =>
                    {
                        var arrivalModes = incidentParams[0].ToLower().Trim();
                        var faction = incidentParams[1].ToLower().Trim();

                        float arr_mult = 1f;
                        float fac_mult = 1f;
                        switch (arrivalModes)
                        {
                            case "random":
                                arr_mult = 1.5f;
                                break;
                            case "air":
                                arr_mult = 1.2f;
                                break;
                            default:
                                arr_mult = 1f;
                                break;
                        }
                        switch (faction)
                        {
                            case "mech":
                                fac_mult = 3.5f;
                                break;
                            case "pirate":
                                fac_mult = 2f;
                                break;
                            case "randy":
                                fac_mult = 2.5f;
                                break;
                            default:
                                fac_mult = 1f;
                                break;
                        }
                        return fac_mult * arr_mult;
                    }
                },
                new IncidentMetadata()
                {
                    NumberOrder = 3,
                    OrderLabel = "OC_Incidents_Hire_label", // Найняти рейд — перша вкладка.
                    Enable = false,
                    IncidentType = IncidentTypes.Bombing,
                    IncidentTypeName = "bomb",
                    Label = "Bombing",  //todo
                    DelayBeforeStart = (mail) => false, //todo
                    DelayMessageType = null, //todo
                    DelayMessageLabel = null, //todo
                    DelayMessageText = null, //todo
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label", // Вплив на область — друга вкладка.
                    Enable = true,
                    IncidentType = IncidentTypes.Acid,
                    IncidentTypeName = "acid",
                    Label = "OC_Acid_Rain", //Кислотный дождь
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 2f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label", // Вплив на область — друга вкладка.
                    Enable = true,
                    IncidentType = IncidentTypes.Plague,
                    IncidentTypeName = "plague",
                    Label = "OC_Plague", //Эпидемия
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label", // Вплив на область — друга вкладка.
                    Enable = true,
                    IncidentType = IncidentTypes.EMP,
                    IncidentTypeName = "emp",
                    Label = "OC_EMP", //Э.М.И.
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 1.2f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label", // Вплив на область — друга вкладка.
                    Enable = true,
                    IncidentType = IncidentTypes.Eclipse,
                    IncidentTypeName = "eclipse",
                    Label = "OC_Eclipse", //Поглотитель света
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 0.5f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 2,
                    OrderLabel = "OC_Incidents_Impact_label", // Вплив на область — друга вкладка.
                    Enable = false,
                    IncidentType = IncidentTypes.Storm,
                    IncidentTypeName = "storm",
                    Label = "OC_Storm", // Шторм.
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 1,
                    OrderLabel = "", // Позитивні події — відсутні в інтерфейсі. //todo?
                    Enable = false,
                    IncidentType = IncidentTypes.Caravan,
                    IncidentTypeName = "caravan",
                    Label = "Caravan", //todo?
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 1,
                    OrderLabel = "", // Позитивні події — відсутні в інтерфейсі. //todo?
                    Enable = false,
                    IncidentType = IncidentTypes.ChunkDrop,
                    IncidentTypeName = "chunkdrop",
                    Label = "ChunkDrop", //todo?
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 1,
                    OrderLabel = "", // Позитивні події — відсутні в інтерфейсі. //todo?
                    Enable = false,
                    IncidentType = IncidentTypes.Quest,
                    IncidentTypeName = "quest",
                    Label = "Quest", //todo?
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 1f,
                },
                new IncidentMetadata()
                {
                    NumberOrder = 0,
                    OrderLabel = "", // Особливі події миттєвої дії, завжди без інтерфейсу.
                    Enable = false,
                    IncidentType = IncidentTypes.Def,
                    IncidentTypeName = "def",
                    Label = "def",
                    DelayBeforeStart = (mail) => false,
                    CalcCostMult = (_) => 0, // У "def" параметр використовується, але не впливає на ціну: вона завжди дорівнює 0, оскільки "def" доступний лише адміністратору.
                },
            };
        }

        public static IncidentMetadata ParseIncidentName(string arg)
        {
            var name = (arg ?? "").ToLower().Trim();
            return Incidents.AllIncidents.FirstOrDefault(i => i.IncidentTypeName == name);
        }
    }
    public class IncidentMetadata
    {
        /// <summary>
        /// Номер черги, залежить від типу події.
        /// 0 — без черги та очікування.
        /// 1 — позитивна подія.
        /// 2 — стихійне лихо.
        /// 3 — агресивна подія з попередженням і очікуванням перед початком (якщо рівень >= 5).
        /// </summary>
        public int NumberOrder { get; set; }
        public string OrderLabel { get; set; }
        /// <summary>
        /// Чи увімкнено.
        /// </summary>
        public bool Enable { get; set; }

        public IncidentTypes IncidentType { get; set; }
        /// <summary>
        /// Ідентифікатор без локалізації, лише в нижньому регістрі й без пробілів.
        /// </summary>
        public string IncidentTypeName { get; set; }
        public string Label { get; set; }

        /// <summary>
        /// Чи робити затримку перед інцидентом; якщо так, перед нею буде показано додаткове повідомлення.
        /// </summary>
        public Func<ModelMailStartIncident, bool> DelayBeforeStart { get; set; }
        public Func<ModelMailStartIncident, ModelMailMessadge.MessadgeTypes> DelayMessageType { get; set; }
        public Func<ModelMailStartIncident, string> DelayMessageLabel { get; set; }
        public Func<ModelMailStartIncident, string> DelayMessageText { get; set; }

        /// <summary>
        /// Множник вартості; параметри передаються в mail.IncidentParams.
        /// </summary>
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
