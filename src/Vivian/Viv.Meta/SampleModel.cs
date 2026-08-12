namespace Viv.Meta
{
    /// <summary>
    /// 样例类型：标注 <see cref="VivModelAttribute"/> 后，生成器会注入 partial 段
    /// 使其实现 <see cref="IVivGenerated"/>。仅用于端到端验证生成管线。
    /// </summary>
    [VivModel("sample")]
    public partial class SampleModel
    {
    }
}
