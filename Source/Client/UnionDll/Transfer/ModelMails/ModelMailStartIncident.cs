using Model;
using OCUnion;
using System;
using System.Collections.Generic;

namespace Transfer.ModelMails
{

    [Serializable]
    public class ModelMailStartIncident : ModelMail, IModelPlace
    {
        public int Tile { get; set; }
        public long PlaceServerId { get; set; }

        public IncidentTypes IncidentType { get; set; }
        public int IncidentMult { get; set; }
        public List<string> IncidentParams { get; set; }
        /// <summary>
        /// Лише для перегляду в інтерфейсі тих, хто вже в черзі.
        /// </summary>
        public bool AlreadyStart { get; set; }

        public override string GetHash()
        {
            return $"T{Tile}P{PlaceServerId} {(int)IncidentType} {IncidentMult} "
                + IncidentParams == null ? "" : string.Join(" ", IncidentParams);
        }
    }
}
