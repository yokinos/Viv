using RabbitMQ.Client;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Models;
using Viv.Nana.Options;
using Viv.Nana.RabbitMQ;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Nana.Core
{
    public class NanaFactory
    {
        protected static NanaOptions NanaOptions = VivConfigRegistry.Get<NanaOptions>() ?? new NanaOptions();
        private static List<QueueModel> QueueList = new List<QueueModel>();


        public NanaFactory()
        {

        }

        protected QueueModel GetQueue<T>()
        {

        }

        public async Task<bool> RabbitMQPublishAsync<T>(VivMessage<T> message) where T : NanaMessage
        {
            var model = GetQueue<T>();
            if (model is null || message.Content is null) return false;

            var channel = await VivRabbitClient.GetInstance().GetChannelAsync(model);
            if (channel is null || channel.IsClosed) return false;

            var properties = message.Content.GetBasicProperties();
            if (properties is null)
            {
                properties = new BasicProperties()
                {

                };
            }

            await channel.BasicPublishAsync(model.Exchange, model.RoutingKey, model.IsMandatory, properties, message.ToBytes());
            return true;
        }
    }
}
