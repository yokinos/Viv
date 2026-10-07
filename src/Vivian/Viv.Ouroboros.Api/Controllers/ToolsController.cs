using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Delusion.Generic;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// 工具管理接口：管理后台用。ParamsSchema 必须是合法 JSON 对象，否则不入库。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ToolsController : ControllerBase
    {
        private readonly IToolAdminService _tools;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="tools">工具管理服务</param>
        public ToolsController(IToolAdminService tools)
        {
            _tools = tools;
        }

        /// <summary>
        /// 分页列表
        /// </summary>
        /// <param name="toolKey">按工具键过滤</param>
        /// <param name="ownerDomain">按归属域过滤</param>
        /// <param name="transport">按传输方式过滤，取 EmToolTransport</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <returns>工具列表</returns>
        [HttpGet]
        [ProducesResponseType(typeof(PagedList<ToolItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAsync([FromQuery] string? toolKey, [FromQuery] string? ownerDomain,
            [FromQuery] int? transport, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            return await _tools.ListAsync(toolKey, ownerDomain, transport, pageIndex, pageSize);
        }

        /// <summary>
        /// 详情
        /// </summary>
        /// <param name="id">工具行 Id</param>
        /// <returns>工具详情</returns>
        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(ToolDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAsync(long id)
        {
            return await _tools.GetAsync(id);
        }

        /// <summary>
        /// 新增
        /// </summary>
        /// <param name="request">新增请求</param>
        /// <returns>新增后的工具详情</returns>
        [HttpPost]
        [ProducesResponseType(typeof(ToolDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateToolRequest request)
        {
            return await _tools.CreateAsync(request);
        }

        /// <summary>
        /// 修改：ToolKey 不在请求里，改键会让能力绑定悬空
        /// </summary>
        /// <param name="request">修改请求</param>
        /// <returns>修改后的工具详情</returns>
        [HttpPut]
        [ProducesResponseType(typeof(ToolDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateAsync([FromBody] UpdateToolRequest request)
        {
            return await _tools.UpdateAsync(request);
        }

        /// <summary>
        /// 启用
        /// </summary>
        /// <param name="id">工具行 Id</param>
        /// <returns>启用后的工具详情</returns>
        [HttpPost("{id:long}/enable")]
        [ProducesResponseType(typeof(ToolDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> EnableAsync(long id)
        {
            return await _tools.SetEnabledAsync(id, true, true);
        }

        /// <summary>
        /// 停用：还被启用中的绑定引用时，需 force=true 确认（紧急拉闸也走这条）
        /// </summary>
        /// <param name="id">工具行 Id</param>
        /// <param name="force">是否确认跳过依赖检查</param>
        /// <returns>停用后的工具详情</returns>
        [HttpPost("{id:long}/disable")]
        [ProducesResponseType(typeof(ToolDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> DisableAsync(long id, [FromQuery] bool force = false)
        {
            return await _tools.SetEnabledAsync(id, false, force);
        }
    }
}
