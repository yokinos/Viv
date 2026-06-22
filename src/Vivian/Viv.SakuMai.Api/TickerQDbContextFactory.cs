using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TickerQ.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.DbContextFactory;

namespace Viv.SakuMai.Api
{
    public class TickerQDbContextFactory : IDesignTimeDbContextFactory<TickerQDbContext>
    {
        /// <summary>
        /// https://tickerq.net/docs/entity-framework/migrations
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public TickerQDbContext CreateDbContext(string[] args)
        {
            string conn = "server=43.228.79.205;user id=sa;password=viv_sqlserver_77;database=viv_tickerq_core;min pool size=4;max pool size=512;TrustServerCertificate=true;";
            var builder = new DbContextOptionsBuilder<TickerQDbContext>();
            builder.UseSqlServer(conn, o => o.MigrationsAssembly("Viv.SakuMai.Api"));
            return new TickerQDbContext(builder.Options);
        }
    }
}