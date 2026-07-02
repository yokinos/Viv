using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Generic;
using Viv.Momo.Interface;

namespace Viv.Elysia.Interface
{
    public interface ICRUD
    {
        Task<T> GetAsync<T>(long id) where T : IEntity;

        Task<T> GetAsync<T>(Func<T, bool> predicate) where T : IEntity;

        Task<List<T>> GetListAsync<T>(Func<T, bool> predicate) where T : IEntity;

        Task<bool> InsertAsync<T>(T entity) where T : IEntity;

        Task<bool> InsertAsync<T>(List<T> entities) where T : IEntity;

        Task<bool> UpdateAsync<T>(T entity) where T : IEntity;

        Task<bool> DeleteAsync<T>(long id) where T : IEntity;

        Task<bool> DeleteAsync<T>(Func<T, bool> predicate) where T : IEntity;

        Task<bool> SoftDeleteAsync<T>(long id) where T : IEntity, ISoftDelete;

        Task<bool> SoftDeleteAsync<T>(Func<T, bool> predicate) where T : IEntity, ISoftDelete;

        Task<PagedList<T>> GetPageAsync<T>(IPageRequest request) where T : IEntity;
    }
}
