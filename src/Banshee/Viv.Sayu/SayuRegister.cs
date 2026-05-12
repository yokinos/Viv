using Viv.Vva;
using Viv.Vva.Extension;
using Viv.Vva.Magic;

namespace Viv.Sayu
{
    public static class SayuRegister
    {
        public static void Initialize(SayuOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            VivConfigRegistry.Add(options);
        }

        public static void AddVivTickerQTasks(List<FilterTypeOptions> taskTypes)
        {
            if (taskTypes.IsNullOrEmpty()) return;

            // TODO: 用 TypeScanMagic 扫描后通过 TickerQ 的 MapTicker 注册
            // 目前 TickerQ 10.3.0 的 MapTicker 需要泛型参数，待确定最佳映射方式
        }
    }
}
