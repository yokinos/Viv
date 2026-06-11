using Microsoft.SqlServer.TransactSql.ScriptDom;
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.SqlServer
{
    /// <summary>
    /// MSSQL SQL 解析器。
    /// 只负责把 SQL 解析成 SelectStatement。
    /// </summary>
    public sealed class TSqlQueryParser
    {
        public SelectStatement ParseSelect(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL 不能为空。", nameof(sql));

            IList<ParseError> errors;

            var parser = new TSql160Parser(initialQuotedIdentifiers: true);

            TSqlFragment fragment;
            using (var reader = new StringReader(sql))
            {
                fragment = parser.Parse(reader, out errors);
            }

            if (errors != null && errors.Count > 0)
            {
                var message = string.Join(
                    Environment.NewLine,
                    errors.Select(e => $"Line {e.Line}, Column {e.Column}: {e.Message}"));

                throw new ArgumentException("SQL 解析失败：" + Environment.NewLine + message, nameof(sql));
            }

            if (fragment is not TSqlScript script)
                throw new ArgumentException("不是合法的 T-SQL 脚本。", nameof(sql));

            if (script.Batches.Count != 1)
                throw new ArgumentException("只支持单个 SQL Batch。", nameof(sql));

            var batch = script.Batches[0];

            if (batch.Statements.Count != 1)
                throw new ArgumentException("只支持单条 SQL 语句。", nameof(sql));

            if (batch.Statements[0] is not SelectStatement selectStatement)
                throw new ArgumentException("只支持 SELECT 语句。", nameof(sql));

            return selectStatement;
        }
    }
}
