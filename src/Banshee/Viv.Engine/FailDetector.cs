namespace Viv.Engine
{
    /// <summary>
    /// 「这次调用算成功还是失败」的唯一判定，事务回滚与本地事件分发共用。
    ///
    /// 有两个使用者 —— 工作单元拦截器（决定提交还是回滚）与本地事件分发过滤器（决定发还是整队丢弃）。
    /// 放在 Viv.Engine 根、与它判定的对象 <see cref="VivApiResult"/> 并排，是为了让两边引同一份实现，
    /// 而不是各抄一份 —— 判定一旦漂移，就会出现「数据回滚了、事件却发出去了」这类静默的不一致。
    ///
    /// 判定用 2xx 区间而非等于 <c>Success</c>：ApiResultCode 约定 2xx 全部代表成功类（如 Accepted = 201）。
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
