using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Options
{
    public class OpenAIOptions
    {
        public string ApiUrl { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;
    }
}
