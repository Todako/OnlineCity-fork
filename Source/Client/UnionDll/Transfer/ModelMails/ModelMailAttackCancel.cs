using System;

namespace Transfer.ModelMails
{

    [Serializable]
    public class ModelMailAttackCancel : ModelMail
    {
        public override string GetHash()
        {
            return "NotContent";
        }
    }
}
