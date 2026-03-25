using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Core;
using Viv.Nana.Enums;
using Viv.Nana.Options;
using Viv.Nana.RabbitMq;
using Viv.Vva;
using Viv.Vva.Extension;
using Viv.Vva.Magic;

namespace Viv.Nana
{
    public static class NanaRegister
    {
        public static void Initialize(NanaOptions options)
        {
            var copy = options.DeepCopy();
            ArgumentNullException.ThrowIfNull(copy);
            VivConfigRegistry.Add(copy);

            if ((options.MainQueueType == MessageQueueType.RabbitMQ || options.SecondaryQueueType == MessageQueueType.RabbitMQ))
            {
                ArgumentNullException.ThrowIfNull(options.RabbitMqOptions);
                RabbitMQFactory.ValidateOptions(options.RabbitMqOptions);
            }
        }

        public static async Task InitConsumerAsync(List<FilterTypeOptions> options)
        {
            if (options.IsNullOrEmpty()) return;

            var typeList = TypeScanMagic.ScanRange(options);
            if (typeList.IsNullOrEmpty()) return;

            foreach (var type in typeList)
            {
                var instance = Activator.CreateInstance(type);
                if (instance is IVivConsumer consumer)
                {
                    await consumer.SubscribeAsync();
                }
            }
        }
    }
}
