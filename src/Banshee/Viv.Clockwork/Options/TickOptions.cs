using Viv.Clockwork.Enums;
using Viv.Delusion.Magic;

namespace Viv.Clockwork.Options
{
    public class TickOptions
    {
        /// <summary>
        /// 总开关。<c>false</c> = 定时调度不装，TickerQ 的宿主与看板都不注册。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        public VivSchedulerType SchedulerType { get; set; } = VivSchedulerType.TickerQ;

        public TickerQOptions? TickerQ { get; set; }
    }
}
