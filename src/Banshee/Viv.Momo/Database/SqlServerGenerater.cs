using Azure;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Database
{
    public class SqlServerGenerater : ISqlGenerater
    {
        public string CreateDeleteSql(string tableName, string whereKeys)
        {
            throw new NotImplementedException();
        }

        public string CreateInsertSql(string tableName, object entity)
        {
            throw new NotImplementedException();
        }

        public string CreateInsertTemplateSql(string tableName, Type type)
        {
            throw new NotImplementedException();
        }

        public string CreateUpdateSql(string tableName, object entity, string whereKeys)
        {
            throw new NotImplementedException();
        }

        public string GetFindSql(string tableName)
        {
            return $"SELECT * FROM [{tableName}] WHERE Id = @Id";
        }

        public string GetPageSql(string sql, int pageIndex, int pageSize, out string countSql)
        {
            countSql = $"SELECT COUNT(*) FROM ({sql}) xx";
            return $"{sql} OFFSET {(pageIndex - 1) * pageSize} ROWS FETCH NEXT {pageSize} ROWS ONLY";
        }

        public string ToDatabaseValue(object value)
        {
            throw new NotImplementedException();
        }
    }
}
