using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Viv.Momo.Core
{
    /// <summary>
    /// 模型缓存键：把「实体扫描配置」也算进去。
    ///
    /// EF 默认只按上下文类型缓存模型（一个 <see cref="EFAppContext"/> 类型一份），
    /// 而 <see cref="EFAppContext.OnModelCreating"/> 是按实例的 <c>DatabaseOptions.EntityTypeOptions</c> 建模型的。
    /// 两者不一致时，先建模型的那个上下文（比如初始化流程里连 master 库、配置里没有实体扫描的那个）
    /// 会把它的模型缓存下来，后面所有上下文都拿到同一份 —— 空配置的话就是空模型，
    /// 插入实体时报「The entity type 'X' was not found」。
    ///
    /// 实测就是这个形状：全流程 OnModelCreating 只跑一次（master、filters=0），
    /// 后面 viv_apex_master 的上下文直接复用了那份空模型。
    /// </summary>
    internal sealed class MomoModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is EFAppContext app
                ? (context.GetType(), app.ModelCacheKey, designTime)
                : (object)(context.GetType(), designTime);
    }
}
