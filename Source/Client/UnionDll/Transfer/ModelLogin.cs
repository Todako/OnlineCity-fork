using System;

namespace Transfer
{
    [Serializable]
    public class ModelLogin
    {
        public string Login { get; set; }
        public string Pass { get; set; }
        public string Email { get; set; }
        public string DiscordUserName { get; set; }
        public string KeyReconnect { get; set; }
        public long Version { get; set; }
    }
}
