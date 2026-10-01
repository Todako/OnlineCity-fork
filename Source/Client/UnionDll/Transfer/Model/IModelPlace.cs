namespace Model
{

    public interface IModelPlace
    {
        int Tile { get; set; }

        /// <summary>
        /// Id із сервера, що відповідає певному ігровому об'єкту WorldObject.
        /// </summary>
        long PlaceServerId { get; set; }
    }
}
