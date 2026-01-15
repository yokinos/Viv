using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Viv.Test.Core
{
    public class CommandModel
    {
        private string _description;

        public CommandModel() { }

        public CommandModel(string cmd, CommandType cmdType, string description = "")
        {
            this.Command = cmd;
            this.CommandType = cmdType;
            this.Description = description;
        }

        public void SetTypeInfo(Type type)
        {
            if (type.IsClass)
            {
                this.ClassName = type.Name;
                this.NameSpace = type.Namespace;
                this.AssemblyName = type.Assembly.ManifestModule.Name;
            }
        }

        /// <summary>
        /// 指令
        /// </summary>
        public string Command { get; set; }

        /// <summary>
        /// 指令类型
        /// </summary>
        public CommandType CommandType { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description
        {
            get => _description ?? ClassName;
            set => _description = value;
        }

        /// <summary>
        /// 程序集名称
        /// </summary>
        public string AssemblyName { get; set; }

        /// <summary>
        /// 命名空间
        /// </summary>
        public string NameSpace { get; set; }

        /// <summary>
        /// 类名称
        /// </summary>
        public string ClassName { get; set; }

        /// <summary>
        /// type 全名
        /// </summary>
        public string ClassFullName => $"{NameSpace}.{ClassName}";
    }
}
