using Model;
using System;
using System.Collections.Generic;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Повідомлення від каравану іншого гравця.
    /// </summary>
    [Serializable]
    public class ModelMailTrade : ModelMail, IModelPlace
    {
        public int Tile { get; set; }
        public long PlaceServerId { get; set; }
        public List<ThingEntry> Things { get; set; }

        public override string GetHash()
        {
            return $"T{Tile}P{PlaceServerId} " + ContentString();
        }

        public override string ContentString()
        {
            return Things == null ? "" : Things.ToStringLabel();
        }
    }

}
