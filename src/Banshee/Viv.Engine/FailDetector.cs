namespace Viv.Engine
{
    /// <summary>
    /// 「这次调用算成功还是失败」的唯一判定 —— 事务回滚与本地事件分发共用同一套标准。
    ///
    /// 【为什么不放在任何一方的文件夹里】
    /// 它有两个使用者：工作单元拦截器（决定提交还是回滚）与本地事件分发过滤器（决定发还是整队丢弃）。
    /// 放进其中任何一方，另一方就够不着、只能自己再抄一份 —— 判定一旦漂移，会出现
    /// 「数据回滚了、事件却发出去了」，或者反过来「数据提交了、事件被丢弃」，
    /// 两种都是静默的、事后极难对齐的不一致。所以放在 <c>Viv.Engine</c> 根、
    /// 与它判定的对象 <see cref="VivApiResult"/> 并排。
    ///
    /// 【判定规则】用 2xx <b>区间</b>而非等于 <c>Success</c> —— ApiResultCode 约定
    /// 「2xx 正数区间全部代表成功类」（如 Accepted = 201）。
    /// </summary>
    internal static class FailDetector
    {
        /// <summary>业务是否失败。非 <see cref="VivApiResult"/> 的返回值（含 null）一律视为成功。</summary>
        public static bool IsFailed(object? result)
        {
            return result is VivApiResult apiResult
                && (apiResult.Code < 200 || apiResult.Code >= 300);
        }
    }
}
