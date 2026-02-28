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
    }
}
