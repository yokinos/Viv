using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Core;
using Viv.Nana.Models;

namespace Viv.Nana.LocalMessage
{
    /// <summary>
    /// 提供默认实现ILocalMessageRespository接口的类，所有方法均为无操作（No-op）
    /// </summary>
    public class NoneLocalRespository : ILocalMessageRespository
    {
        public async Task<bool> AddMessageAsync<T>(NanaMessage<T> message) where T : VivMessage
        {
            return false;
        }
    }
}