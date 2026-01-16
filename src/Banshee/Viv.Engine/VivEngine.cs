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
        private static bool isInit = false;
        private static readonly Lock _initlock = new();

        /// <summary>
        /// Viv配置选项
        /// </summary>
        public static VivOptions VivOptions { get; private set; }

        /// <summary>
        /// 不允许实例化
        /// </summary>
        private VivEngine() { }

        public static void Initialize(VivOptions options)
        {
            lock (_initlock)
            {
                if (isInit) return;


            }
        }
    }
}
