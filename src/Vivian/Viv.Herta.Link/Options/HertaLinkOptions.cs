namespace Viv.Herta.Link.Options
{
    public class HertaLinkOptions
    {
        public string HubPath { get; set; } = "/chat";

        public string? RedisConnectionString { get; set; }

        public bool EnableDetailedErrors { get; set; }
    }
}
