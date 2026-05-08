using Microsoft.Extensions.Hosting;
using Viv.Contracts;
using Viv.Vva;
using Viv.Vva.Magic;

namespace Viv.Nana.Core
{
    public class NanaConsumerHostedService : BackgroundService
    {
        private readonly List<FilterTypeOptions> _consumerTypes;

        public NanaConsumerHostedService(List<FilterTypeOptions> consumerTypes)
        {
            _consumerTypes = consumerTypes;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await NanaRegister.InitConsumerAsync(_consumerTypes);
        }
    }
}