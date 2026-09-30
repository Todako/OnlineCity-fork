using System;

namespace Transfer.ModelMails
{

    [Serializable]
    public class ModelMailAttackTechnicalVictory : ModelMail
    {
        public override string GetHash()
        {
            return "NotContent";
        }
    }
}
