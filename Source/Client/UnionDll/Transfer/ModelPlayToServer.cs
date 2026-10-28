using Model;
using System;
using System.Collections.Generic;

namespace Model
{
    /// <summary>
    /// Модель готової ділянки дороги між двома сусідніми клітинками планети (Roads of the Rim).
    /// </summary>
    [Serializable]
    public class WorldRoadOnline
    {
        public int FromTile { get; set; }
        public int ToTile { get; set; }
        public string RoadDefName { get; set; }
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// Модель активного або запланованого будівельного майданчика дороги (RoadConstructionSite + RoadConstructionLeg).
    /// </summary>
    [Serializable]
    public class WorldRoadSiteOnline
    {
        public string OwnerLogin { get; set; }
        public int LocalSiteId { get; set; }
        public int Tile { get; set; }
        public string RoadDefName { get; set; }
        public List<int> LegTiles { get; set; }
        public string CompProgressData { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime UpdateTime { get; set; }
    }
}

namespace Transfer
{
    [Serializable]
    public class ModelPlayToServer
    {
        public DateTime UpdateTime { get; set; }
        public List<WorldObjectEntry> WObjects { get; set; }
        public List<WorldObjectEntry> WObjectsToDelete { get; set; }
        public byte[] SaveFileData { get; set; }
        public bool SingleSave { get; set; }
        public long LastTick { get; set; }
        public PlayerGameProgress GameProgress { get; set; }
        public List<string> GetPlayersInfo { get; set; }
        public List<WorldObjectOnline> WObjectOnlineList { get; set; }
        public List<WorldObjectOnline> WObjectOnlineToAdd { get; set; }
        public List<WorldObjectOnline> WObjectOnlineToDelete { get; set; }

        public List<FactionOnline> FactionOnlineList { get; set; }
        public List<FactionOnline> FactionOnlineToAdd { get; set; }
        public List<FactionOnline> FactionOnlineToDelete { get; set; }

        public List<WorldRoadOnline> RoadsToAdd { get; set; }
        public List<WorldRoadSiteOnline> RoadSitesToSync { get; set; }
    }
}