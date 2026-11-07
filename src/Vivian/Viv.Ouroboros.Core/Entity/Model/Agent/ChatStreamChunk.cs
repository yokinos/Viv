namespace Viv.Ouroboros.Core.Entity.Model.Agent
{
    /// <summary>
    /// 流式输出的一段：要么是正文增量，要么是一次失败。
    /// 失败不能混进正文——否则客户端会把「会话不存在」当成模型说的话拼进对话气泡，
    /// 而且 HTTP 状态仍是 200，调用方没有任何办法区分。
    /// </summary>
    /// <param name="Text">正文增量；失败时为 null</param>
    /// <param name="Error">失败文案；正常时为 null</param>
    public sealed record ChatStreamChunk(string? Text, string? Error)
    {
        /// <summary>
        /// 这一段是不是失败
        /// </summary>
        public bool IsError => Error is not null;

        /// <summary>
        /// 正文增量
        /// </summary>
        public static ChatStreamChunk Delta(string text) => new(text, null);

        /// <summary>
        /// 一次失败
        /// </summary>
        public static ChatStreamChunk Fail(string error) => new(null, error);
    }
}
