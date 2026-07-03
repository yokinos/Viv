using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Options;

namespace Viv.Contracts.Interface
{
    public interface IAiClientFactory
    {
        IChatClient GetDefaultClient();

        IChatClient CreateClient(string apiUrl, string apiKey, string model);

        IChatClient CreateClient(OpenAIOptions option) => CreateClient(option.ApiUrl, option.ApiKey, option.Model);
    }
}
