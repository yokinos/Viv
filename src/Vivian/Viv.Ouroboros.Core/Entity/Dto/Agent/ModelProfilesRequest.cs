using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// 模型档位管理接口的新增请求
    /// </summary>
    /// <param name="ProfileKey">档位键：main / sub / router / summarize / vision</param>
    /// <param name="ProviderType">供应商类型，取 EmProviderType</param>
    /// <param name="ApiUrl">接口地址</param>
    /// <param name="ApiKey">密钥**明文**，只在写入时收，落库前加密；为空表示这一行没有密钥</param>
    /// <param name="Model">模型名</param>
    /// <param name="Temperature">采样温度，为空用供应商默认值</param>
    /// <param name="MaxOutputTokens">最大输出 token，为空用供应商默认值</param>
    /// <param name="TimeoutSeconds">调用超时秒数，为空按 60</param>
    /// <param name="Priority">同档位多行时的兜底顺序，小的优先</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    /// <param name="Remark">备注</param>
    public sealed record CreateModelProfileRequest(string ProfileKey, int ProviderType, string ApiUrl, string? ApiKey, string Model,
        double? Temperature, int? MaxOutputTokens, int? TimeoutSeconds, int? Priority, bool? IsEnabled, string? Remark);

    /// <summary>
    /// 模型档位管理接口的修改请求：整体覆盖，未给的可空字段按清空处理
    /// </summary>
    /// <param name="Id">档位行 Id</param>
    /// <param name="ProfileKey">档位键</param>
    /// <param name="ProviderType">供应商类型，取 EmProviderType</param>
    /// <param name="ApiUrl">接口地址</param>
    /// <param name="ApiKey">密钥明文；**null = 不改动现有密钥，空串 = 清空密钥，非空 = 换成新密钥**</param>
    /// <param name="Model">模型名</param>
    /// <param name="Temperature">采样温度</param>
    /// <param name="MaxOutputTokens">最大输出 token</param>
    /// <param name="TimeoutSeconds">调用超时秒数，为空按 60</param>
    /// <param name="Priority">兜底顺序</param>
    /// <param name="IsEnabled">是否启用，为空按启用</param>
    /// <param name="Remark">备注</param>
    public sealed record UpdateModelProfileRequest(long Id, string ProfileKey, int ProviderType, string ApiUrl, string? ApiKey, string Model,
        double? Temperature, int? MaxOutputTokens, int? TimeoutSeconds, int? Priority, bool? IsEnabled, string? Remark);
}
