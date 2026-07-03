using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Options
{
    public class OpenAIOptions
    {
        public string ApiUrl { get; set; }

        public string ApiKey { get; set; }

        public string Model { get; set; }
    }
}
