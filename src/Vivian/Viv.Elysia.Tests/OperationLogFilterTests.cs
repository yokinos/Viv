using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Models;
using Viv.Elysia.Attributes;
using Viv.Elysia.Filter;
using Viv.Elysia.Logging;
using Viv.Engine;
using Viv.Entity.Enums;
using Viv.Fakes;
using Viv.Log;

namespace Viv.Elysia.Tests
{
    /// <summary>
    /// 操作日志采集器的行为契约。
    ///
    /// 这里钉的几件事，恰好是旧实现出过错的地方：请求参数能不能采到（旧的在过滤器里重读流，生产恒空）、
    /// 失败要不要记（旧只记 200）、出口炸了会不会把成功的请求变成 500（旧会）。
    /// </summary>
    public class OperationLogFilterTests
    {
        private sealed class SampleController
        {
            [OperationLog(EmOperationType.Add, Name = "新增用户")]
            public void WithAttr() { }

            public void NoAttr() { }
        }

        private sealed class LoginBody
        {
            public string? UserName { get; set; }

            public string? Password { get; set; }
        }

        /// <summary>记录并可控抛异常的出口替身</summary>
        private sealed class RecordingWriter : IOperationLogWriter
        {
            public List<OperationLogRecord> Records { get; } = [];

            /// <summary>设了就在写入时抛 —— 验"一个出口炸了不影响其它出口、也不影响请求"</summary>
            public Exception? ThrowOnWrite { get; set; }

            public ValueTask WriteAsync(OperationLogRecord record, CancellationToken cancellationToken = default)
            {
                if (ThrowOnWrite is not null)
                {
                    throw ThrowOnWrite;
                }

                Records.Add(record);
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>只解析日志器的最小容器 —— 过滤器兜底告警与出口都从这里取</summary>
        private sealed class LoggerOnlyProvider(ILoggerContract logger) : IServiceProvider
        {
            public object? GetService(Type serviceType) => serviceType == typeof(ILoggerContract) ? logger : null;
        }

        private static ControllerActionDescriptor DescriptorFor(string methodName) => new()
        {
            // 分组取的就是这个：UserController → user
            ControllerName = "User",
            MethodInfo = typeof(SampleController).GetMethod(methodName)!,
        };

        private static OperationLogFilter CreateFilter(
            IEnumerable<IOperationLogWriter> writers,
            out TestContext vivContext,
            OperationLogOptions? options = null)
        {
            var context = new TestContext
            {
                Snapshot = new VivContextContent { AppId = 3, SubjectId = 7, UserId = 42, TraceId = "trace-1" },
            };
            vivContext = context;

            // 过滤器从容器里取策略，模拟 AddVivOperationLog 注册的那一份
            var services = new ServiceCollection()
                .AddSingleton(options ?? new OperationLogOptions())
                .BuildServiceProvider();

            return new OperationLogFilter(writers, context, services);
        }

        private static ActionExecutingContext CreateContext(
            ActionDescriptor descriptor,
            HttpContext http,
            IDictionary<string, object?>? arguments = null)
        {
            var actionContext = new ActionContext(http, new RouteData(), descriptor);
            return new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                arguments ?? new Dictionary<string, object?>(),
                controller: null!);
        }

        private static ActionExecutionDelegate Next(HttpContext http, VivApiResult? result, Action? inAction = null) => () =>
        {
            inAction?.Invoke();
            // .NET 10：ActionExecutedContext 3 参构造的第三参是 controller，Result 必须走属性 setter
            var executed = new ActionExecutedContext(
                new ActionContext(http, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                controller: null!)
            {
                Result = result,
            };

            return Task.FromResult(executed);
        };

        /// <summary>请求上下文 + 一个能解析出 <see cref="ILoggerContract"/> 的容器</summary>
        private static (DefaultHttpContext http, RecordingLogger logger) CreateHttp()
        {
            var logger = new RecordingLogger();
            var http = new DefaultHttpContext { RequestServices = new LoggerOnlyProvider(logger) };
            http.Request.Path = "/api/user/add";
            http.Request.Headers.UserAgent = "xunit";
            http.Connection.RemoteIpAddress = IPAddress.Loopback;

            return (http, logger);
        }

        private static OperationLogScope CreateScope(HttpContext http)
            => new(new HttpContextAccessor { HttpContext = http }, http.RequestServices);

        [Fact]
        public async Task 有标注_成功_写入一条_正文取特性Name()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, VivApiResult.Success("成功了")));

            var record = Assert.Single(writer.Records);
            Assert.Equal(EmOperationType.Add, record.Operation);
            Assert.Equal("新增用户", record.Name);
            // 正文取 Name，不取接口返回的 Message
            Assert.Equal("新增用户", record.Description);
            Assert.Equal("user", record.Module);
            Assert.True(record.Succeeded);
            Assert.Equal(200, record.ResultCode);
            Assert.Equal(42, record.UserId);
            Assert.Equal(7, record.SubjectId);
            Assert.Equal(3, record.AppId);
            Assert.Equal("trace-1", record.TraceId);
            Assert.Equal("127.0.0.1", record.Ip);
            Assert.Equal("xunit", record.UserAgent);
            Assert.Equal("/api/user/add", record.Path);
        }

        [Fact]
        public async Task 有标注_失败_默认也要记()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, new VivApiResult((int)ApiResultCode.Error, "没有权限")));

            var record = Assert.Single(writer.Records);
            Assert.False(record.Succeeded);
            Assert.Equal(-200, record.ResultCode);
        }

        [Fact]
        public async Task 关掉RecordFailure_失败不记()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _, new OperationLogOptions { RecordFailure = false });
            var (http, _) = CreateHttp();

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, new VivApiResult((int)ApiResultCode.Error, "没有权限")));

            Assert.Empty(writer.Records);
        }

        [Fact]
        public async Task 未标注_也没碰过Scope_不记()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.NoAttr)), http),
                Next(http, VivApiResult.Success("ok")));

            Assert.Empty(writer.Records);
        }

        [Fact]
        public async Task 未标注_但业务Target了_要记_且没有操作类型()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();
            var scope = CreateScope(http);

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.NoAttr)), http),
                Next(http, VivApiResult.Success("ok"), () => scope.Target("Order", 1001)));

            var record = Assert.Single(writer.Records);
            Assert.Null(record.Operation);
            Assert.Equal("Order", record.TargetType);
            Assert.Equal("1001", record.TargetId);
        }

        [Fact]
        public async Task Skip_即使标了特性也不记()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();
            var scope = CreateScope(http);

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, VivApiResult.Success("ok"), scope.Skip));

            Assert.Empty(writer.Records);
        }

        [Fact]
        public async Task Describe_覆盖特性Name()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();
            var scope = CreateScope(http);

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, VivApiResult.Success("ok"), () => scope.Describe("把张三的角色从 A 调整为 B")));

            var record = Assert.Single(writer.Records);
            Assert.Equal("把张三的角色从 A 调整为 B", record.Description);
            Assert.Equal("新增用户", record.Name);
        }

        [Fact]
        public async Task 请求参数走ActionArguments_且已脱敏()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();

            var arguments = new Dictionary<string, object?>
            {
                ["request"] = new LoginBody { UserName = "bob", Password = "P@ssw0rd!" },
            };

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http, arguments),
                Next(http, VivApiResult.Success("登录成功")));

            var record = Assert.Single(writer.Records);
            Assert.NotNull(record.RequestJson);
            Assert.Contains("bob", record.RequestJson);
            Assert.DoesNotContain("P@ssw0rd!", record.RequestJson);
        }

        [Fact]
        public async Task 单个参数序列化失败_不影响其它参数()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();

            // CancellationToken 的 WaitHandle 一读就抛，是真实存在的坑；
            // 整体序列化会因为它把整份参数丢掉，逐个序列化才只丢它自己
            var arguments = new Dictionary<string, object?>
            {
                ["request"] = new LoginBody { UserName = "bob", Password = "x" },
                ["cancellationToken"] = new CancellationToken(canceled: false),
            };

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http, arguments),
                Next(http, VivApiResult.Success("ok")));

            var record = Assert.Single(writer.Records);
            Assert.NotNull(record.RequestJson);
            Assert.Contains("bob", record.RequestJson);
        }

        [Fact]
        public async Task 打开响应采集后_同样脱敏()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _, new OperationLogOptions { IncludeResponse = true });
            var (http, _) = CreateHttp();

            var result = VivApiResult.Success("登录成功", new { userName = "bob", accessToken = "eyJhbGciOi.payload.sig" });

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, result));

            var record = Assert.Single(writer.Records);
            Assert.NotNull(record.ResponseJson);
            Assert.DoesNotContain("eyJhbGciOi.payload.sig", record.ResponseJson);
        }

        [Fact]
        public async Task 默认不采响应()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, VivApiResult.Success("ok", new { a = 1 })));

            Assert.Null(Assert.Single(writer.Records).ResponseJson);
        }

        [Fact]
        public async Task 超长参数被截断()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _, new OperationLogOptions { MaxPayloadChars = 20 });
            var (http, _) = CreateHttp();

            var arguments = new Dictionary<string, object?>
            {
                ["request"] = new LoginBody { UserName = new string('a', 200), Password = "x" },
            };

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http, arguments),
                Next(http, VivApiResult.Success("ok")));

            var record = Assert.Single(writer.Records);
            Assert.NotNull(record.RequestJson);
            Assert.EndsWith("[截断]", record.RequestJson);
            Assert.True(record.RequestJson!.Length <= 20 + "...[截断]".Length);
        }

        [Fact]
        public async Task action抛异常_记失败并原样抛出()
        {
            var writer = new RecordingWriter();
            var filter = CreateFilter([writer], out _);
            var (http, _) = CreateHttp();

            ActionExecutionDelegate throwing = () => throw new InvalidOperationException("boom");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                filter.OnActionExecutionAsync(CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http), throwing));

            var record = Assert.Single(writer.Records);
            Assert.False(record.Succeeded);
            Assert.Equal((int)ApiResultCode.ServerError, record.ResultCode);
        }

        [Fact]
        public async Task 一个出口抛异常_不影响另一个出口_也不影响请求()
        {
            var broken = new RecordingWriter { ThrowOnWrite = new InvalidOperationException("db down") };
            var healthy = new RecordingWriter();
            var filter = CreateFilter([broken, healthy], out _);
            var (http, _) = CreateHttp();

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, VivApiResult.Success("ok")));

            Assert.Single(healthy.Records);
        }

        [Fact]
        public async Task 没有出口_只告警不抛()
        {
            var filter = CreateFilter([], out _);
            var (http, logger) = CreateHttp();

            await filter.OnActionExecutionAsync(
                CreateContext(DescriptorFor(nameof(SampleController.WithAttr)), http),
                Next(http, VivApiResult.Success("ok")));

            Assert.Contains(logger.Warnings, w => w.Contains("IOperationLogWriter"));
        }
    }
}
