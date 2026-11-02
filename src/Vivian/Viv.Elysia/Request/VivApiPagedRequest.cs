using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Delusion.Extension;
using Viv.Elysia.Interface;

namespace Viv.Elysia.Request
{
    public abstract class VivApiPagedRequest : VivApiRequest, IApiPagedRequest
    {
        /// <summary>
        /// 当前页码
        /// </summary>
        [DisplayName("当前页码")]
        [Range(1, double.MaxValue)]
        public int PageIndex { get; set; } = 1;

        /// <summary>
        /// 每页条数
        /// </summary>
        [DisplayName("每页条数")]
        [Range(1, 10000)]
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 获取分页查询sql及请求参数
        /// </summary>
        /// <returns></returns>
        public abstract (string sql, object parameters) GetSqlQuery();

        public override string Validate()
        {
            if (!PageIndex.Between(1, int.MaxValue) || !PageSize.Between(1, 2000))
            {
                return "分页参数错误,pageIndex不得小于1,pageSize不得小于1且不得大于2000";
            }

            return base.Validate();
        }
    }
}
