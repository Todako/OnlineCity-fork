using System;

namespace OCUnion.Transfer
{
    /// <summary>
    /// Correctly disconnect reason
    /// </summary>
    [Serializable]
    public enum DisconnectReason : byte
    {
        ///
        /// Усе гаразд, продовжуємо працювати.
        /// 
        AllGood,
        /// <summary>
        /// Close game 
        /// </summary>
        CloseConnection,
        /// <summary>
        /// Connection Time Out
        /// </summary>
        ConnectionTimeOut,
        /// <summary>
        ///  
        /// </summary>
        FilesMods,
    }
}
