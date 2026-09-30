using Model;

namespace RimWorldOnlineCity
{
    public abstract class WorldObjectBaseOnline : WorldObject
    {
        public abstract IModelPlace Place { get; }

        public abstract string ExpandingIconName { get; }
    }
}
