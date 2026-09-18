using System;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Лист-сповіщення про скасування онлайн-атаки
    /// </summary>
    [Serializable]
    public class ModelMailAttackCancel : ModelMail
    {
        public override string GetHash() => "NotContent";
    }
}
