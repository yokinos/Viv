using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Interface
{
    public interface IEventPublisher
    {
        Task<bool> PublishAsync<TEvent>(TEvent @event) where TEvent : class;


    }
}
