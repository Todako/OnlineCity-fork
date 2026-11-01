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
        public int State { get; set; }
        public int BattleSeed { get; set; }
        public long HostBattleTick { get; set; }
        public long AttackerBattleTick { get; set; }
        public bool HostReady { get; set; }
        public bool AttackerReady { get; set; }

        public bool TestMode { get; set; }

        public PlayerServer Attacker { get; set; }
        public PlayerServer Host { get; set; }

        public bool? VictoryAttacker { get; set; }

        public long HostPlaceServerId { get; set; }
        public long InitiatorPlaceServerId { get; set; }
        public int InitiatorPlaceTile { get; set; }

        public IntVec3S MapSize { get; set; }
        public byte[] MapSnapshotData { get; set; }
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

        // FIFO-черги команд замість словника, щоб жоден наказ одному й тому самому пішаку не перезаписувався
        public List<AttackPawnCommand> UpdateCommandQueue { get; set; }
        public List<AttackPawnCommand> HostCommandQueue { get; set; }
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
            UpdateCommandQueue = new List<AttackPawnCommand>(32);
            HostCommandQueue = new List<AttackPawnCommand>(32);
            NeedNewThingIDs = new HashSet<int>();

            CreateTime = DateTime.UtcNow;
            AttackUpdateTick = 0;
            HostBattleTick = 0;
            AttackerBattleTick = 0;
            HostReady = false;
            AttackerReady = false;

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
                    BattleSeed = fromClient.BattleSeed;
                    MapSize = fromClient.MapSize;
                    MapSnapshotData = fromClient.MapSnapshotData;
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
                    if (fromClient.CurrentBattleTick > 0) HostBattleTick = fromClient.CurrentBattleTick;
                    if (fromClient.HostReady) HostReady = true;

                    if (VictoryAttacker == null) VictoryAttacker = fromClient.VictoryAttacker;

                    bool hasNewEntities = (fromClient.NewPawnsId != null && fromClient.NewPawnsId.Count > 0)
                        || (fromClient.NewThingsId != null && fromClient.NewThingsId.Count > 0)
                        || (fromClient.NewCorpses != null && fromClient.NewCorpses.Count > 0)
                        || (fromClient.Delete != null && fromClient.Delete.Count > 0);

                    if (hasNewEntities)
                    {
                        if (fromClient.NewPawnsId != null)
                        {
                            for (int i = 0; i < fromClient.NewPawnsId.Count; i++)
                                Delete.Remove(fromClient.NewPawnsId[i]);
                        }
                        if (fromClient.NewThingsId != null)
                        {
                            for (int i = 0; i < fromClient.NewThingsId.Count; i++)
                                Delete.Remove(fromClient.NewThingsId[i]);
                        }
                        if (fromClient.NewCorpses != null)
                        {
                            for (int i = 0; i < fromClient.NewCorpses.Count; i++)
                                Delete.Remove(fromClient.NewCorpses[i].CorpseId);
                        }

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
                                if (found) NewCorpses.RemoveAt(i--);
                            }

                            for (int i = 0; i < fromClient.NewCorpses.Count; i++)
                                NewCorpses.Add(fromClient.NewCorpses[i]);
                        }

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
                                    NewCorpses.RemoveAt(i--);
                            }
                        }
                    }

                    if (fromClient.UpdateState != null && fromClient.UpdateState.Count > 0)
                    {
                        for (int i = 0; i < fromClient.UpdateState.Count; i++)
                        {
                            var stateItem = fromClient.UpdateState[i];
                            if (stateItem != null)
                                UpdateState[stateItem.HostThingID] = stateItem;
                        }
                    }

                    if (fromClient.HostCommands != null && fromClient.HostCommands.Count > 0)
                    {
                        for (int i = 0; i < fromClient.HostCommands.Count; i++)
                        {
                            var hCmd = fromClient.HostCommands[i];
                            if (hCmd != null)
                                HostCommandQueue.Add(hCmd);
                        }
                    }

                    var res = new AttackHostFromSrv
                    {
                        State = State,
                        AttackerBattleTick = AttackerBattleTick,
                        AttackerReady = AttackerReady,
                        UpdateCommand = UpdateCommandQueue.Count > 0 ? new List<AttackPawnCommand>(UpdateCommandQueue) : new List<AttackPawnCommand>(0),
                        NeedNewThingIDs = NeedNewThingIDs.Count > 0 ? new List<int>(NeedNewThingIDs) : new List<int>(0),
                        SetPauseOnTime = SetPauseOnTimeToHost ?? DateTime.MinValue,
                        VictoryHost = VictoryHostToHost,
                        TerribleFatalError = TerribleFatalError
                    };

                    UpdateCommandQueue.Clear();
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
                        BattleSeed = BattleSeed,
                        MapSize = MapSize,
                        MapSnapshotData = MapSnapshotData,
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
                    if (fromClient.CurrentBattleTick > 0) AttackerBattleTick = fromClient.CurrentBattleTick;
                    if (fromClient.AttackerReady) AttackerReady = true;

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
                        if (AttackUpdateTick > 10)
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
                                UpdateCommandQueue.Add(cmd);
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
                        BattleSeed = BattleSeed,
                        HostBattleTick = HostBattleTick,
                        HostReady = HostReady,
                        NewPawns = NewPawns.Count > 0 ? NewPawns : new List<ThingEntry>(0),
                        NewPawnsId = NewPawnsId.Count > 0 ? NewPawnsId : new List<int>(0),
                        NewThings = NewThings.Count > 0 ? NewThings : new List<ThingTrade>(0),
                        NewThingsId = NewThingsId.Count > 0 ? NewThingsId : new List<int>(0),
                        NewCorpses = NewCorpses.Count > 0 ? NewCorpses : new List<AttackCorpse>(0),
                        Delete = Delete.Count > 0 ? Delete : new List<int>(0),
                        UpdateState = UpdateState.Count > 0 ? new List<AttackThingState>(UpdateState.Values) : new List<AttackThingState>(0),
                        HostCommands = HostCommandQueue.Count > 0 ? new List<AttackPawnCommand>(HostCommandQueue) : new List<AttackPawnCommand>(0),
                        Finishing = VictoryAttacker != null,
                        VictoryAttacker = VictoryAttacker ?? false
                    };

                    if (NewPawns.Count > 0) NewPawns = new List<ThingEntry>();
                    if (NewPawnsId.Count > 0) NewPawnsId = new List<int>();
                    if (NewThings.Count > 0) NewThings = new List<ThingTrade>();
                    if (NewThingsId.Count > 0) NewThingsId = new List<int>();
                    if (NewCorpses.Count > 0) NewCorpses = new List<AttackCorpse>();
                    if (Delete.Count > 0) Delete = new List<int>();

                    UpdateState.Clear();
                    HostCommandQueue.Clear();

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