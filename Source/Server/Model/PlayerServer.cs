using Model;
using OCUnion;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Services;
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Transfer;
using Transfer.ModelMails;
using Util;

namespace ServerOnlineCity.Model
{
    [Serializable]
    public class PlayerServer : IPlayerEx
    {
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
                return Public.LastOnlineTime > now.AddSeconds(-10);
            }
        }

        public int MinutesIntervalBetweenPVP => ServerManager.ServerSettings.MinutesIntervalBetweenPVP;

        public string Pass;

        public bool IsAdmin => (Public?.Grants & (Grants.SuperAdmin | Grants.Moderator)) > Grants.NoPermissions;

        public bool Approve { get; set; }

        public Guid DiscordToken;

        public float CashlessBalance;

        [NonSerialized]
        public float StorageBalance;

        [NonSerialized]
        public DisconnectReason ExitReason;

        [NonSerialized]
        public bool ApproveLoadWorldReason;

        [XmlIgnore]
        public Dictionary<Chat, ModelUpdateTime> Chats;

        public DateTime SaveDataPacketTime;

        public DateTime LastUpdateTime;

        [XmlIgnore]
        public List<ModelMail> Mails = new List<ModelMail>();

        [XmlIgnore]
        public List<ModelMail> MailsConfirmationSave = new List<ModelMail>();

        [XmlIgnore]
        public List<IFunctionMail> FunctionMails = new List<IFunctionMail>();

        [XmlIgnore]
        public List<TradeThingStorage> TradeThingStorages = new List<TradeThingStorage>();

        [NonSerialized]
        [XmlIgnore]
        public AttackServer AttackData;

        public int SettingDelaySaveGame;

        public bool SettingEnableFileLog;

        public DateTime TimeChangeEnablePVP;

        public DateTime PVPHostLastTime;

        private DateTime KeyReconnectTime;

        public string KeyReconnect1;

        [NonSerialized]
        private string KeyReconnect2;

        public string IntruderKeys;

        [NonSerialized]
        [XmlIgnore]
        public PlayerGameProgress GameProgressLast;

        [NonSerialized]
        [XmlIgnore]
        public PlayerGameProgress GameProgressLastDay;

        public PlayerGameProgress GameProgress;

        [NonSerialized]
        public bool LastUpdateWithMail;

        public int AttacksWonCount;

        public int AttacksInitiatorCount;

        public float StartMarketValue;
        public float StartMarketValuePawn;

        public float LastMarketValue;
        public float LastMarketValuePawn;
        public float LastMarketValueBalance;
        public float LastMarketValueStorage;

        public float DeltaMarketValue;
        public float DeltaMarketValuePawn;
        public float DeltaMarketValueBalance;
        public float DeltaMarketValueStorage;

        public float SumDeltaGameMarketValue;
        public float SumDeltaGameMarketValuePawn;
        public float SumDeltaGameMarketValueBalance;
        public float SumDeltaGameMarketValueStorage;

        public float SumDeltaRealMarketValue;
        public float SumDeltaRealMarketValuePawn;
        public float SumDeltaRealMarketValueBalance;
        public float SumDeltaRealMarketValueStorage;

        public long SumDeltaRealTicks;

        public long SumDeltaRealSecond;

        public long TotalRealSecond;

        public float StatMaxDeltaGameMarketValue;
        public float StatMaxDeltaGameMarketValuePawn;
        public float StatMaxDeltaGameMarketValueBalance;
        public float StatMaxDeltaGameMarketValueStorage;
        public float StatMaxDeltaGameMarketValueTotal;

        public float StatMaxDeltaRealMarketValue;
        public float StatMaxDeltaRealMarketValuePawn;
        public float StatMaxDeltaRealMarketValueBalance;
        public float StatMaxDeltaRealMarketValueStorage;
        public float StatMaxDeltaRealMarketValueTotal;

        public long StatMaxDeltaRealTicks;

        [NonSerialized]
        public DateTime StatLastUpdateTime;

        [NonSerialized]
        public long StatLastTick;

        [NonSerialized]
        public bool LastUpdateIsGood;

        [NonSerialized]
        public DateTime WLastUpdateTime;

        [NonSerialized]
        public long WLastTick;

        [NonSerialized]
        public StatePosition StatePosition;

        public List<float> MarketValueHistory;

        public int MarketValueRanking;

        public int MarketValueRankingLast;

        private static readonly CryptoProvider _crypto = new CryptoProvider();

        private PlayerServer()
        {
            ExitReason = DisconnectReason.AllGood;
            ApproveLoadWorldReason = true;
        }

        public PlayerServer(string login)
        {
            Public = new Player
            {
                Login = login
            };

            Chats = new Dictionary<Chat, ModelUpdateTime>(1);
            Chats.Add(ChatManager.Instance.PublicChat, new ModelUpdateTime { Value = -1 });
        }

        public void MarketValueHistoryAdd(float value)
        {
            if (MarketValueHistory == null) MarketValueHistory = new List<float>(64);
            MarketValueHistory.Add(value);
            if (MarketValueHistory.Count > 60) MarketValueHistory.RemoveAt(0);
        }

        public WorldObjectsValues CostWorldObjects(long serverId = 0)
        {
            var values = new WorldObjectsValues();
            var data = Repository.GetData;
            if (data?.WorldObjects == null || Public?.Login == null) return values;

            string myLogin = Public.Login;
            var baseIds = new List<long>();
            var processedServerIds = new HashSet<long>();

            lock (data)
            {
                for (int i = 0; i < data.WorldObjects.Count; i++)
                {
                    var wo = data.WorldObjects[i];
                    if (wo == null || wo.LoginOwner != myLogin) continue;
                    if (serverId != 0 && wo.PlaceServerId != serverId) continue;
                    if (!processedServerIds.Add(wo.PlaceServerId)) continue;

                    values.MarketValue += wo.MarketValue;
                    values.MarketValuePawn += wo.MarketValuePawn;
                    values.MarketValueBalance += wo.MarketValueBalance;
                    values.MarketValueStorage += wo.MarketValueStorage;

                    if (wo.Type == WorldObjectEntryType.Base)
                    {
                        values.BaseCount++;
                        baseIds.Add(wo.PlaceServerId);
                    }
                    else
                    {
                        values.CaravanCount++;
                    }
                }
            }

            if (baseIds.Count > 0)
            {
                values.BaseServerIds = string.Join(",", baseIds);
            }

            return values;
        }

        [NonSerialized]
        private WorldObjectsValues CostWorldObjectsCache = null;
        [NonSerialized]
        private long CostWorldObjectsCacheTicks = 0;

        public WorldObjectsValues CostWorldObjectsWithCache()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks > CostWorldObjectsCacheTicks || CostWorldObjectsCache == null)
            {
                CostWorldObjectsCache = CostWorldObjects();
                CostWorldObjectsCacheTicks = nowTicks + 30L * TimeSpan.TicksPerSecond;
            }
            return CostWorldObjectsCache;
        }

        public float AllCostWorldObjects()
        {
            var costAll = CostWorldObjects();
            if (costAll.BaseCount + costAll.CaravanCount == 0) return 0;
            if (costAll.MarketValueTotal == 0) return -1;
            return costAll.MarketValueTotal;
        }

        /// <summary>
        /// Оновлення вартості товарів у сховищі через O(N) розрахунок без виділення пам'яті (0 байт GC).
        /// </summary>
        public void UpdateStorageBalance()
        {
            var data = Repository.GetData;
            StorageBalance = data?.OrderOperator?.CalcStorageBalance(this) ?? 0f;
        }

        public bool GetKeyReconnect()
        {
            lock (this)
            {
                var now = DateTime.UtcNow;
                if ((now - KeyReconnectTime).TotalMinutes < 30 && !string.IsNullOrEmpty(KeyReconnect1))
                {
                    return false;
                }

                KeyReconnectTime = now;
                var rnd = new Random((int)(now.Ticks & int.MaxValue));
                var key = "o6*#fn`~ыggTgj0&9 gT54Qa[g}t,23rfr7*vcx%%4/\"d!2"
                    + rnd.Next(int.MaxValue)
                    + now.Date.AddHours(now.Hour).ToBinary()
                    + (Public?.Login ?? string.Empty);

                var hash = _crypto.GetHash(key);

                KeyReconnect2 = KeyReconnect1;
                KeyReconnect1 = hash;

                return true;
            }
        }

        public bool KeyReconnectVerification(string testKey)
        {
            if (string.IsNullOrEmpty(testKey)) return false;
            lock (this)
            {
                GetKeyReconnect();
                return KeyReconnect1 == testKey || KeyReconnect2 == testKey;
            }
        }

        public void AbandonSettlement()
        {
            if (Public?.Login == null) return;
            Repository.DropUserFromMap(Public.Login);

            lock (this)
            {
                Mails = new List<ModelMail>();
                MailsConfirmationSave = new List<ModelMail>();
                FunctionMails = new List<IFunctionMail>();
                TradeThingStorages = new List<TradeThingStorage>();
            }

            Repository.GetSaveData.DeletePlayerData(Public.Login);
            Public.LastSaveTime = DateTime.MinValue;
            Repository.Get.ChangeData = true;
        }

        public void Delete()
        {
            var data = Repository.GetData;
            if (data?.PlayersAll == null) return;

            lock (data.PlayersAll)
            {
                data.PlayersAll.Remove(this);
                data.UpdatePlayersAllDic();
                Repository.Get.ChangeData = true;
            }
        }
    }

    [Flags]
    public enum PossiblyIntruderType : int
    {
        LevelNone = 0,
        LevelSuspicious = 0xFFFF,
    }
}