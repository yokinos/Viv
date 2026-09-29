using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Cli;
using Viv.Momo;

namespace Viv.Toolbox.Commands.Basic
{
    /// <summary>
    /// 用来初始化Viv相关表 及基础数据
    /// </summary>
    [VivCommand("initapex", "用来初始化Apex相关表 及基础数据")]
    public class Cmd_InitApex : AsyncCommand
    {
        private readonly IMomoDbContext _dbContext;

        public Cmd_InitApex(IMomoDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        protected async override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {

            return 0;
        }
    }
}
