using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Toolbox.CommandSetting.Basic
{
    /// <summary>
    /// initviv 的参数：全部给了就不问，缺什么问什么
    /// </summary>
    public class InitVivSettings : CommandSettings
    {
        /// <summary>
        /// 数据库地址（含端口），如 43.228.79.205,1433
        /// </summary>
        [CommandOption("--conn <SERVER>")]
        public string? Server { get; set; }

        /// <summary>
        /// 数据库账号
        /// </summary>
        [CommandOption("--dbuser <ACCOUNT>")]
        public string? DbUser { get; set; }

        /// <summary>
        /// 数据库密码
        /// </summary>
        [CommandOption("--dbpwd <PASSWORD>")]
        public string? DbPassword { get; set; }

        /// <summary>
        /// 初始操作员用户名
        /// </summary>
        [CommandOption("--user <NAME>")]
        public string? UserName { get; set; }

        /// <summary>
        /// 初始操作员昵称
        /// </summary>
        [CommandOption("--nick <NICKNAME>")]
        public string? NickName { get; set; }

        /// <summary>
        /// 初始操作员手机号（必填）
        /// </summary>
        [CommandOption("--phone <PHONE>")]
        public string? Phone { get; set; }

        /// <summary>
        /// 初始操作员密码
        /// </summary>
        [CommandOption("--pwd <PASSWORD>")]
        public string? Password { get; set; }

        /// <summary>
        /// 免确认（自动化用），必须同时给全上面各值
        /// </summary>
        [CommandOption("--yes")]
        public bool Yes { get; set; }

        /// <summary>
        /// 重新生成：先删掉该库中本域的表，再按实体重建
        /// </summary>
        [CommandOption("--drop")]
        public bool Drop { get; set; }

        /// <summary>
        /// 只打印将要执行的 SQL（删表），不实际执行
        /// </summary>
        [CommandOption("--sql-only")]
        public bool SqlOnly { get; set; }
    }
}
