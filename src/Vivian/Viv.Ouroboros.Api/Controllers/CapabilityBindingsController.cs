using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Delusion.Generic;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// 能力绑定管理接口：管理后台用。CapabilityKey 必须指向真实存在的能力，否则不入库。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CapabilityBindingsController : ControllerBase
    {
        private readonly ICapabilityBindingAdminService _bindings;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="bindings">能力绑定管理服务</param>
        public CapabilityBindingsController(ICapabilityBindingAdminService bindings)
        {
            _bindings = bindings;
        }

        /// <summary>
        /// 分页列表
        /// </summary>
        /// <param name="agentKey">按宿主 Agent 业务键过滤</param>
        /// <param name="capabilityType">按能力类型过滤，取 EmCapabilityType</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <returns>能力绑定列表</returns>
        [HttpGet]
        [ProducesResponseType(typeof(PagedList<CapabilityBindingItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAsync([FromQuery] string? agentKey, [FromQuery] int? capabilityType,
            [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            return await _bindings.ListAsync(agentKey, capabilityType, pageIndex, pageSize);
        }

        /// <summary>
        /// 详情
        /// </summary>
        /// <param name="id">绑定行 Id</param>
        /// <returns>能力绑定详情</returns>
        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(CapabilityBindingDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAsync(long id)
        {
            return await _bindings.GetAsync(id);
        }

        /// <summary>
        /// 新增
        /// </summary>
        /// <param name="request">新增请求</param>
        /// <returns>新增后的能力绑定详情</returns>
        [HttpPost]
        [ProducesResponseType(typeof(CapabilityBindingDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateCapabilityBindingRequest request)
        {
            return await _bindings.CreateAsync(request);
        }

        /// <summary>
        /// 修改：AgentKey 不在请求里，换宿主等于换一条绑定
        /// </summary>
        /// <param name="request">修改请求</param>
        /// <returns>修改后的能力绑定详情</returns>
        [HttpPut]
        [ProducesResponseType(typeof(CapabilityBindingDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateAsync([FromBody] UpdateCapabilityBindingRequest request)
        {
            return await _bindings.UpdateAsync(request);
        }

        /// <summary>
        /// 启用
        /// </summary>
        /// <param name="id">绑定行 Id</param>
        /// <returns>启用后的能力绑定详情</returns>
        [HttpPost("{id:long}/enable")]
        [ProducesResponseType(typeof(CapabilityBindingDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> EnableAsync(long id)
        {
            return await _bindings.SetEnabledAsync(id, true);
        }

        /// <summary>
        /// 停用：停用是收紧权限的方向，不做依赖拦截
        /// </summary>
        /// <param name="id">绑定行 Id</param>
        /// <returns>停用后的能力绑定详情</returns>
        [HttpPost("{id:long}/disable")]
        [ProducesResponseType(typeof(CapabilityBindingDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> DisableAsync(long id)
        {
            return await _bindings.SetEnabledAsync(id, false);
        }
    }
}
