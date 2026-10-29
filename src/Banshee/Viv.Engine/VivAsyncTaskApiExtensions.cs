using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Viv.Contracts.Interface;
using Viv.Skirk;
using Viv.Skirk.Core;

namespace Viv.Engine
{
    /// <summary>
    /// 异步任务（Skirk）的查询/取消端点。
    ///
    /// 全部用 **POST**（含查询），和平台其余接口一致：本平台每个请求体都要带 <c>version</c>
    /// （<c>VivApiRequest</c> 必填 1000–9999），GET 没有 body 带不了；筛选条件放 body 也好扩展。
    ///
    /// 只提供**读与取消**，不提供"按 type 提交任意任务"：提交端点留在业务侧，
    /// 那里才有参数校验、业务行与幂等键语义。
    ///
    /// 归属一律以 <see cref="IVivContext"/> 为准：只能看/取消自己的任务，不接受客户端传的归属条件。
    /// </summary>
    public static class VivAsyncTaskApiExtensions
    {
        /// <summary>默认路由前缀</summary>
        public const string DefaultRoutePrefix = "/api/VivAsyncTasks";

        /// <summary>
        /// 挂载三个端点：
        /// <list type="bullet">
        /// <item><description><c>POST {prefix}/get</c> —— 单个任务状态/进度/结果</description></item>
        /// <item><description><c>POST {prefix}/query</c> —— 我的任务列表</description></item>
        /// <item><description><c>POST {prefix}/cancel</c> —— 请求取消</description></item>
        /// </list>
        /// 宿主配了鉴权时自动加 <c>RequireAuthorization()</c>。
        /// </summary>
        public static IEndpointRouteBuilder MapVivAsyncTasks(
            this IEndpointRouteBuilder endpoints,
            string routePrefix = DefaultRoutePrefix)
        {
            var group = endpoints.MapGroup(routePrefix.TrimEnd('/'));

            // 匿名服务（没配 TokenOption）不挂这组端点：暴露任务表没有意义
            if (endpoints.ServiceProvider.GetService<IAuthenticationSchemeProvider>() is not null)
            {
                group.RequireAuthorization();
            }

            group.MapPost("/get", GetAsync);
            group.MapPost("/query", QueryAsync);
            group.MapPost("/cancel", CancelAsync);

            return endpoints;
        }

        private static async Task<IResult> GetAsync(
            [FromBody] VivAsyncTaskGetRequest request,
            HttpContext http,
            CancellationToken cancellationToken)
        {
            var tasks = http.RequestServices.GetRequiredService<IVivAsyncTask>();
            var context = http.RequestServices.GetRequiredService<IVivContext>();

            var task = await tasks.GetAsync(request.Id, cancellationToken).ConfigureAwait(false);

            // 不是自己的任务一律当"不存在"，不泄露存在性
            if (task is null || !BelongsTo(task, context))
            {
                return Results.Ok(VivApiResult.ApiResult(ApiResultCode.NotFound, "任务不存在"));
            }

            return Results.Ok(VivApiResult.Success(ToView(task)));
        }

        private static async Task<IResult> QueryAsync(
            [FromBody] VivAsyncTaskQueryRequest request,
            HttpContext http,
            CancellationToken cancellationToken)
        {
            var tasks = http.RequestServices.GetRequiredService<IVivAsyncTask>();
            var context = http.RequestServices.GetRequiredService<IVivContext>();

            if (context.UserId <= 0)
            {
                return Results.Ok(VivApiResult.ApiResult(ApiResultCode.NoPermission, "无法识别当前用户"));
            }

            var paged = await tasks.QueryAsync(
                new VivAsyncTaskQuery
                {
                    // 归属永远取上下文，请求体里的条件只用来缩小范围
                    SubjectId = context.SubjectId > 0 ? context.SubjectId : null,
                    UserId = context.UserId,
                    TaskType = string.IsNullOrWhiteSpace(request.TaskType) ? null : request.TaskType,
                    Status = request.Status is null ? null : (VivAsyncTaskStatus)request.Status.Value,
                    PageIndex = request.PageIndex,
                    PageSize = request.PageSize,
                },
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(VivApiResult.Success(new
            {
                paged.TotalCount,
                paged.PageIndex,
                paged.PageSize,
                paged.TotalPages,
                paged.IsHaveFrontPage,
                paged.IsHaveNextPage,
                Items = paged.Items.Select(ToView).ToList(),
            }));
        }

        private static async Task<IResult> CancelAsync(
            [FromBody] VivAsyncTaskCancelRequest request,
            HttpContext http,
            CancellationToken cancellationToken)
        {
            var tasks = http.RequestServices.GetRequiredService<IVivAsyncTask>();
            var context = http.RequestServices.GetRequiredService<IVivContext>();

            var task = await tasks.GetAsync(request.Id, cancellationToken).ConfigureAwait(false);
            if (task is null || !BelongsTo(task, context))
            {
                return Results.Ok(VivApiResult.ApiResult(ApiResultCode.NotFound, "任务不存在"));
            }

            var canceled = await tasks.CancelAsync(request.Id, cancellationToken).ConfigureAwait(false);

            return canceled
                ? Results.Ok(VivApiResult.Success(new { id = request.Id, cancelRequested = true }))
                : Results.Ok(VivApiResult.ApiResult(ApiResultCode.Error, "任务已结束，无法取消"));
        }

        /// <summary>归属判定：同一用户、且（同租户或任一为空）</summary>
        private static bool BelongsTo(VivAsyncTask task, IVivContext context)
        {
            if (context.UserId <= 0 || task.UserId is null || task.UserId != context.UserId) return false;

            return task.SubjectId is null || context.SubjectId <= 0 || task.SubjectId == context.SubjectId;
        }

        /// <summary>对外投影：不暴露租约、重试次数、幂等键这些内部字段</summary>
        private static VivAsyncTaskView ToView(VivAsyncTask task)
            => new(
                task.Id,
                task.TaskType,
                (int)task.Status,
                task.Status.ToString(),
                task.Progress,
                task.TotalCount,
                task.DoneCount,
                task.ProgressMessage,
                task.ResultJson,
                task.ErrorMessage,
                task.CreatedAt,
                task.StartedAt,
                task.FinishedAt);

        /// <summary>任务视图（前端任务中心用）</summary>
        private sealed record VivAsyncTaskView(
            long Id,
            string TaskType,
            int Status,
            string StatusName,
            int Progress,
            int? TotalCount,
            int? DoneCount,
            string? ProgressMessage,
            string? ResultJson,
            string? ErrorMessage,
            DateTime CreatedAt,
            DateTime? StartedAt,
            DateTime? FinishedAt);
    }

    /// <summary>查单个任务（<c>version</c> 由请求层统一注入，这里不必声明）</summary>
    public sealed class VivAsyncTaskGetRequest
    {
        public long Id { get; set; }
    }

    /// <summary>查我的任务列表</summary>
    public sealed class VivAsyncTaskQueryRequest
    {
        /// <summary>状态过滤：0 待执行 / 1 执行中 / 2 成功 / 3 失败 / 4 已取消，空 = 全部</summary>
        public int? Status { get; set; }

        public string? TaskType { get; set; }

        /// <summary>页码，从 1 开始</summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>每页条数，默认 20，最大 200</summary>
        public int PageSize { get; set; } = 20;
    }

    /// <summary>请求取消</summary>
    public sealed class VivAsyncTaskCancelRequest
    {
        public long Id { get; set; }
    }
}
