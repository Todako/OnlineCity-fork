using System;

namespace Model
{
    /// <summary>
    /// Розширений інтерфейс інформації про гравця, його статус та розрахунок вартості активів
    /// </summary>
    public interface IPlayerEx
    {
        /// <summary>
        /// Публічні базові дані гравця (логін, фракція тощо)
        /// </summary>
        Player Public { get; }

        /// <summary>
        /// Ознака перебування гравця в мережі (онлайн)
        /// </summary>
        bool Online { get; }

        /// <summary>
        /// Інтервал між PvP-атаками у хвилинах
        /// </summary>
        int MinutesIntervalBetweenPVP { get; }

        /// <summary>
        /// Розрахунок поточної вартості об'єктів світу гравця
        /// </summary>
        /// <param name="serverId">Ідентифікатор конкретного сервера/поселення (0 — для всіх об'єктів)</param>
        WorldObjectsValues CostWorldObjects(long serverId = 0);
    }

    /// <summary>
    /// Контейнер показників ринкової вартості та кількості об'єктів гравця у світі
    /// </summary>
    [Serializable]
    public class WorldObjectsValues
    {
        /// <summary>
        /// Ринкова вартість будівель та території
        /// </summary>
        public float MarketValue;

        /// <summary>
        /// Ринкова вартість колоністів, рабів і тварин
        /// </summary>
        public float MarketValuePawn;

        /// <summary>
        /// Ринкова вартість речей на складах та в інвентарях
        /// </summary>
        public float MarketValueStorage;

        /// <summary>
        /// Ринкова вартість безготівкового балансу/срібла на рахунку
        /// </summary>
        public float MarketValueBalance;

        /// <summary>
        /// Сумарна ринкова вартість усіх активів гравця
        /// </summary>
        public float MarketValueTotal => MarketValue + MarketValuePawn + MarketValueStorage + MarketValueBalance;

        /// <summary>
        /// Кількість підконтрольних баз/поселень
        /// </summary>
        public int BaseCount;

        /// <summary>
        /// Кількість споряджених караванів у дорозі
        /// </summary>
        public int CaravanCount;

        /// <summary>
        /// Скорочений текстовий опис розподілу вартості
        /// </summary>
        public string Details;

        /// <summary>
        /// Розширений деталізований опис вартості майна
        /// </summary>
        public string DetailsExtended;

        /// <summary>
        /// Рядковий список ідентифікаторів серверних баз гравця
        /// </summary>
        public string BaseServerIds;
    }
}
