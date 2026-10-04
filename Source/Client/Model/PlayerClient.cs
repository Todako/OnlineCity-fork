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
        private const long CacheDurationTicks = 5L * TimeSpan.TicksPerSecond;

        public Player Public { get; set; }

        public bool Online
        {
            get
            {
                if (Public == null) return false;
                var now = DateTime.UtcNow;
                if (Public.LastOnlineTime == DateTime.MinValue)
                {
                    return Public.LastSaveTime > now.AddMinutes(-17);
                }
                var serverTimeDelta = SessionClientController.Data != null ? SessionClientController.Data.ServetTimeDelta : TimeSpan.Zero;
                return Public.LastOnlineTime > (now + serverTimeDelta).AddSeconds(-10);
            }
        }

        public int MinutesIntervalBetweenPVP => SessionClientController.Data != null ? SessionClientController.Data.MinutesIntervalBetweenPVP : 0;

        public List<CaravanOnline> WObjects;

        public string GetTextInfo()
        {
            UpdateTextInfoCalc();
            return TextInfo;
        }

        private string TextInfo = "";
        private string TextInfoExtended = "";
        private long _textInfoTicks = 0;

        public PlayerClient Refrash()
        {
            if (Public?.Login != null && SessionClientController.Data?.Players != null && SessionClientController.Data.Players.TryGetValue(Public.Login, out var player))
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
            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks - _allWorldObjectsTicks > CacheDurationTicks)
            {
                _allWorldObjectsTicks = nowTicks;
                AllWorldObjects = CostWorldObjects();
            }
            return AllWorldObjects;
        }

        private WorldObjectsValues AllWorldObjects = null;
        private long _allWorldObjectsTicks = 0;

        /// <summary>
        /// Розрахунок вартості з захистом від дублювання об'єктів за PlaceServerId.
        /// </summary>
        public WorldObjectsValues CostWorldObjects(long serverId = 0)
        {
            var values = new WorldObjectsValues();
            if (WObjects != null && WObjects.Count > 0)
            {
                var detailsSb = new StringBuilder(WObjects.Count * 64);
                var detailsExtSb = new StringBuilder(WObjects.Count * 64);
                var processedServerIds = new HashSet<long>(WObjects.Count);

                for (int i = 0; i < WObjects.Count; i++)
                {
                    var wo = WObjects[i];
                    if (wo?.OnlineWObject == null) continue;
                    if (serverId != 0 && wo.OnlineWObject.PlaceServerId != serverId) continue;

                    // Захист від дублікатів
                    if (!processedServerIds.Add(wo.OnlineWObject.PlaceServerId)) continue;

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

        #region Статичне кешування локалізованих міток та шаблону
        private static string _cachedTemplate;
        private static string _cachedPositionLabel;
        private static string _cachedInLabel;
        private static string _cachedDiscordLabel;
        private static string _cachedEmailLabel;
        private static string _cachedAboutMyLabel;
        private static string _cachedPvpInvolved;
        private static string _cachedPvpNotInvolved;
        private static TaggedString? _cachedLastSaveTimeNon;

        private static string GetTemplate()
        {
            if (_cachedTemplate == null)
            {
                _cachedTemplate = "OCity_PlayerClient_LastTick".Translate() + Environment.NewLine
                    + "OCity_PlayerClient_LastSaveTime".Translate() + Environment.NewLine
                    + "OCity_PlayerClient_baseCount".Translate() + Environment.NewLine
                    + "OCity_PlayerClient_caravanCount".Translate() + Environment.NewLine
                    + "OCity_PlayerClient_marketValue".Translate() + Environment.NewLine
                    + "OCity_PlayerClient_marketValuePawn".Translate() + Environment.NewLine
                    + "OCity_PlayerClient_marketValueTrading".Translate();
            }
            return _cachedTemplate;
        }
        #endregion

        private void UpdateTextInfoCalc()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks - _textInfoTicks < CacheDurationTicks) return;
            if (Public == null) return;

            var sb = new StringBuilder(512);
            var sbExt = new StringBuilder(512);

            if (!string.IsNullOrEmpty(Public.StateName))
            {
                if (_cachedInLabel == null) _cachedInLabel = "OC_PlayerClient_In".Translate().ToString();
                if (_cachedPositionLabel == null) _cachedPositionLabel = "OC_PlayerClient_Position".Translate().ToString();

                string statePos = string.IsNullOrEmpty(Public.StatePositionName)
                    ? string.Empty
                    : " " + _cachedPositionLabel + " " + Public.StatePositionName;

                sb.Append(_cachedInLabel);
                sb.Append(' ');
                sb.Append(Public.StateName);
                sb.Append(statePos);
                sb.AppendLine();

                sbExt.Append("<:world_map height=18:> ");
                sbExt.Append(_cachedInLabel);
                sbExt.Append(" <@");
                sbExt.Append(Public.StateName);
                sbExt.Append('>');
                sbExt.Append(statePos);
                sbExt.AppendLine();
            }

            // Додавання інформаційного блоку напряму без проміжного StringBuilder
            if (SessionClientController.Data != null && SessionClientController.Data.GeneralSettings.EnablePVP)
            {
                if (_cachedPvpInvolved == null) _cachedPvpInvolved = "OCity_PlayerClient_InvolvedInPVP".Translate().ToString();
                if (_cachedPvpNotInvolved == null) _cachedPvpNotInvolved = "OCity_PlayerClient_NotInvolvedInPVP".Translate().ToString();

                string pvpStr = Public.EnablePVP ? _cachedPvpInvolved : _cachedPvpNotInvolved;
                sb.AppendLine(pvpStr);
                sbExt.AppendLine(pvpStr);
            }

            if (!string.IsNullOrEmpty(Public.DiscordUserName))
            {
                if (_cachedDiscordLabel == null) _cachedDiscordLabel = "OCity_PlayerClient_Discord".Translate().ToString();
                sb.Append(_cachedDiscordLabel);
                sb.AppendLine(Public.DiscordUserName);
                sbExt.Append(_cachedDiscordLabel);
                sbExt.AppendLine(Public.DiscordUserName);
            }

            if (!string.IsNullOrEmpty(Public.EMail))
            {
                if (_cachedEmailLabel == null) _cachedEmailLabel = "OCity_PlayerClient_Email".Translate().ToString();
                sb.Append(_cachedEmailLabel);
                sb.AppendLine(Public.EMail);
                sbExt.Append(_cachedEmailLabel);
                sbExt.AppendLine(Public.EMail);
            }

            if (!string.IsNullOrEmpty(Public.AboutMyText))
            {
                if (_cachedAboutMyLabel == null) _cachedAboutMyLabel = "OCity_PlayerClient_AboutMyself".Translate().ToString();
                sb.AppendLine(_cachedAboutMyLabel);
                sb.AppendLine(Public.AboutMyText);
                sbExt.AppendLine(_cachedAboutMyLabel);
                sbExt.AppendLine(Public.AboutMyText);
            }
            sb.AppendLine();
            sbExt.AppendLine();

            _allWorldObjectsTicks = nowTicks;
            AllWorldObjects = CostWorldObjects();

            if (_cachedLastSaveTimeNon == null)
            {
                _cachedLastSaveTimeNon = "OCity_PlayerClient_LastSaveTimeNon".Translate();
            }

            TaggedString lastSaveStr = Public.LastSaveTime == DateTime.MinValue
                ? _cachedLastSaveTimeNon.Value
                : new TaggedString(Public.LastSaveTime.ToGoodUtcString());

            float totalTrading = AllWorldObjects.MarketValueBalance + AllWorldObjects.MarketValueStorage;
            string template = GetTemplate();

            string info3 = template.Translate(
                Public.LastTick / 3600000,
                Public.LastTick / 60000,
                lastSaveStr,
                AllWorldObjects.BaseCount,
                AllWorldObjects.CaravanCount,
                AllWorldObjects.MarketValue.ToStringMoney(),
                AllWorldObjects.MarketValuePawn.ToStringMoney(),
                totalTrading.ToStringMoney()
            ).ToString();

            string info3Extended = template.Translate(
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
            _textInfoTicks = DateTime.UtcNow.Ticks;
        }
    }
}