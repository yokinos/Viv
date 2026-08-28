using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Entity.Vue;

namespace Viv.Apex.Core.Entity.Vo.User
{
    public class GetLoginDataOutput
    {
        /// <summary>
        /// 基础用户信息
        /// </summary>
        public UserBaseInfo User { get; set; }

        /// <summary>
        /// 用户所属机构？组织？集团？
        /// </summary>
        public UserSubjectInfo SubjectInfo { get; set; }

        /// <summary>
        /// 该用户路由信息
        /// </summary>
        public List<RouteItem> Routes { get; set; }

        /// <summary>
        /// 授权的按钮列表
        /// </summary>
        public List<ButtonAuthItem> ButtonList { get; set; }
    }

    public class UserBaseInfo
    {
        public long UserId { get; set; }
        public string UserName { get; set; }
        public string AvatarUrl { get; set; }
        public bool IsSuperAdmin { get; set; }
        public string Phone { get; set; }
        public string Nickname { get; set; }
        public EmUserType UserType { get; set; }
    }

    public class UserSubjectInfo
    {
        public long SubjectId { get; set; }

        public string SubjectName { get; set; }

        public string SubjectCode { get; set; }
    }

    public class ButtonAuthItem
    {
        public long MenuId { get; set; }

        public List<ButtonItem> Buttons { get; set; } = [];
    }
}
