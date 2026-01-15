using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Viv.Test.Core;
using Viv.Vva.Magic;

namespace Viv.Test.Common
{
    public class ConsoleContext
    {
        public const string FirstMessage = "Viv Test\r\n1:所有命令均不区分大小写!\r\n2:输入Exit即可停止运行!输入Help可获取全部指令信息!\r\n☆--------☆--------☆--------☆--------☆--------☆";
        private static List<CommandModel> _assemblys;

        public static List<CommandModel> GetCmdAssemblys()
        {
            if (_assemblys == null)
            {
                _assemblys = new List<CommandModel>();
                var assembly = Assembly.GetExecutingAssembly();
                if (assembly != null)
                {
                    var types = assembly.GetTypes();
                    var tests = types.Where(x => x.GetInterface(typeof(ITestTask).Name) != null);
                    if (tests != null && tests.Any())
                    {
                        foreach (var type in tests)
                        {
                            var attribute = ReflectionMagic.GetAttribute<CommandSetAttribute>(type);
                            if (attribute != null)
                            {
                                attribute.Assembly.SetTypeInfo(type);
                                _assemblys.Add(attribute.Assembly);
                            }
                        }
                    }
                }
            }

            return _assemblys;
        }
    }
}
