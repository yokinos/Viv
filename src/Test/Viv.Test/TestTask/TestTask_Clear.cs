
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Viv.Test.Common;
using Viv.Test.Core;

namespace Viv.Test.TestTask
{
    [CommandSet(Command.Clear)]
    public class TestTask_Clear : ITestTask
    {
        public async Task StartAsync()
        {
            Console.Clear();
            Console.WriteLine(ConsoleContext.FirstMessage);
            await Task.CompletedTask;
        }
    }
}
