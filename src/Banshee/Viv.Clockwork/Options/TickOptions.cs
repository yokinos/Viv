using Viv.Clockwork.Enums;
using Viv.Delusion.Magic;

namespace Viv.Clockwork.Options
{
    public class TickOptions
    {
        public VivSchedulerType SchedulerType { get; set; } = VivSchedulerType.TickerQ;

        public TickerQOptions? TickerQ { get; set; }
    }
}
