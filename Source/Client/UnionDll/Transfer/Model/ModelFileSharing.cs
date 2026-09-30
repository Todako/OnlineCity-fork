using System;

namespace OCUnion.Transfer.Model
{
    public enum FileSharingCategory
    {
        PlayerIcon,
        ColonyScreen
    }

    [Serializable]
    public class ModelFileSharing
    {
        public FileSharingCategory Category { get; set; }
        public string Name { get; set; }
        public string Hash { get; set; }
        public byte[] Data { get; set; }
    }
}
