using Microsoft.SqlServer.TransactSql.ScriptDom;
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.SqlServer
{
    internal static class MssqlSqlScriptGenerator
    {
        public static string Generate(TSqlFragment fragment)
        {
            var options = new SqlScriptGeneratorOptions
            {
                KeywordCasing = KeywordCasing.Uppercase,
                IncludeSemicolons = false,

                NewLineBeforeFromClause = true,
                NewLineBeforeWhereClause = true,
                NewLineBeforeGroupByClause = true,
                NewLineBeforeHavingClause = true,
                NewLineBeforeOrderByClause = true
            };

            var generator = new Sql160ScriptGenerator(options);
            generator.GenerateScript(fragment, out var sql);

            return sql.Trim().TrimEnd(';').Trim();
        }
    }
}
