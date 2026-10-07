using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viv.Delusion.Generic;
using Viv.Ouroboros.Core.Entity.Dto.Agent;
using Viv.Ouroboros.Core.Entity.Vo.Agent;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Api.Controllers
{
    /// <summary>
    /// 模型档位管理接口：管理后台用。密钥只进不出 —— 新增/修改收明文，查询只回 hasKey。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ModelProfilesController : ControllerBase
    {
        private readonly IModelProfileAdminService _profiles;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="profiles">模型档位管理服务</param>
        public ModelProfilesController(IModelProfileAdminService profiles)
        {
            _profiles = profiles;
        }

        /// <summary>
        /// 分页列表
        /// </summary>
        /// <param name="profileKey">按档位键过滤，不给则全部</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <returns>档位列表（不含密钥）</returns>
        [HttpGet]
        [ProducesResponseType(typeof(PagedList<ModelProfileItemOutput>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAsync([FromQuery] string? profileKey,
            [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            return await _profiles.ListAsync(profileKey, pageIndex, pageSize);
        }

        /// <summary>
        /// 详情
        /// </summary>
        /// <param name="id">档位行 Id</param>
        /// <returns>档位详情（不含密钥）</returns>
        [HttpGet("{id:long}")]
        [ProducesResponseType(typeof(ModelProfileDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAsync(long id)
        {
            return await _profiles.GetAsync(id);
        }

        /// <summary>
        /// 新增：apiKey 传明文，服务端加密后落 ApiKeyCipher
        /// </summary>
        /// <param name="request">新增请求</param>
        /// <returns>新增后的档位详情（不含密钥）</returns>
        [HttpPost]
        [ProducesResponseType(typeof(ModelProfileDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateAsync([FromBody] CreateModelProfileRequest request)
        {
            return await _profiles.CreateAsync(request);
        }

        /// <summary>
        /// 修改：apiKey 为 null 表示不改动现有密钥，空串表示清空
        /// </summary>
        /// <param name="request">修改请求</param>
        /// <returns>修改后的档位详情（不含密钥）</returns>
        [HttpPut]
        [ProducesResponseType(typeof(ModelProfileDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateAsync([FromBody] UpdateModelProfileRequest request)
        {
            return await _profiles.UpdateAsync(request);
        }

        /// <summary>
        /// 启用
        /// </summary>
        /// <param name="id">档位行 Id</param>
        /// <returns>启用后的档位详情（不含密钥）</returns>
        [HttpPost("{id:long}/enable")]
        [ProducesResponseType(typeof(ModelProfileDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> EnableAsync(long id)
        {
            return await _profiles.SetEnabledAsync(id, true, true);
        }

        /// <summary>
        /// 停用：停掉某档位唯一启用的一行会让用它的 Agent 装不出来，需 force=true 确认
        /// </summary>
        /// <param name="id">档位行 Id</param>
        /// <param name="force">是否确认跳过依赖检查</param>
        /// <returns>停用后的档位详情（不含密钥）</returns>
        [HttpPost("{id:long}/disable")]
        [ProducesResponseType(typeof(ModelProfileDetailOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> DisableAsync(long id, [FromQuery] bool force = false)
        {
            return await _profiles.SetEnabledAsync(id, false, force);
        }

        /// <summary>
        /// 用当前主钥匙重新加密库里已有的档位密钥（换 VivOptions.EnvOption.AiKeySecret 时的迁移入口）。
        /// 会改写每一行密文，必须 force=true 确认；返回数量与解不开的档位键，不回密钥与密文。
        /// </summary>
        /// <param name="force">是否确认执行</param>
        /// <returns>重加密结果</returns>
        [HttpPost("reencrypt")]
        [ProducesResponseType(typeof(ModelProfileReencryptOutput), StatusCodes.Status200OK)]
        public async Task<IActionResult> ReencryptAsync([FromQuery] bool force = false)
        {
            return await _profiles.ReencryptAsync(force);
        }
    }
}
