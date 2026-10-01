using OCUnion;
using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;

namespace Model
{
    [Serializable]
    public class Player
    {
        public string Login { get; set; }

        /// <summary>
        /// int faster then string ;-)
        /// </summary>
        public int Id { get; set; }

        public long Version { get; set; }

        public string ServerName { get; set; }

        //public bool ExistMap { get; set; }

        public DateTime LastSaveTime { get; set; }

        public DateTime LastOnlineTime { get; set; }

        public DateTime LastPVPTime { get; set; }

        public long LastTick { get; set; }

        public bool EnablePVP { get; set; }

        public string DiscordUserName { get; set; }

        public string EMail { get; set; }

        public string AboutMyText { get; set; }

        public Grants Grants { get; set; }

        public bool ExistsEnemyPawns { get; set; }

        /// <summary>
        /// Держава. Посилання на State.Name.
        /// </summary>
        public string StateName { get; set; }

        /// <summary>
        /// Посада в державі. Посилання на StatePosition.Name.
        /// </summary>
        public string StatePositionName { get; set; }

    }

    [Serializable]
    public class PlayerGameProgress
    {
        public int ColonistsCount { get; set; }
        public int ColonistsDownCount { get; set; }
        public int ColonistsBleedCount { get; set; }
        public int ColonistsNeedingTend { get; set; }
        public int AnimalObedienceCount { get; set; }
        /// <summary>
        /// Скільки пішаків мають 8 із 12 навичок 20-го рівня. Імовірно, завжди має дорівнювати 0. Якщо значення дорівнює ColonistsCount, це чит.
        /// </summary>
        public int PawnMaxSkill { get; set; }
        public int KillsHumanlikes { get; set; }
        public int KillsMechanoids { get; set; }
        public string KillsBestHumanlikesPawnName { get; set; }
        public string KillsBestMechanoidsPawnName { get; set; }
        public int KillsBestHumanlikes { get; set; }
        public int KillsBestMechanoids { get; set; }
        public List<PawnStat> Pawns { get; set; }
        public string TransLog { get; set; }
        public bool ExistsEnemyPawns { get; set; }
    }

}
