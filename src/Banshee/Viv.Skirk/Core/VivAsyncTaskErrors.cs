namespace Viv.Skirk.Core
{
    /// <summary>
    /// 识别唯一索引冲突。不直接引用 SqlClient / Npgsql，避免 Skirk 再挂一条数据库驱动。
    /// </summary>
    internal static class VivAsyncTaskErrors
    {
        public static bool IsUniqueViolation(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                switch (current.GetType().FullName)
                {
                    case "Npgsql.PostgresException":
                        if (current.GetType().GetProperty("SqlState")?.GetValue(current) is "23505")
                            return true;
                        break;

                    case "Microsoft.Data.SqlClient.SqlException":
                    case "System.Data.SqlClient.SqlException":
                        if (current.GetType().GetProperty("Number")?.GetValue(current) is int number
                            && number is 2601 or 2627)
                            return true;
                        break;
                }
            }

            return false;
        }
    }
}
