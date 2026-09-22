using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.Interface
{
    /// <summary>
    /// 软删除标记接口
    /// <list type="bullet">
    /// <item><description>实现该接口的实体自动启用EF全局查询过滤器：LINQ查询、EF的Find，以及框架按主键拼SQL的 Find&lt;T&gt;(id) 都会默认过滤 IsDeleted = true 的数据，业务无需重复写 !x.IsDeleted 判断。</description></item>
    /// <item><description>如需查询已删除数据，可通过 IDataFilter.Disable&lt;SoftDeletedFilter&gt;() 临时关闭过滤，作用域结束自动恢复。</description></item>
    /// <item><description>手写原生SQL（PageAsync / FindList&lt;T&gt;(sql)等重载）不受全局过滤器控制，不会自动追加软删除条件。</description></item>
    /// </list>
    /// </summary>
    public interface ISoftDeleted
    {
        /// <summary>
        /// 是否已软删除
        /// </summary>
        bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间，软删除时赋值
        /// </summary>
        DateTime? DeletedAt { get; set; }
    }
}
