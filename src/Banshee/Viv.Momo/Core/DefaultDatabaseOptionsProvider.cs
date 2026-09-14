using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Momo.Core
{
    /// <summary>
    /// <see cref="IDatabaseOptionsProvider"/> 的默认实现。
    /// 通过 <see cref="IOptions{TOptions}"/> 获取 <see cref="DatabaseOptions"/> 配置，
    /// 并在构造时缓存其实例，后续调用 <see cref="GetRealOptions"/> 直接返回该实例。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="IOptions{TOptions}"/> 而非 <see cref="IOptionsMonitor{TOptions}"/>，
    /// 因此不支持配置热更新：实例在构造时确定，之后不会随配置变更而改变。
    /// 若需要热更新，应改用 <see cref="IOptionsMonitor{TOptions}"/> 并在每次获取时读取 CurrentValue。
    /// </remarks>
    public class DefaultDatabaseOptionsProvider : IDatabaseOptionsProvider
    {
        /// <summary>
        /// 构造时缓存的数据库配置实例。
        /// </summary>
        protected readonly DatabaseOptions _databaseOptions;

        /// <summary>
        /// 初始化 <see cref="DefaultDatabaseOptionsProvider"/> 的新实例。
        /// </summary>
        /// <param name="options">数据库配置选项，由 DI 容器注入，不能为 null。</param>
        /// <exception cref="ArgumentNullException">
        /// 当 <paramref name="options"/> 为 null 时抛出。
        /// </exception>
        public DefaultDatabaseOptionsProvider(IOptions<DatabaseOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _databaseOptions = options.Value;
        }

        /// <summary>
        /// 获取当前生效的数据库配置实例。
        /// </summary>
        /// <returns>构造时缓存的 <see cref="DatabaseOptions"/> 实例，不会为 null。</returns>
        public DatabaseOptions GetRealOptions()
        {
            return _databaseOptions;
        }
    }
}