namespace Model
{
    /// <summary>
    /// Інтерфейс для моделей та пакетів, прив'язаних до конкретної точки або об'єкта глобальної карти
    /// </summary>
    public interface IModelPlace
    {
        /// <summary>
        /// Номер тайла глобальної карти світу
        /// </summary>
        int Tile { get; set; }

        /// <summary>
        /// Серверний ідентифікатор, що відповідає певному ігровому об'єкту карти (WorldObject)
        /// </summary>
        long PlaceServerId { get; set; }
    }
}
