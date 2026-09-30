using Model;
using System;

namespace Transfer.ModelMails
{

    [Serializable]
    public class ModelMailDeleteWO : ModelMail, IModelPlace
    {
        public int Tile { get; set; }
        public long PlaceServerId { get; set; }

        public override string GetHash()
        {
            return $"T{Tile}P{PlaceServerId}";
        }
    }
}
