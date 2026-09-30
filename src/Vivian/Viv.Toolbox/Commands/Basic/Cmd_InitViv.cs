using Spectre.Console.Cli;
using Viv.Cli;
using Viv.Momo;

namespace Viv.Toolbox.Commands.Basic
{
    /// <summary>
    /// 按实体同步表结构：建缺失的表、加缺失的列，不改不删。
    /// </summary>
    [VivCommand("initviv", "初始化Viv相关表结构")]
    public class Cmd_InitViv : AsyncCommand
    {
        private readonly IMomoDbContext _dbContext;

        public Cmd_InitViv(IMomoDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            try
            {
                await _dbContext.SyncTableAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Out.PrintlnError($"表结构同步失败：{ex.Message}");
                return 1;
            }

            Out.PrintlnSuccess("表结构同步完成");
            return 0;
        }
    }
}
