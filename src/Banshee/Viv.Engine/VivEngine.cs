using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Engine.Options;

#nullable disable
namespace Viv.Engine
{
    public sealed class VivEngine
    {
        private static volatile VivOptions _vivOptions;

        /// <summary>
        /// Viv配置选项
        /// </summary>
        public static VivOptions VivOptions { get => _vivOptions; }

        /// <summary>
        /// 不允许实例化
        /// </summary>
        private VivEngine() { }
    }
}
