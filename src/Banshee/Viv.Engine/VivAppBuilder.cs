using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine.Options;

namespace Viv.Engine
{
    /// <summary>
    /// 用于在 控制台程序 winform wpf 桌面程序中用来启动 Viv的方式
    /// </summary>
    public sealed class VivAppBuilder
    {
        private readonly HostApplicationBuilder _builder;
        private readonly List<Action<ContainerBuilder>> _containerActions = [];
        private IHost? _host;
        private bool _built;

        private VivAppBuilder(string[]? args)
        {
            // 桌面程序从快捷方式启动时 cwd 是随机的，显式指向程序目录，否则 appsettings.json 找不到
            _builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });
        }

        /// <summary>
        /// 在 AddVivApp 之前追加自己的注册
        /// </summary>
        public IServiceCollection Services => _builder.Services;

        public IConfigurationManager Configuration => _builder.Configuration;

        /// <summary>
        /// AddVivApp 之后可取
        /// </summary>
        public VivOptions? Options { get; private set; }

        /// <summary>
        /// 宿主容器，StartAsync 之后可取。
        ///
        /// 窗体和控件是 new 出来的，构造注入够不着，只能回头找容器；要 Scoped 服务就自己开作用域。
        /// </summary>
        public IServiceProvider? Provider { get; private set; }

        public static VivAppBuilder Create(string[]? args = null) => new(args);

        /// <summary>
        /// 往 Autofac 容器里登记（模块 / 装饰器 / 泛型注册这些 MS DI 表达不了的）。
        /// 走 Services 能表达的就不用走这儿。
        ///
        /// 容器在 StartAsync 里 Build 那一刻才建起来，登记攒到那时回放，之后再来登记直接抛。
        /// </summary>
        public VivAppBuilder ConfigureContainer(Action<ContainerBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            if (_built)
            {
                throw new InvalidOperationException("容器已经建好了，现在登记不会生效。");
            }

            _containerActions.Add(configure);
            return this;
        }

        /// <summary>
        /// 加载配置、挂 Autofac 根容器、注册 Viv 全部组件
        /// </summary>
        public VivAppBuilder AddVivApp()
        {
            var vivOptions = _builder.AddVivConfig();
            Options = vivOptions;

            _builder.ConfigureContainer(new AutofacServiceProviderFactory(), container =>
            {
                // 容器是 AutofacServiceProviderFactory 自己 new 的，回调这一刻才拿到它，
                // 所以外部登记的（见 ConfigureContainer）攒在列表里、到这儿才回放
                foreach (var configure in _containerActions)
                {
                    configure(container);
                }

                container.VivAutofacRegister(vivOptions);
            });

            _builder.Services.AddViv(vivOptions);

            if (vivOptions.LogOption?.LogType == Log.LogType.Serilog)
            {
                _builder.Logging.ClearProviders();
                _builder.Logging.AddSerilog(dispose: false);
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return this;
        }

        /// <summary>
        /// Build → VivLocator.Initialize → 表结构同步 → 启动宿主，返回根 provider 供业务自建作用域
        /// </summary>
        public async Task<IServiceProvider> StartAsync(CancellationToken cancellationToken = default)
        {
            if (_host != null)
            {
                throw new InvalidOperationException("已经启动过，不能重复启动。");
            }

            _host = _builder.Build();
            _built = true;
            Provider = _host.Services;

            VivLocator.Initialize(_host.Services);
            VivStartupSchemaSync.Run(_host.Services);

            await _host.StartAsync(cancellationToken).ConfigureAwait(false);
            return _host.Services;
        }

        /// <summary>
        /// 停止宿主并释放容器
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken = default)
            => _host == null ? Task.CompletedTask : _host.StopAsync(cancellationToken);

        /// <summary>
        /// 写入租户上下文。值存在 AsyncLocal 上、随 ExecutionContext 向下流，
        /// 哪个线程调就只对那条线程的后续可见，所以登录成功后请在主线程调一次。
        /// </summary>
        public void SetVivContext(VivContextContent content)
        {
            ArgumentNullException.ThrowIfNull(content);
            GetAccessor().Current = content;
        }

        /// <summary>
        /// 清空租户上下文
        /// </summary>
        public void ClearVivContext() => GetAccessor().Current = null;

        private IVivContextAccessor GetAccessor()
        {
            if (_host == null)
            {
                throw new InvalidOperationException("请先调用 StartAsync。");
            }

            // 取 Singleton 的 accessor：Scoped 的 IVivContext 从根 provider 解析会在 ValidateScopes 下直接抛
            return _host.Services.GetRequiredService<IVivContextAccessor>();
        }
    }
}
