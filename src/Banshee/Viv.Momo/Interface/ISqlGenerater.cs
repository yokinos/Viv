using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.Interface
{
    public interface ISqlGenerater
    {
        string CreateInsertSql(string tableName, object entity);

        string CreateUpdateSql(string tableName, object entity, string whereKeys);

        string CreateDeleteSql(string tableName, string whereKeys);

        string ToDatabaseValue(object value);

        string CreateInsertTemplateSql(string tableName, Type type);

        string GetFindSql(string tableName);

        string GetPageSql(string sql, int pageIndex, int pageSize, out string countSql);
    }
}
