namespace OCUnion.Transfer
{
    public enum ServerInfoType : byte
    {
        Full = 1,
        Short = 2,
        SendSave = 3,
        /// <summary>
        /// Повна інформація з докладним текстовим описом.
        /// </summary>
        FullWithDescription
    }
}