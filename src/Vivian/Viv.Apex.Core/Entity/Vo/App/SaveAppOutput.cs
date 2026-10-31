namespace Viv.Apex.Core.Entity.Vo.App
{
    /// <summary>保存应用的结果。密钥只在新增时出现一次，列表接口不会再带。</summary>
    public class SaveAppOutput
    {
        /// <summary>应用 Id。新增时是刚生成的 Id。</summary>
        public long Id { get; set; }

        /// <summary>应用密钥。只在新增时返回一次，更新时为空。</summary>
        public string? AppSecret { get; set; }
    }
}
