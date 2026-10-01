using System;
using System.Collections.Generic;
using System.Linq;

namespace OCUnion
{
    [Serializable]
    public struct ServerGeneralSettings
    {
        /// <summary>
        /// Def оповідача.
        /// </summary>
        public string StorytellerDef { get; set; }

        /// <summary>
        /// Складність.
        /// </summary>
        public string Difficulty { get; set; }

        /// <summary>
        /// Увімкнути режим онлайн-нападів гравців один на одного з обмеженим керуванням.
        /// </summary>
        public bool EnablePVP { get; set; }

        /// <summary>
        /// Заборонити змінювати налаштування оповідача та модифікацій у грі.
        /// </summary>
        public bool DisableGameSettings { get; set; }

        /// <summary>
        /// Чи дозволені інциденти.
        /// </summary>
        public bool IncidentEnable { get; set; }

        /// <summary>
        /// Скільки інцидентів дозволено в черзі.
        /// </summary>
        public int IncidentCountInOffline { get; set; }

        /// <summary>
        /// Максимальний коефіцієнт сили інцидентів.
        /// </summary>
        public int IncidentMaxMult { get; set; }

        /// <summary>
        /// Мінімальна пауза між інцидентами в тіках (1 день = 60000).
        /// </summary>
        public int IncidentTickDelayBetween { get; set; }

        /// <summary>
        /// Модифікатор вартості найму у відсотках.
        /// </summary>
        public int IncidentCostPrecent { get; set; }

        /// <summary>
        /// Модифікатор сили рейдів у відсотках.
        /// </summary>
        public int IncidentPowerPrecent { get; set; }

        /// <summary>
        /// Модифікатор перерви після рейду.
        /// </summary>
        public int IncidentCoolDownPercent { get; set; }

        /// <summary>
        /// Модифікатор попередження перед рейдом.
        /// </summary>
        public int IncidentAlarmInHours { get; set; }

        /// <summary>
        /// Увімкнути синхронізацію об'єктів на планеті між усіма гравцями (WIP).
        /// </summary>
        public bool EquableWorldObjects { get; set; }

        /// <summary>
        /// Увімкнути біржу (WIP).
        /// </summary>
        public bool ExchengeEnable { get; set; }

        /// <summary>
        /// Увімкнути вибір місця старту.
        /// </summary>
        public bool ScenarioAviable { get; set; }

        /// <summary>
        /// Вартість речей на біржі, в угодах і на рахунку для рейдів на 1000 срібла (50 — це 5%). Рекомендоване значення: 1000 або 1200.
        /// </summary>
        public int ExchengePrecentWealthForIncident { get; set; }

        /// <summary>
        /// Комісія за переказ еквівалента 1000 срібла на безготівковий рахунок або назад (50 — це 5%).
        /// </summary>
        public int ExchengePrecentCommissionConvertToCashlessCurrency { get; set; }

        /// <summary>
        /// Вартість доставки еквівалента 1000 срібла на відстань 100 клітинок (це приблизно 1/15 планети по екватору; при /10 — вартість доставки сусідам).
        /// </summary>
        public int ExchengeCostCargoDelivery { get; set; }

        /// <summary>
        /// Відсоток надбавки до ціни за швидку доставку.
        /// </summary>
        public int ExchengeAddPrecentCostForFastCargoDelivery { get; set; }

        /// <summary>
        /// Перелік defName речей, заборонених до передавання, через кому.
        /// </summary>
        public string ExchengeForbiddenDefNames { get; set; }
        [NonSerialized]
        private HashSet<string> ExchengeForbiddenDefNamesListData;
        public HashSet<string> ExchengeForbiddenDefNamesList
        {
            get
            {
                if (ExchengeForbiddenDefNamesListData == null)
                    if (string.IsNullOrEmpty(ExchengeForbiddenDefNames))
                        ExchengeForbiddenDefNamesListData = new HashSet<string>();
                    else
                        ExchengeForbiddenDefNamesListData = new HashSet<string>(ExchengeForbiddenDefNames.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
                return ExchengeForbiddenDefNamesListData;
            }
        }

        /// <summary>
        /// Встановлює початковий рік у грі замість 5500.
        /// </summary>
        public int StartGameYear { get; set; }

        /// <summary>
        /// Попередження під час входу на сервер.
        /// </summary>
        public string EntranceWarning { get; set; }

        /// <summary>
        /// Попередження під час входу на сервер російською мовою.
        /// </summary>
        public string EntranceWarningRussian { get; set; }

        /// <summary>
        /// Робити знімки колоній щодня опівдні.
        /// </summary>
        public bool ColonyScreenEnable { get; set; }

        /// <summary>
        /// Висока якість знімків колоній.
        /// </summary>
        public bool ColonyScreenHighQuality { get; set; }

        /// <summary>
        /// Інтервал між знімками колоній у днях: від 1 до 60 (якщо більше 59 — знімок раз на рік).
        /// </summary>
        public int ColonyScreenDelayDays { get; set; }

        public ServerGeneralSettings SetDefault()
        {
            StorytellerDef = "";

            Difficulty = "";

            EnablePVP = false;

            DisableGameSettings = false;

            IncidentEnable = true;

            IncidentCountInOffline = 2;

            IncidentMaxMult = 10;

            IncidentTickDelayBetween = 60000;

            IncidentCostPrecent = 100;

            IncidentPowerPrecent = 100;

            IncidentCoolDownPercent = 100;

            IncidentAlarmInHours = 10;

            EquableWorldObjects = false;

            ScenarioAviable = true;

            ExchengeEnable = true;

            ExchengePrecentWealthForIncident = 1000;

            ExchengePrecentCommissionConvertToCashlessCurrency = 50;

            ExchengeCostCargoDelivery = 1000;

            ExchengeAddPrecentCostForFastCargoDelivery = 100;

            StartGameYear = -1;

            ColonyScreenEnable = true;

            ColonyScreenHighQuality = true;

            ColonyScreenDelayDays = 1;

            return this;
        }

    }
}
