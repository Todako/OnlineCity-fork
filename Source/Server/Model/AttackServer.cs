using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;
using Transfer.ModelMails;

namespace ServerOnlineCity.Model
{
    public class AttackServer
    {
        /*
         * А0: 0 в 1 Атакуючий пересилає айді об'єктів
         * А1: Атакуючий посилає запит, поки статус не прийде 2
         * Н2: 1 в 2 Хосту приходить сигнал про атаку і він запитує айді 
         * А2: Посилає атакуючих пішаків
         * А3: Посилає запит, поки статус не прийде більше 3. Відповідь зі статусом 4 містить дані для створення карти
         * Н4: 2 в 4 Посилає дані для створення карти
         * Н5: 4 в 5 Зчитує атакуючих пішаків, повторює, поки не прийдуть дані пішаків
        */
        public int State { get; set; }

        public bool TestMode { get; set; }

        public PlayerServer Attacker { get; set; }

        public PlayerServer Host { get; set; }

        /// <summary>
        /// Ознака перемоги. Обчислюється і приймається від Хоста.
        /// </summary>
        public bool? VictoryAttacker { get; set; }

        public long HostPlaceServerId { get; set; }

        public long InitiatorPlaceServerId { get; set; }
        public int InitiatorPlaceTile { get; set; }

        public IntVec3S MapSize { get; set; }
        public List<ThingEntry> Pawns { get; set; }
        public List<IntVec3S> TerrainDefNameCell { get; set; }
        public List<string> TerrainDefName { get; set; }
        public List<ThingTrade> Thing { get; set; }
        public List<IntVec3S> ThingCell { get; set; }

        public List<ThingEntry> NewPawns { get; set; }
        public List<int> NewPawnsId { get; set; }
        public List<ThingTrade> NewThings { get; set; }
        public List<int> NewThingsId { get; set; }
        public List<AttackCorpse> NewCorpses { get; set; }
        public List<int> Delete { get; set; }
        public Dictionary<int, AttackThingState> UpdateState { get; set; }
        public Dictionary<int, AttackPawnCommand> UpdateCommand { get; set; }
        public HashSet<int> NeedNewThingIDs { get; set; }

        public DateTime? SetPauseOnTimeToHost { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime CreateTime { get; set; }
        public bool VictoryHostToHost { get; set; }
        public bool TerribleFatalError { get; set; }
        public long AttackUpdateTick { get; set; }

        private readonly object SyncObj = new object();

        public string New(PlayerServer player, PlayerServer hostPlayer, AttackInitiatorToSrv fromClient, bool testMode)
        {
            if (!ServerManager.ServerSettings.GeneralSettings.EnablePVP) return "PVP online disable on this server";

            string playerLogin = player?.Public?.Login ?? "-";
            string hostLogin = hostPlayer?.Public?.Login ?? "-";

            if (player == null || hostPlayer == null || !player.Online || !hostPlayer.Online)
            {
                Loger.Log($"Server AttackServer {playerLogin} -> {hostLogin} canceled: Attack not possible: player offline");
                return "Attack not possible: player offline";
            }

            var err = AttackUtils.CheckPossibilityAttack(player, hostPlayer, fromClient.InitiatorPlaceServerId, fromClient.HostPlaceServerId,
                ServerManager.ServerSettings.ProtectingNovice);

            if (err != null)
            {
                Loger.Log($"Server AttackServer {playerLogin} -> {hostLogin} canceled: {err}");
                return err;
            }

            TestMode = testMode;
            Attacker = player;
            Host = hostPlayer;
            Attacker.AttackData = this;
            Host.AttackData = this;
            HostPlaceServerId = fromClient.HostPlaceServerId;
            InitiatorPlaceServerId = fromClient.InitiatorPlaceServerId;

            var data = Repository.GetData;
            if (data?.WorldObjects != null)
            {
                lock (data)
                {
                    for (int i = 0; i < data.WorldObjects.Count; i++)
                    {
                        var wo = data.WorldObjects[i];
                        if (wo != null && wo.PlaceServerId == InitiatorPlaceServerId)
                        {
                            InitiatorPlaceTile = wo.Tile;
                            break;
                        }
                    }
                }
            }

            NewPawns = new List<ThingEntry>();
            NewPawnsId = new List<int>();
            NewThings = new List<ThingTrade>();
            NewThingsId = new List<int>();
            NewCorpses = new List<AttackCorpse>();
            Delete = new List<int>();
            UpdateState = new Dictionary<int, AttackThingState>();
            UpdateCommand = new Dictionary<int, AttackPawnCommand>();
            NeedNewThingIDs = new HashSet<int>();

            CreateTime = DateTime.UtcNow;
            AttackUpdateTick = 0;

            if (!TestMode && Host.Public != null)
            {
                Host.Public.LastPVPTime = DateTime.UtcNow;
            }

            Loger.Log($"Server AttackServer {Attacker.Public.Login} -> {Host.Public.Login} New");
            return null;
        }

        public AttackHostFromSrv RequestHost(AttackHostToSrv fromClient)
        {
            if (fromClient == null) return new AttackHostFromSrv { State = State, ErrorText = "No request" };

            lock (SyncObj)
            {
                // Перші 5-8 хвилин не перевіряємо на відключення, оскільки створення карти може тривати довго
                if ((fromClient.State == 10 || (DateTime.UtcNow - CreateTime).TotalSeconds > 8 * 60)
                    && CheckConnect(false))
                {
                    return new AttackHostFromSrv { State = State };
                }

                if (fromClient.State < State)
                {
                    return new AttackHostFromSrv
                    {
                        ErrorText = "Unexpected request " + fromClient.State + ". Was expected " + State
                    };
                }

                if (fromClient.State == 2)
                {
                    State = 2;
                    return new AttackHostFromSrv
                    {
                        State = State,
                        HostPlaceServerId = HostPlaceServerId,
                        InitiatorPlaceServerId = InitiatorPlaceServerId,
                        StartInitiatorPlayer = Attacker?.Public?.Login,
                        TestMode = TestMode
                    };
                }

                if (fromClient.State == 4)
                {
                    MapSize = fromClient.MapSize;
                    TerrainDefNameCell = fromClient.TerrainDefNameCell;
                    TerrainDefName = fromClient.TerrainDefName;
                    Thing = fromClient.Thing;
                    ThingCell = fromClient.ThingCell;
                    State = 4;
                    return new AttackHostFromSrv { State = State };
                }

                if (fromClient.State == 5)
                {
                    State = 5;
                    return new AttackHostFromSrv
                    {
                        State = State,
                        Pawns = Pawns
                    };
                }

                if (fromClient.State == 10)
                {
                    State = 10;

                    if (VictoryAttacker == null) VictoryAttacker = fromClient.VictoryAttacker;

                    bool hasNewEntities = (fromClient.NewPawnsId != null && fromClient.NewPawnsId.Count > 0)
                        || (fromClient.NewThingsId != null && fromClient.NewThingsId.Count > 0)
                        || (fromClient.NewCorpses != null && fromClient.NewCorpses.Count > 0)
                        || (fromClient.Delete != null && fromClient.Delete.Count > 0);

                    if (hasNewEntities)
                    {
                        // Видаляємо зі списку Delete, якщо надходить команда додати цей ID
                        if (fromClient.NewPawnsId != null)
                        {
                            for (int i = 0; i < fromClient.NewPawnsId.Count; i++)
                            {
                                Delete.Remove(fromClient.NewPawnsId[i]);
                            }
                        }
                        if (fromClient.NewThingsId != null)
                        {
                            for (int i = 0; i < fromClient.NewThingsId.Count; i++)
                            {
                                Delete.Remove(fromClient.NewThingsId[i]);
                            }
                        }
                        if (fromClient.NewCorpses != null)
                        {
                            for (int i = 0; i < fromClient.NewCorpses.Count; i++)
                            {
                                Delete.Remove(fromClient.NewCorpses[i].CorpseId);
                            }
                        }

                        // Об'єднуємо нових пішаків
                        if (fromClient.NewPawnsId != null && fromClient.NewPawns != null)
                        {
                            for (int i = 0; i < fromClient.NewPawnsId.Count; i++)
                            {
                                int id = fromClient.NewPawnsId[i];
                                if (NewPawnsId.Contains(id)) continue;
                                NewPawnsId.Add(id);
                                if (i < fromClient.NewPawns.Count) NewPawns.Add(fromClient.NewPawns[i]);
                            }
                        }

                        // Об'єднуємо нові предмети
                        if (fromClient.NewThingsId != null && fromClient.NewThings != null)
                        {
                            for (int i = 0; i < fromClient.NewThingsId.Count; i++)
                            {
                                int id = fromClient.NewThingsId[i];
                                if (NewThingsId.Contains(id)) continue;
                                NewThingsId.Add(id);
                                if (i < fromClient.NewThings.Count) NewThings.Add(fromClient.NewThings[i]);
                            }
                        }

                        // Об'єднуємо трупи без виділення лямбда-делегатів LINQ
                        if (fromClient.NewCorpses != null)
                        {
                            for (int i = 0; i < NewCorpses.Count; i++)
                            {
                                var existing = NewCorpses[i];
                                bool found = false;
                                for (int j = 0; j < fromClient.NewCorpses.Count; j++)
                                {
                                    var incoming = fromClient.NewCorpses[j];
                                    if (incoming != null && (incoming.PawnId == existing.PawnId || incoming.CorpseId == existing.CorpseId))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if (found)
                                {
                                    NewCorpses.RemoveAt(i--);
                                }
                            }

                            for (int i = 0; i < fromClient.NewCorpses.Count; i++)
                            {
                                NewCorpses.Add(fromClient.NewCorpses[i]);
                            }
                        }

                        // Об'єднуємо видалення
                        if (fromClient.Delete != null)
                        {
                            for (int i = 0; i < fromClient.Delete.Count; i++)
                            {
                                int id = fromClient.Delete[i];
                                if (!Delete.Contains(id)) Delete.Add(id);
                            }
                        }

                        if (fromClient.NewCorpses != null)
                        {
                            for (int i = 0; i < fromClient.NewCorpses.Count; i++)
                            {
                                int pawnId = fromClient.NewCorpses[i].PawnId;
                                if (!Delete.Contains(pawnId)) Delete.Add(pawnId);
                            }
                        }

                        // Запобіжне коригування: прибираємо з доданих ті, що стоять у черзі на видалення
                        for (int d = 0; d < Delete.Count; d++)
                        {
                            int n = Delete[d];
                            int index = NewPawnsId.IndexOf(n);
                            if (index >= 0)
                            {
                                NewPawnsId.RemoveAt(index);
                                if (index < NewPawns.Count) NewPawns.RemoveAt(index);
                            }

                            index = NewThingsId.IndexOf(n);
                            if (index >= 0)
                            {
                                NewThingsId.RemoveAt(index);
                                if (index < NewThings.Count) NewThings.RemoveAt(index);
                            }

                            for (int i = 0; i < NewCorpses.Count; i++)
                            {
                                if (n == NewCorpses[i].CorpseId)
                                {
                                    NewCorpses.RemoveAt(i--);
                                }
                            }
                        }
                    }

                    if (fromClient.UpdateState != null && fromClient.UpdateState.Count > 0)
                    {
                        for (int i = 0; i < fromClient.UpdateState.Count; i++)
                        {
                            var stateItem = fromClient.UpdateState[i];
                            if (stateItem != null)
                            {
                                UpdateState[stateItem.HostThingID] = stateItem;
                            }
                        }
                    }

                    var res = new AttackHostFromSrv
                    {
                        State = State,
                        UpdateCommand = UpdateCommand.Count > 0 ? new List<AttackPawnCommand>(UpdateCommand.Values) : new List<AttackPawnCommand>(0),
                        NeedNewThingIDs = NeedNewThingIDs.Count > 0 ? new List<int>(NeedNewThingIDs) : new List<int>(0),
                        SetPauseOnTime = SetPauseOnTimeToHost ?? DateTime.MinValue,
                        VictoryHost = VictoryHostToHost,
                        TerribleFatalError = TerribleFatalError
                    };

                    // ОПТИМІЗАЦІЯ: замість перестворення нових словників очищаємо наявні (0 байт GC)
                    UpdateCommand.Clear();
                    NeedNewThingIDs.Clear();

                    if (SetPauseOnTimeToHost != null)
                    {
                        Loger.Log("Server Send SetPauseOnTimeToHost=" + SetPauseOnTimeToHost.Value.ToGoodUtcString());
                    }

                    if (Host?.Public != null) Host.PVPHostLastTime = DateTime.UtcNow;
                    SetPauseOnTimeToHost = null;
                    VictoryHostToHost = false;
                    return res;
                }

                return new AttackHostFromSrv
                {
                    ErrorText = "Unexpected request " + fromClient.State + "! Was expected " + State
                };
            }
        }

        public AttackInitiatorFromSrv RequestInitiator(AttackInitiatorToSrv fromClient)
        {
            if (fromClient == null) return new AttackInitiatorFromSrv { State = State, ErrorText = "No request" };

            lock (SyncObj)
            {
                if ((fromClient.State == 10 || (DateTime.UtcNow - CreateTime).TotalSeconds > 8 * 60)
                    && CheckConnect(true))
                {
                    return new AttackInitiatorFromSrv { State = State };
                }

                if (fromClient.State == 0 && fromClient.StartHostPlayer != null)
                {
                    State = 1;
                    TestMode = fromClient.TestMode;
                    return new AttackInitiatorFromSrv { State = State };
                }

                if (fromClient.State == 1)
                {
                    return new AttackInitiatorFromSrv { State = State };
                }

                if (fromClient.State == 2 && State >= 2 && fromClient.Pawns != null && fromClient.Pawns.Count > 0)
                {
                    Pawns = fromClient.Pawns;
                    SetPauseOnTimeToHost = DateTime.UtcNow.AddMinutes(8);
                    Loger.Log("Server Set 1 SetPauseOnTimeToHost=" + SetPauseOnTimeToHost.Value.ToGoodUtcString());

                    return new AttackInitiatorFromSrv
                    {
                        State = State,
                        TestMode = TestMode
                    };
                }

                if (fromClient.State == 3 && State >= 3)
                {
                    return new AttackInitiatorFromSrv
                    {
                        State = State,
                        MapSize = MapSize,
                        TerrainDefNameCell = TerrainDefNameCell,
                        TerrainDefName = TerrainDefName,
                        Thing = Thing,
                        ThingCell = ThingCell
                    };
                }

                if (fromClient.State == 10 && State < 10)
                {
                    return new AttackInitiatorFromSrv { State = State };
                }

                if (fromClient.State == 10)
                {
                    AttackUpdateTick++;

                    if (StartTime == DateTime.MinValue)
                    {
                        Loger.Log($"Server AttackServer {Attacker?.Public?.Login} -> {Host?.Public?.Login} Start");
                        StartTime = DateTime.UtcNow;
                    }

                    if (fromClient.VictoryHostToHost)
                    {
                        VictoryHostToHost = fromClient.VictoryHostToHost;
                    }

                    if (fromClient.TerribleFatalError)
                    {
                        Loger.Log($"Server AttackServer TerribleFatalError {AttackUpdateTick}");
                        if (AttackUpdateTick != 2)
                        {
                            VictoryHostToHost = true;
                        }
                        else
                        {
                            TerribleFatalError = fromClient.TerribleFatalError;
                            SendAttackCancel();
                            Finish();
                        }
                    }

                    if (fromClient.SetPauseOnTimeToHost != TimeSpan.MinValue)
                    {
                        SetPauseOnTimeToHost = DateTime.UtcNow + fromClient.SetPauseOnTimeToHost;
                        Loger.Log("Server Set 2 SetPauseOnTimeToHost=" + SetPauseOnTimeToHost.Value.ToGoodUtcString());
                    }

                    if (fromClient.UpdateCommand != null && fromClient.UpdateCommand.Count > 0)
                    {
                        for (int i = 0; i < fromClient.UpdateCommand.Count; i++)
                        {
                            var cmd = fromClient.UpdateCommand[i];
                            if (cmd != null)
                            {
                                UpdateCommand[cmd.HostPawnID] = cmd;
                            }
                        }
                    }

                    if (fromClient.NeedNewThingIDs != null && fromClient.NeedNewThingIDs.Count > 0)
                    {
                        for (int i = 0; i < fromClient.NeedNewThingIDs.Count; i++)
                        {
                            NeedNewThingIDs.Add(fromClient.NeedNewThingIDs[i]);
                        }
                    }

                    var res = new AttackInitiatorFromSrv
                    {
                        State = State,
                        NewPawns = NewPawns.Count > 0 ? NewPawns : new List<ThingEntry>(0),
                        NewPawnsId = NewPawnsId.Count > 0 ? NewPawnsId : new List<int>(0),
                        NewThings = NewThings.Count > 0 ? NewThings : new List<ThingTrade>(0),
                        NewThingsId = NewThingsId.Count > 0 ? NewThingsId : new List<int>(0),
                        NewCorpses = NewCorpses.Count > 0 ? NewCorpses : new List<AttackCorpse>(0),
                        Delete = Delete.Count > 0 ? Delete : new List<int>(0),
                        UpdateState = UpdateState.Count > 0 ? new List<AttackThingState>(UpdateState.Values) : new List<AttackThingState>(0),
                        Finishing = VictoryAttacker != null,
                        VictoryAttacker = VictoryAttacker ?? false
                    };

                    // ОПТИМІЗАЦІЯ: створюємо нові списки тільки якщо передані непорожні дані
                    if (NewPawns.Count > 0) NewPawns = new List<ThingEntry>();
                    if (NewPawnsId.Count > 0) NewPawnsId = new List<int>();
                    if (NewThings.Count > 0) NewThings = new List<ThingTrade>();
                    if (NewThingsId.Count > 0) NewThingsId = new List<int>();
                    if (NewCorpses.Count > 0) NewCorpses = new List<AttackCorpse>();
                    if (Delete.Count > 0) Delete = new List<int>();

                    UpdateState.Clear();

                    if (VictoryAttacker != null) Finish();

                    return res;
                }

                return new AttackInitiatorFromSrv
                {
                    ErrorText = "Unexpected request " + fromClient.State + "! Was expected " + State
                };
            }
        }

        private static void AddMail(PlayerServer player, ModelMail mail)
        {
            if (player?.Mails == null || mail == null) return;
            lock (player.Mails)
            {
                player.Mails.Add(mail);
            }
        }

        private void SendAttackCancel()
        {
            var systemPlayer = Repository.GetData?.PlayerSystem?.Public;

            AddMail(Host, new ModelMailAttackCancel
            {
                From = systemPlayer,
                To = Host?.Public
            });

            AddMail(Attacker, new ModelMailAttackCancel
            {
                From = systemPlayer,
                To = Attacker?.Public
            });
        }

        /// <summary>
        /// Перевірка та дії при збоях зв'язку під час активного бою.
        /// </summary>
        private bool CheckConnect(bool attacker)
        {
            if (!TestMode && Host?.Public != null)
            {
                Host.Public.LastPVPTime = DateTime.UtcNow;
            }

            bool fail = false;
            bool asTestMode = TestMode;
            string logDet;

            if (attacker)
            {
                logDet = " (is attacker)";
                fail = Host == null || !Host.Online;
            }
            else
            {
                logDet = " (is host)";
                if (StartTime != DateTime.MinValue
                    || (SetPauseOnTimeToHost.HasValue && SetPauseOnTimeToHost.Value != DateTime.MinValue && SetPauseOnTimeToHost.Value < DateTime.UtcNow))
                {
                    fail = Attacker == null || !Attacker.Online;
                }
            }

            if (!fail && State < 10 && (DateTime.UtcNow - CreateTime).TotalSeconds > 8 * 60)
            {
                fail = true;
                asTestMode = true;
                logDet = " Loading too long.";
            }

            if (fail)
            {
                var systemPlayer = Repository.GetData?.PlayerSystem?.Public;

                if (asTestMode)
                {
                    Loger.Log("Server AttackServer Fail and back." + (TestMode ? " TestMode" : "") + logDet);
                    SendAttackCancel();
                }
                else if (!attacker)
                {
                    // Відключився атакуючий
                    if (StartTime == DateTime.MinValue || (DateTime.UtcNow - StartTime).TotalSeconds < 60)
                    {
                        Loger.Log("Server AttackServer Fail attacker off in start" + logDet);
                        SendAttackCancel();
                    }
                    else
                    {
                        Loger.Log("Server AttackServer Fail attacker off in progress" + logDet);

                        AddMail(Host, new ModelMailAttackTechnicalVictory
                        {
                            From = systemPlayer,
                            To = Host?.Public
                        });

                        AddMail(Attacker, new ModelMailAttackCancel
                        {
                            From = systemPlayer,
                            To = Attacker?.Public
                        });

                        AddMail(Attacker, new ModelMailDeleteWO
                        {
                            From = systemPlayer,
                            To = Attacker?.Public,
                            PlaceServerId = InitiatorPlaceServerId,
                            Tile = InitiatorPlaceTile
                        });
                    }
                }
                else
                {
                    // Відключився хост
                    AddMail(Host, new ModelMailAttackCancel
                    {
                        From = systemPlayer,
                        To = Host?.Public
                    });

                    AddMail(Host, new ModelMailDeleteWO
                    {
                        From = systemPlayer,
                        To = Host?.Public,
                        PlaceServerId = HostPlaceServerId
                    });

                    if (StartTime == DateTime.MinValue)
                    {
                        Loger.Log("Server AttackServer Fail host off in start" + logDet);
                        AddMail(Attacker, new ModelMailAttackCancel
                        {
                            From = systemPlayer,
                            To = Attacker?.Public
                        });
                    }
                    else
                    {
                        Loger.Log("Server AttackServer Fail host off in progress" + logDet);
                        AddMail(Attacker, new ModelMailAttackTechnicalVictory
                        {
                            From = systemPlayer,
                            To = Attacker?.Public
                        });
                    }
                }
                Finish();
            }

            return fail;
        }

        public void Finish()
        {
            Loger.Log($"Server AttackServer {Attacker?.Public?.Login ?? "-"} -> {Host?.Public?.Login ?? "-"} Finish StartTime sec = "
                + (StartTime == DateTime.MinValue ? "-" : (DateTime.UtcNow - StartTime).TotalSeconds.ToString())
                + (VictoryAttacker == null ? "" : VictoryAttacker.Value ? " VictoryAttacker" : " VictoryHost")
                + (TestMode ? " TestMode" : "")
                + (TerribleFatalError ? " TerribleFatalError" : ""));

            if (Attacker != null) Attacker.AttackData = null;
            if (Host != null) Host.AttackData = null;
        }
    }
}