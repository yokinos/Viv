using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Database;
using Viv.Momo.Enums;
using Viv.Momo.Interface;

namespace Viv.Momo.Core
{
    public class SqlGeneraterFactory
    {
        private static readonly Dictionary<DatabaseSouceType, ISqlGenerater> _generaters = [];

        /// <summary>
        /// 获取sql生成器
        /// </summary>
        /// <param name="databaseSouce"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static ISqlGenerater GetSqlGenerater(DatabaseSouceType databaseSouce)
        {
            if (_generaters.TryGetValue(databaseSouce, out var generater))
            {
                return generater;
            }

            ISqlGenerater newGenerater = databaseSouce switch
            {
                DatabaseSouceType.PostgreSQL => new PostgreSqlGenerater(),
                DatabaseSouceType.SqlServer => new SqlServerGenerater(),
                _ => throw new ArgumentException("Invalid database source type"),
            };

            _generaters[databaseSouce] = newGenerater;
            return newGenerater;
        }

    }
}
