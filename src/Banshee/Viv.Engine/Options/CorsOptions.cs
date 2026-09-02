namespace Viv.Engine.Options
{
    /// <summary>
    /// CORS 允许的 Origin。未配置时：Development 允许本机回环；其他环境不允许跨域。
    /// 条目支持 <c>https://*.example.com</c> 形式的子域通配。
    /// </summary>
    public class CorsOptions
    {
        public string[]? Origins { get; set; }
    }
}
