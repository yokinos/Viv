using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 客户端技术栈<br/>
    /// </summary>
    public enum EmClientStack
    {
        /// <summary>Vue</summary>
        Vue = 0,

        /// <summary>React</summary>
        React = 1,

        /// <summary>Angular</summary>
        Angular = 2,

        /// <summary>Svelte</summary>
        Svelte = 3,

        /// <summary>Winform</summary>
        Winform = 10,

        /// <summary>WPF</summary>
        WPF = 11,

        /// <summary>Blazor（Server / WASM）</summary>
        Blazor = 12,

        /// <summary>WinUI / UWP</summary>
        WinUI = 13,

        /// <summary>.NET MAUI</summary>
        Maui = 14,

        /// <summary>Avalonia</summary>
        Avalonia = 15,

        /// <summary>Flutter</summary>
        Flutter = 20,

        /// <summary>React Native</summary>
        ReactNative = 21,

        /// <summary>UniApp</summary>
        UniApp = 22,

        /// <summary>微信小程序</summary>
        WeChatMiniProgram = 30,

        /// <summary>支付宝小程序</summary>
        AlipayMiniProgram = 31,

        /// <summary>Electron</summary>
        Electron = 32,

        /// <summary>Tauri</summary>
        Tauri = 33,
    }
}
