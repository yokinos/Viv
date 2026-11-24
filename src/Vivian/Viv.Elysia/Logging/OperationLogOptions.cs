namespace Viv.Elysia.Logging
{
    /// <summary>
    /// 操作日志的采集与记录策略。<b>凡是"记不记"的判断都放这里</b>，不要放回特性上 ——
    /// 特性描述的是"这个接口是什么操作"，改不了"部署环境要不要记失败"。
    ///
    /// <para>通过 <c>AddVivOperationLog(o =&gt; ...)</c> 配置。注意该回调是<b>立即执行</b>的
    /// （不是 MVC 那种延迟 Configure）：<see cref="UseEventPublisher"/> 要在注册期决定注册哪个
    /// 出口实现，晚于此就读不到了。</para>
    /// </summary>
    public sealed class OperationLogOptions
    {
        /// <summary>
        /// 总开关。false 时过滤器直接放行，一条都不记（保留采集开销也一并省掉）。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否记录失败的请求，默认 <c>true</c>。
        ///
        /// <para>这条默认值和旧实现相反（旧的是"只记业务码 200"），是刻意的：审计最需要留痕的恰恰是
        /// 「谁试图删了不该删的东西」「谁的权限被别人改掉了」。只记成功，等于把事故现场全部丢掉。</para>
        /// </summary>
        public bool RecordFailure { get; set; } = true;

        /// <summary>
        /// 是否采集请求参数，默认 <c>true</c>。取模型绑定后的 <c>ActionArguments</c>，
        /// 结构化、字段名与 DTO 一致，落库前统一脱敏。
        /// </summary>
        public bool IncludeRequest { get; set; } = true;

        /// <summary>
        /// 是否采集响应数据，默认 <c>false</c>。两个理由：<b>体积</b>（列表/导出接口能到几 MB）
        /// 与<b>敏感度</b>（登录响应里就是 AccessToken / RefreshToken）。
        /// 确实需要时打开，脱敏会同样生效，但仍然建议只对个别接口开。
        /// </summary>
        public bool IncludeResponse { get; set; }

        /// <summary>
        /// 请求/响应 JSON 的字符上限，默认 8 KB，超出截断并标注。
        /// 没有这一刀，一个批量导入接口能把整包数据塞进消息队列。
        /// </summary>
        public int MaxPayloadChars { get; set; } = 8 * 1024;

        /// <summary>
        /// 是否注册默认的「发 MQ 事件」出口，默认 <c>true</c>。
        ///
        /// <para>宿主自己实现了 <see cref="IOperationLogWriter"/> 并且只想落库时，把它设成
        /// <c>false</c>：否则配了消息队列的服务会两边都写一份。没配消息队列时该出口只会记一条
        /// Warning 然后返回，不会让请求失败。</para>
        /// </summary>
        public bool UseEventPublisher { get; set; } = true;
    }
}
