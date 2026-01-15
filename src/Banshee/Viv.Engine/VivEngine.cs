using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine.Options;

namespace Viv.Engine
{
    public sealed class VivEngine
    { 
        private static bool isInit = false;
        private static readonly Lock _initlock = new();

        /// <summary>
        /// Viv配置选项
        /// </summary>
        public static VivOptions? VivOptions { get; private set; }

        private VivEngine() { }


    }
}
