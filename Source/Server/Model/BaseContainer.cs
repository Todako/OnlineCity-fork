using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using Server.Mechanics;
using ServerOnlineCity.Mechanics;
using ServerOnlineCity.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Transfer;

namespace ServerOnlineCity.Model
{
    /// <summary>
    /// Головне сховище стану сервера: зберігає облікові записи гравців, стан світу, 
    /// активні торгові ордери, держави та списки видалених об'єктів.
    /// </summary>
    [Serializable]
    public class BaseContainer
    {
        public string Version { get; set; }
        public long VersionNum { get; set; }

        public List<PlayerServer> PlayersAll { get; set; }
        public PlayerServer PlayerSystem
        {
            get
            {
                var all = PlayersAll;
                return (all != null && all.Count > 0) ? all[0] : null;
            }
        }

        [NonSerialized]
        public ICollection<string> GetPlayerLoginsAll;

        [NonSerialized]
        public ICollection<PlayerServer> GetPlayersAll;

        [NonSerialized]
        public ConcurrentDictionary<string, PlayerServer> PlayersAllDic;

        [NonSerialized]
        public ConcurrentDictionary<string, PlayerServer> PlayersAllDicWithNotApprove;

        public DateTime RankingUpdate { get; set; }
        public List<string> PlayersRanking { get; set; }
        public List<string> StatesRanking { get; set; }
        public List<string> PlayersRankingLast { get; set; }
        public List<string> StatesRankingLast { get; set; }

        /// <summary>
        /// Потокобезпечне атомарне оновлення словників та наборів гравців із точним виділенням ємності.
        /// </summary>
        public void UpdatePlayersAllDic()
        {
            var all = PlayersAll;
            if (all == null) return;

            int concurrencyLevel = Environment.ProcessorCount;

            ConcurrentDictionary<string, PlayerServer> approvedDic;
            ConcurrentDictionary<string, PlayerServer> allDic;
            HashSet<PlayerServer> approvedList;
            HashSet<string> approvedLogins;

            lock (all)
            {
                int capacity = Math.Max(32, all.Count);
                approvedDic = new ConcurrentDictionary<string, PlayerServer>(concurrencyLevel, capacity, StringComparer.Ordinal);
                allDic = new ConcurrentDictionary<string, PlayerServer>(concurrencyLevel, capacity, StringComparer.Ordinal);
                approvedList = new HashSet<PlayerServer>();
                approvedLogins = new HashSet<string>(capacity, StringComparer.Ordinal);

                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p?.Public?.Login == null) continue;

                    allDic[p.Public.Login] = p;
                    if (p.Approve)
                    {
                        approvedDic[p.Public.Login] = p;
                        approvedList.Add(p);
                        approvedLogins.Add(p.Public.Login);
                    }
                }
            }

            // Атомарна підміна посилань
            PlayersAllDic = approvedDic;
            PlayersAllDicWithNotApprove = allDic;
            GetPlayersAll = approvedList;
            GetPlayerLoginsAll = approvedLogins;
        }

        public string WorldSeed { get; set; }
        public string WorldScenarioName { get; set; }
        public string WorldStoryteller { get; set; }
        public string WorldDifficulty { get; set; }
        public int WorldMapSize { get; set; }
        public float WorldPlanetCoverage { get; set; }
        public long MaxServerIdWorldObjectEntry;
        public int MaxIdChat { get; set; }

        public int MaxPlayerId;

        public List<WorldObjectEntry> WorldObjects { get; set; }
        [NonSerialized]
        public List<WorldObjectEntry> WorldObjectsDeleted = new List<WorldObjectEntry>();

        public List<TradeOrder> Orders { get; set; }

        public Dictionary<int, long> OrdersPlaceServerIdByTile { get; set; }

        public ExchengeOperator OrderOperator => _OrderOperator;

        [NonSerialized]
        private ExchengeOperator _OrderOperator;

        [NonSerialized]
        public ConcurrentDictionary<long, string> UploadService;

        internal long SetInUploadService(string data)
        {
            if (data == null) return 0;
            long hash = (long)data.GetHashCode();
            if (UploadService == null)
            {
                UploadService = new ConcurrentDictionary<long, string>(Environment.ProcessorCount, 32);
            }
            UploadService[hash] = data;
            return hash;
        }

        // Об'єкти світу онлайн (NPC, фракції)
        public List<WorldObjectOnline> WorldObjectOnlineList { get; set; }
        public List<FactionOnline> FactionOnlineList { get; set; }

        [NonSerialized]
        public bool EverybodyLogoff;

        public List<State> States { get; set; }

        public List<StatePosition> StatePositions { get; set; }

        public StateOperator StateOperator => _StateOperator;

        [NonSerialized]
        private StateOperator _StateOperator;

        [NonSerialized]
        public ICollection<State> GetStates;

        [NonSerialized]
        public ConcurrentDictionary<string, State> StatesDic;

        [NonSerialized]
        public ConcurrentDictionary<string, ConcurrentDictionary<string, StatePosition>> StatePositionsDic;

        [NonSerialized]
        private ConcurrentDictionary<string, HashSet<PlayerServer>> StatePlayersDic;

        [NonSerialized]
        public DateTime StateUpdateTime;

        private static readonly HashSet<PlayerServer> EmptyPlayerSet = new HashSet<PlayerServer>();

        /// <summary>
        /// Потокобезпечне оновлення держав та посад зі зниженою складністю O(N+M) замість O(N*M).
        /// </summary>
        public void UpdateStatesDic()
        {
            var states = States;
            var positions = StatePositions;
            if (states == null) return;

            int concurrencyLevel = Environment.ProcessorCount;

            ConcurrentDictionary<string, State> statesDic;
            HashSet<State> statesList;
            ConcurrentDictionary<string, ConcurrentDictionary<string, StatePosition>> positionsDic;

            lock (states)
            {
                int statesCount = Math.Max(16, states.Count);
                statesDic = new ConcurrentDictionary<string, State>(concurrencyLevel, statesCount, StringComparer.Ordinal);
                statesList = new HashSet<State>();
                positionsDic = new ConcurrentDictionary<string, ConcurrentDictionary<string, StatePosition>>(concurrencyLevel, statesCount, StringComparer.Ordinal);

                // Одноразове групування посад за назвою держави з єдиним блокуванням
                var positionsByState = new Dictionary<string, List<StatePosition>>(statesCount, StringComparer.Ordinal);
                if (positions != null)
                {
                    lock (positions)
                    {
                        for (int j = 0; j < positions.Count; j++)
                        {
                            var pos = positions[j];
                            if (pos != null && !string.IsNullOrEmpty(pos.StateName) && !string.IsNullOrEmpty(pos.Name))
                            {
                                if (!positionsByState.TryGetValue(pos.StateName, out var list))
                                {
                                    list = new List<StatePosition>();
                                    positionsByState[pos.StateName] = list;
                                }
                                list.Add(pos);
                            }
                        }
                    }
                }

                for (int i = 0; i < states.Count; i++)
                {
                    var s = states[i];
                    if (string.IsNullOrEmpty(s?.Name)) continue;

                    statesDic[s.Name] = s;
                    statesList.Add(s);

                    var spDic = new ConcurrentDictionary<string, StatePosition>(concurrencyLevel, 8, StringComparer.Ordinal);
                    if (positionsByState.TryGetValue(s.Name, out var posList))
                    {
                        for (int k = 0; k < posList.Count; k++)
                        {
                            var pos = posList[k];
                            spDic[pos.Name] = pos;
                        }
                    }
                    positionsDic[s.Name] = spDic;
                }
            }

            StatesDic = statesDic;
            GetStates = statesList;
            StatePositionsDic = positionsDic;
            StatePlayersDic = new ConcurrentDictionary<string, HashSet<PlayerServer>>(concurrencyLevel, Math.Max(16, states.Count), StringComparer.Ordinal);
            StateUpdateTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Повертає список гравців вказаної держави без створення замикань і делегатів.
        /// </summary>
        public HashSet<PlayerServer> GetStatePlayers(string stateName)
        {
            if (string.IsNullOrEmpty(stateName)) return EmptyPlayerSet;

            var dic = StatePlayersDic;
            if (dic == null) return EmptyPlayerSet;

            if (dic.TryGetValue(stateName, out var cached))
            {
                return cached;
            }

            var players = GetPlayersAll;
            if (players == null) return EmptyPlayerSet;

            var result = new HashSet<PlayerServer>();
            foreach (var p in players)
            {
                if (p?.Public?.StateName == stateName)
                {
                    result.Add(p);
                }
            }

            dic[stateName] = result;
            return result;
        }

        public NameValidator NameValidator => _NameValidator;

        [NonSerialized]
        private NameValidator _NameValidator;

        public BaseContainer()
        {
            var publicChat = new Chat()
            {
                Id = 1,
                Name = "Public",
                OwnerLogin = "system",
                OwnerMaker = false,
                PartyLogin = new List<string>() { "system" },
                Posts = new List<ChatPost>(),
                LastChanged = DateTime.UtcNow,
            };

            MaxIdChat = 1; // Id = 1 закріплено за загальним чатом, 0 — системний приватний чат
            ChatManager.Instance.NewChatManager(1, publicChat);

            PlayersAll = new List<PlayerServer>()
            {
                new PlayerServer("system")
            };

            States = new List<State>();
            StatePositions = new List<StatePosition>();

            WorldObjects = new List<WorldObjectEntry>();
            WorldObjectsDeleted = new List<WorldObjectEntry>();
            Orders = new List<TradeOrder>();
            OrdersPlaceServerIdByTile = new Dictionary<int, long>();
            VersionNum = MainHelper.VersionNum;
            WorldObjectOnlineList = new List<WorldObjectOnline>();
            FactionOnlineList = new List<FactionOnline>();

            PostLoad();
        }

        public void PostLoad()
        {
            if (Orders == null) Orders = new List<TradeOrder>();
            if (OrdersPlaceServerIdByTile == null) OrdersPlaceServerIdByTile = new Dictionary<int, long>();
            if (UploadService == null) UploadService = new ConcurrentDictionary<long, string>();
            if (States == null) States = new List<State>();
            if (StatePositions == null) StatePositions = new List<StatePosition>();
            if (PlayersRanking == null) PlayersRanking = new List<string>();
            if (StatesRanking == null) StatesRanking = new List<string>();
            if (PlayersRankingLast == null) PlayersRankingLast = new List<string>();
            if (StatesRankingLast == null) StatesRankingLast = new List<string>();

            if (!ServerManager.ServerSettings.PlayerNeedApprove)
            {
                for (int i = 0; i < PlayersAll.Count; i++)
                {
                    PlayersAll[i].Approve = true;
                }
            }

            _OrderOperator = new ExchengeOperator(this);
            _StateOperator = new StateOperator(this);
            _NameValidator = new NameValidator(this);
            if (WorldObjectsDeleted == null) WorldObjectsDeleted = new List<WorldObjectEntry>();

            UpdatePlayersAllDic();
            UpdateStatesDic();

            // Якщо PVP вимкнено в конфігурації сервера — вимикаємо його у всіх гравців
            if (!ServerManager.ServerSettings.GeneralSettings.EnablePVP)
            {
                for (int i = 0; i < PlayersAll.Count; i++)
                {
                    var player = PlayersAll[i];
                    if (player?.Public != null)
                    {
                        player.Public.EnablePVP = false;
                    }
                }
            }
        }

        /// <summary>
        /// Потокобезпечна генерація унікального ServerId для об'єкта світу.
        /// </summary>
        public long GetWorldObjectEntryId()
        {
            return Interlocked.Increment(ref MaxServerIdWorldObjectEntry);
        }

        /// <summary>
        /// Потокобезпечна генерація унікального Id для нового гравця.
        /// </summary>
        public int GenerateMaxPlayerId()
        {
            return Interlocked.Increment(ref MaxPlayerId);
        }
    }
}