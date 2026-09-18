using Model;
using OCUnion;
using System;
using System.Collections.Generic;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Лист-запит на запуск інциденту (рейд, караван, природне лихо тощо)
    /// </summary>
    [Serializable]
    public class ModelMailStartIncident : ModelMail, IModelPlace
    {
        /// <summary>
        /// Номер тайла карти, де запускається інцидент
        /// </summary>
        public int Tile { get; set; }

        /// <summary>
        /// Серверний ідентифікатор об'єкта світу
        /// </summary>
        public long PlaceServerId { get; set; }

        /// <summary>
        /// Тип інциденту
        /// </summary>
        public IncidentTypes IncidentType { get; set; }

        /// <summary>
        /// Множник сили/складності інциденту
        /// </summary>
        public int IncidentMult { get; set; }

        /// <summary>
        /// Додаткові параметри інциденту (фракція, спосіб прибуття тощо)
        /// </summary>
        public List<string> IncidentParams { get; set; }

        /// <summary>
        /// Прапорець: тільки для перегляду в інтерфейсі тих подій, які вже перебувають у черзі
        /// </summary>
        public bool AlreadyStart { get; set; }

        /// <summary>
        /// Формування унікального хешу листа
        /// </summary>
        public override string GetHash()
        {
            var paramsStr = (IncidentParams != null && IncidentParams.Count > 0)
                ? string.Join(" ", IncidentParams)
                : string.Empty;

            return $"T{Tile}P{PlaceServerId} {(int)IncidentType} {IncidentMult} {paramsStr}";
        }
    }
}
