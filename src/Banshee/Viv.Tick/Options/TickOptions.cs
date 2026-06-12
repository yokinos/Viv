using Viv.Delusion.Magic;
using Viv.Tick.Enums;

namespace Viv.Tick.Options
{
    public class TickOptions
    {
        public VivSchedulerType SchedulerType { get; set; } = VivSchedulerType.TickerQ;

        public TickerQOptions? TickerQ { get; set; }

        public List<FilterTypeOptions> TaskTypes { get; set; } = [];
    }
}
