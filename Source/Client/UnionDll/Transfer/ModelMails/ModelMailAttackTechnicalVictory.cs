using System;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Лист-сповіщення про зарахування технічної перемоги в онлайн-атаці (наприклад, при дисконекті захисника/атакуючого)
    /// </summary>
    [Serializable]
    public class ModelMailAttackTechnicalVictory : ModelMail
    {
        public override string GetHash() => "NotContent";
    }
}
