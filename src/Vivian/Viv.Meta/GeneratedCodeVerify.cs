namespace Viv.Meta
{
    /// <summary>
    /// 编译期验证：<see cref="SampleModel"/> 必须实现 <see cref="IVivGenerated"/>（由生成器注入）。
    /// 若 Viv.Generators 未生效，此处隐式转换直接编译失败——端到端验证生成管线。
    /// </summary>
    public static class GeneratedCodeVerify
    {
        public static string Marker()
        {
            IVivGenerated generated = new SampleModel();
            return generated.VivGeneratedMarker;
        }
    }
}
