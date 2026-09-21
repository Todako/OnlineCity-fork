using Model;
using OCUnion;
using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace RimWorldOnlineCity
{
    public class PlayerClient : IPlayerEx
    {
        public Player Public { get; set; }

        public bool Online =>
            Public.LastOnlineTime == DateTime.MinValue ? Public.LastSaveTime > DateTime.UtcNow.AddMinutes(-17) :
            Public.LastOnlineTime > (DateTime.UtcNow + SessionClientController.Data.ServetTimeDelta).AddSeconds(-10);

        public int MinutesIntervalBetweenPVP => SessionClientController.Data.MinutesIntervalBetweenPVP;

        public List<CaravanOnline> WObjects;

        public string GetTextInfo()
        {
            UpdateTextInfoCalc();
            return TextInfo;
        }
        private string TextInfo = "";
        private string TextInfoExtended = "";
        private DateTime TextInfoTime = DateTime.MinValue;

        /// <summary>
        /// Швидке оновлення посилання на гравця з кешу.
        /// ОПТИМІЗАЦІЯ: замінено подвійний пошук (ContainsKey + []) на швидкий TryGetValue.
        /// </summary>
        public PlayerClient Refrash()
        {
            if (Public?.Login != null && SessionClientController.Data.Players.TryGetValue(Public.Login, out var player))
            {
                return player;
            }
            return this;
        }

        public string GetTextInfoExtended()
        {
            UpdateTextInfoCalc();
            return TextInfoExtended;
        }

        public WorldObjectsValues CostAllWorldObjects()
        {
            if (AllWorldObjectsTime < DateTime.UtcNow.AddSeconds(-5))
            {
                AllWorldObjectsTime = DateTime.UtcNow;
                AllWorldObjects = CostWorldObjects();
            }
            return AllWorldObjects;
        }
        private WorldObjectsValues AllWorldObjects = null;
        private DateTime AllWorldObjectsTime = DateTime.MinValue;

        /// <summary>
        /// Розрахунок загальної вартості караванів і поселень гравця.
        /// ОПТИМІЗАЦІЯ: ліквідовано конкатенацію рядків у циклі через StringBuilder.
        /// </summary>
        public WorldObjectsValues CostWorldObjects(long serverId = 0)
        {
            var values = new WorldObjectsValues();
            if (WObjects != null && WObjects.Count > 0)
            {
                var detailsSb = new StringBuilder(WObjects.Count * 64);
                var detailsExtSb = new StringBuilder(WObjects.Count * 64);

                for (int i = 0; i < WObjects.Count; i++)
                {
                    var wo = WObjects[i];
                    if (wo?.OnlineWObject == null) continue;
                    if (serverId != 0 && wo.OnlineWObject.PlaceServerId != serverId) continue;

                    values.MarketValue += wo.OnlineWObject.MarketValue;
                    values.MarketValuePawn += wo.OnlineWObject.MarketValuePawn;
                    values.MarketValueBalance += wo.OnlineWObject.MarketValueBalance;
                    values.MarketValueStorage += wo.OnlineWObject.MarketValueStorage;

                    if (wo is BaseOnline)
                    {
                        values.BaseCount++;
                    }
                    else
                    {
                        values.CaravanCount++;
                    }

                    detailsSb.AppendLine();
                    detailsSb.AppendLine();
                    detailsSb.Append(wo.GetInspectString());

                    detailsExtSb.AppendLine();
                    detailsExtSb.AppendLine();
                    detailsExtSb.Append(wo.GetInspectExtendedString());
                }

                values.Details = detailsSb.ToString();
                values.DetailsExtended = detailsExtSb.ToString();
            }
            return values;
        }

        /// <summary>
        /// Формування опису профілю гравця з кешуванням на 5 секунд.
        /// ОПТИМІЗАЦІЯ: збирання рядків через StringBuilder без зайвих проміжних об'єктів.
        /// </summary>
        private void UpdateTextInfoCalc()
        {
            if (TextInfoTime >= DateTime.UtcNow.AddSeconds(-5)) return;

            var sb = new StringBuilder(512);
            var sbExt = new StringBuilder(512);

            // Блок 1: Інформація про державу
            if (!string.IsNullOrEmpty(Public.StateName))
            {
                string statePos = string.IsNullOrEmpty(Public.StatePositionName)
                    ? string.Empty
                    : " " + "OC_PlayerClient_Position".Translate().ToString() + " " + Public.StatePositionName;

                sb.Append("OC_PlayerClient_In".Translate().ToString());
                sb.Append(' ');
                sb.Append(Public.StateName);
                sb.Append(statePos);
                sb.AppendLine();

                sbExt.Append("<:world_map height=18:> ");
                sbExt.Append("OC_PlayerClient_In".Translate().ToString());
                sbExt.Append(" <@");
                sbExt.Append(Public.StateName);
                sbExt.Append('>');
                sbExt.Append(statePos);
                sbExt.AppendLine();
            }

            // Блок 2: Контактні дані та статус PVP
            var info2Sb = new StringBuilder(256);
            if (SessionClientController.Data.GeneralSettings.EnablePVP)
            {
                info2Sb.AppendLine((Public.EnablePVP ? "OCity_PlayerClient_InvolvedInPVP".Translate() : "OCity_PlayerClient_NotInvolvedInPVP".Translate()).ToString());
            }

            if (!string.IsNullOrEmpty(Public.DiscordUserName))
            {
                info2Sb.Append("OCity_PlayerClient_Discord".Translate().ToString());
                info2Sb.AppendLine(Public.DiscordUserName);
            }

            if (!string.IsNullOrEmpty(Public.EMail))
            {
                info2Sb.Append("OCity_PlayerClient_Email".Translate().ToString());
                info2Sb.AppendLine(Public.EMail);
            }

            if (!string.IsNullOrEmpty(Public.AboutMyText))
            {
                info2Sb.AppendLine("OCity_PlayerClient_AboutMyself".Translate().ToString());
                info2Sb.AppendLine(Public.AboutMyText);
            }
            info2Sb.AppendLine();

            string info2 = info2Sb.ToString();
            sb.Append(info2);
            sbExt.Append(info2);

            AllWorldObjectsTime = DateTime.UtcNow;
            AllWorldObjects = CostWorldObjects();

            // Блок 3: Статистика поселень та ринкова вартість
            string s = "OCity_PlayerClient_LastTick".Translate() + Environment.NewLine
                + "OCity_PlayerClient_LastSaveTime".Translate() + Environment.NewLine
                + "OCity_PlayerClient_baseCount".Translate() + Environment.NewLine
                + "OCity_PlayerClient_caravanCount".Translate() + Environment.NewLine
                + "OCity_PlayerClient_marketValue".Translate() + Environment.NewLine
                + "OCity_PlayerClient_marketValuePawn".Translate() + Environment.NewLine
                + "OCity_PlayerClient_marketValueTrading".Translate();

            TaggedString lastSaveStr = Public.LastSaveTime == DateTime.MinValue
                ? "OCity_PlayerClient_LastSaveTimeNon".Translate()
                : new TaggedString(Public.LastSaveTime.ToGoodUtcString());

            float totalTrading = AllWorldObjects.MarketValueBalance + AllWorldObjects.MarketValueStorage;

            string info3 = s.Translate(
                Public.LastTick / 3600000,
                Public.LastTick / 60000,
                lastSaveStr,
                AllWorldObjects.BaseCount,
                AllWorldObjects.CaravanCount,
                AllWorldObjects.MarketValue.ToStringMoney(),
                AllWorldObjects.MarketValuePawn.ToStringMoney(),
                totalTrading.ToStringMoney()
            ).ToString();

            string info3Extended = s.Translate(
                Public.LastTick / 3600000,
                Public.LastTick / 60000,
                lastSaveStr,
                AllWorldObjects.BaseCount,
                AllWorldObjects.CaravanCount,
                "<:money_bag height=16:> " + AllWorldObjects.MarketValue.ToStringMoney(),
                "<:busts_in_silhouette height=16:> " + AllWorldObjects.MarketValuePawn.ToStringMoney(),
                "<:chart_increasing height=16:> " + totalTrading.ToStringMoney()
            ).ToString();

            sb.Append(info3);
            sb.Append(AllWorldObjects.Details);

            sbExt.Append(info3Extended);
            sbExt.Append(AllWorldObjects.DetailsExtended);

            TextInfo = sb.ToString();
            TextInfoExtended = ChatController.PrepareShortTag(sbExt.ToString());
            TextInfoTime = DateTime.UtcNow;
        }
    }
}