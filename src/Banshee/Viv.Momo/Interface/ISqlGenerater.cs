using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.Interface
{
    public interface ISqlGenerater
    {
        string CreateInsertSql(string tableName, object entity, string ignoreKeys = "");

        string CreateUpdateSql(string tableName, object entity, string whereKeys, string ignoreKeys = "");

        string CreateDeleteSql(string tableName, object entity);

        string ToDatabaseValue(object value);
    }
}
