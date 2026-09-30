namespace Model
{

    public interface IModelPlace
    {
        int Tile { get; set; }

        /// <summary>
        /// Id с сервера, соответствующий определенному игровому объекту WorldObject
        /// </summary>
        long PlaceServerId { get; set; }
    }
}
