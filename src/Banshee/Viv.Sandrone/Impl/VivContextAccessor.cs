using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Sandrone.Impl
{
    /// <summary>
    /// IVivContextAccessor 实现
    /// AsyncLocal 唯一存放位置，禁止在其他类新增 AsyncLocal
    /// </summary>
    public class VivContextAccessor : IVivContextAccessor
    {
        private static readonly AsyncLocal<VivContextModel?> _storage = new();

        public VivContextModel? Current
        {
            get => _storage.Value;
            set => _storage.Value = value;
        }
    }
}
