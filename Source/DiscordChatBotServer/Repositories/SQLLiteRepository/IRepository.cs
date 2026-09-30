using System.Collections.Generic;

namespace OC.DiscordBotServer.Repositories
{
    public interface IRepository<TEntity>
        where TEntity : class
    {
        bool AddNewItem(TEntity entity);
        IReadOnlyList<TEntity> GetAll();
        void Delete(IEnumerable<TEntity> entityes);
    }
}
