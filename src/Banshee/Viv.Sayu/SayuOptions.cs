using Viv.Vva.Magic;

namespace Viv.Sayu
{
    public class SayuOptions
    {
        public VivSchedulerType SchedulerType { get; set; } = VivSchedulerType.TickerQ;

        public TickerQOptions? TickerQ { get; set; }

        /// <summary>
        /// TickerQ 定时任务类型扫描配置（不配则不注册任何任务）
        /// </summary>
        public List<FilterTypeOptions> TaskTypes { get; set; } = [];
    }
}
