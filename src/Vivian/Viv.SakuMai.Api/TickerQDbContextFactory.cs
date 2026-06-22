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
            string conn = "";
            var builder = new DbContextOptionsBuilder<TickerQDbContext>();
            builder.UseSqlServer(conn, o => o.MigrationsAssembly("Viv.SakuMai.Api"));
            return new TickerQDbContext(builder.Options);
        }
    }
}