-- ============================================================================
--  Apex 初始数据（种子）
--
--  为什么固定 Id：种子就该是「同一份东西，装到哪台库里都长一样」。
--  运行期新增的数据走 IdMagic.NextId()（雪花），种子没有这个必要 ——
--  固定 Id 换来的是一眼能看懂的外键：菜单 102 的父节点就是 101，不用查。
--
--  Id 分配（各表独立，互不影响）：
--      AtClientApp      1
--      AtMenu           101 目录 / 102 页面 / 111 目录 / 112 页面
--      AtMenuSubPage    201 / 211 / 212
--      AtMenuAction     301..304
--      AtUser           1（超级管理员）
--
--  BitIndex：位号是「应用内」自增的（角色掩码按「角色 × 应用」存，见 AtUserRoleApp），
--  所以这里从 0 连续发号即可 —— 种子是这个应用的第一批数据。
--
--  幂等：每条 INSERT 都先判 Id 是否存在（不判 IsDeleted）。已软删的行也占着主键，
--  再插一遍只会撞 PK；而「跳过一个软删的种子行」正是想要的行为：有人删过就别偷偷复活。
--
--  参数：@userName / @nickName / @phone / @passwordHash / @salt（初始操作员，口令哈希在 C# 里算）
-- ============================================================================

-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM AtClientApp WHERE Id = 1 OR AppKey = N'viv-rootadmin-app')
BEGIN
    INSERT INTO AtClientApp (Id, AppKey, Name, Platform, ClientStack, AppSecret, Source, Remark, Status, IsDeleted, CreatedAt)
    VALUES (1, N'viv-rootadmin-app', N'Viv管理后台',
            0,                                  -- EmAppPlatform.Web
            0,                                  -- EmClientStack.Vue
            LOWER(REPLACE(CONVERT(varchar(36), NEWID()), '-', '')),
            0,                                  -- EmAppSouce.Viv
            N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
END

-- ---------------------------------------------------------------------------
-- 2. 菜单 / 子页面 / 动作 —— 只在「应用就是种子这一行（Id = 1）」时才播，
--    否则会得到一棵挂在不存在应用下的孤儿树。
--
--    App管理（目录，code=app）→ App列表（页面）
--    系统设置（目录，code=setting）→ 员工管理（页面）
--    MenuCode 是服务端 [VivRequirePermission("app")] 判定的标识；不带冒号 = 页面/模块权限。
-- ---------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM AtClientApp WHERE Id = 1)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM AtMenu WHERE Id = 101)
    BEGIN
        INSERT INTO AtMenu (Id, ClientAppId, BitIndex, ParentId, Title, MenuCode, Icon, Sort, Type, IsVisible, RouteConfig, Description, Status, IsDeleted, CreatedAt)
        VALUES (101, 1, 0, 0, N'App管理', N'app', N'app', 1,
                0,                              -- EmMenuType.Directory
                1, N'{"path":"/app","name":"app","redirect":"/app/list"}',
                N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
    END

    IF NOT EXISTS (SELECT 1 FROM AtMenu WHERE Id = 102)
    BEGIN
        INSERT INTO AtMenu (Id, ClientAppId, BitIndex, ParentId, Title, MenuCode, Icon, Sort, Type, IsVisible, RouteConfig, Description, Status, IsDeleted, CreatedAt)
        VALUES (102, 1, 1, 101, N'App列表', NULL, NULL, 1,
                1,                              -- EmMenuType.Page
                1, N'{"path":"list","name":"app-list","component":"app/list/index"}',
                N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
    END

    IF NOT EXISTS (SELECT 1 FROM AtMenu WHERE Id = 111)
    BEGIN
        INSERT INTO AtMenu (Id, ClientAppId, BitIndex, ParentId, Title, MenuCode, Icon, Sort, Type, IsVisible, RouteConfig, Description, Status, IsDeleted, CreatedAt)
        VALUES (111, 1, 2, 0, N'系统设置', N'setting', NULL, 2,
                0,                              -- EmMenuType.Directory
                1, N'{"path":"/system","name":"system","redirect":"/system/staff"}',
                N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
    END

    IF NOT EXISTS (SELECT 1 FROM AtMenu WHERE Id = 112)
    BEGIN
        INSERT INTO AtMenu (Id, ClientAppId, BitIndex, ParentId, Title, MenuCode, Icon, Sort, Type, IsVisible, RouteConfig, Description, Status, IsDeleted, CreatedAt)
        VALUES (112, 1, 3, 111, N'员工管理', NULL, NULL, 1,
                1,                              -- EmMenuType.Page
                1, N'{"path":"staff","name":"system-staff","component":"system/staff/index"}',
                N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
    END

    -- 3. 子页面（不进侧边栏的隐藏路由 / 页面内的 tab）
    IF EXISTS (SELECT 1 FROM AtMenu WHERE Id = 102)
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM AtMenuSubPage WHERE Id = 201)
        BEGIN
            INSERT INTO AtMenuSubPage (Id, MenuId, Title, Sort, IsVisible, RouteConfig, Remark, Status, IsDeleted, CreatedAt)
            VALUES (201, 102, N'菜单管理', 1, 0, -- IsVisible = 0：由列表行的动作进入，不占侧边栏
                    N'{"path":"menu","name":"app-list-menu","component":"app/list/menu"}',
                    N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
        END

        -- 4. 动作（操作权限）。ActionCode 必须和代码里的 Viv.Apex.Core.Permissions.App.* 对上，
        --    否则 [VivRequirePermission("app:add")] 永远解不到位号 —— 只有超管能进。
        IF NOT EXISTS (SELECT 1 FROM AtMenuAction WHERE Id = 301)
        BEGIN
            INSERT INTO AtMenuAction (Id, MenuId, BitIndex, Name, ActionType, ActionCode, Sort, Remark, Status, IsDeleted, CreatedAt)
            VALUES (301, 102, 0, N'菜单管理', 2, N'app:menu', 1,   -- ActionType 2 = EmMenuActionType.Action
                    N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
        END

        IF NOT EXISTS (SELECT 1 FROM AtMenuAction WHERE Id = 302)
        BEGIN
            INSERT INTO AtMenuAction (Id, MenuId, BitIndex, Name, ActionType, ActionCode, Sort, Remark, Status, IsDeleted, CreatedAt)
            VALUES (302, 102, 1, N'新增', 2, N'app:add', 2,
                    N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
        END

        IF NOT EXISTS (SELECT 1 FROM AtMenuAction WHERE Id = 303)
        BEGIN
            INSERT INTO AtMenuAction (Id, MenuId, BitIndex, Name, ActionType, ActionCode, Sort, Remark, Status, IsDeleted, CreatedAt)
            VALUES (303, 102, 2, N'编辑', 2, N'app:edit', 3,
                    N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
        END

        IF NOT EXISTS (SELECT 1 FROM AtMenuAction WHERE Id = 304)
        BEGIN
            INSERT INTO AtMenuAction (Id, MenuId, BitIndex, Name, ActionType, ActionCode, Sort, Remark, Status, IsDeleted, CreatedAt)
            VALUES (304, 102, 3, N'删除', 2, N'app:delete', 4,
                    N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
        END
    END

    IF EXISTS (SELECT 1 FROM AtMenu WHERE Id = 112)
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM AtMenuSubPage WHERE Id = 211)
        BEGIN
            INSERT INTO AtMenuSubPage (Id, MenuId, Title, Sort, IsVisible, RouteConfig, Remark, Status, IsDeleted, CreatedAt)
            VALUES (211, 112, N'员工列表', 1, 1,
                    N'{"path":"list","name":"system-staff-list","component":"system/staff/list"}',
                    N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
        END

        IF NOT EXISTS (SELECT 1 FROM AtMenuSubPage WHERE Id = 212)
        BEGIN
            INSERT INTO AtMenuSubPage (Id, MenuId, Title, Sort, IsVisible, RouteConfig, Remark, Status, IsDeleted, CreatedAt)
            VALUES (212, 112, N'角色列表', 2, 1,
                    N'{"path":"roles","name":"system-staff-roles","component":"system/staff/roles"}',
                    N'系统初始化创建', 0, 0, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
        END
    END
END

-- ---------------------------------------------------------------------------
-- 5. 初始操作员（超级管理员）。
--    超管 / 不建角色 / 不建用户-角色关联：IsSuperAdmin 直接拿最大数据权限，
--    角色的绑定是运营期的事，种子不该替客户编岗位。
--    口令只存 PBKDF2 哈希 + 独立盐（哈希在 C# 里算，SQL 不该知道口令算法）。
--    Name 一起判：老库里这个账号是雪花 Id，判 Id 会再建一个同名的超管。
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM AtUser WHERE Id = 1 OR (UserType = 0 AND Name = @userName))
BEGIN
    INSERT INTO AtUser (Id, UserType, Name, NickName, Phone, Password, Salt, IsSuperAdmin, Status, IsDeleted, CreatedAt)
    VALUES (1, 0, @userName, @nickName, @phone, @passwordHash, @salt, 1, 0, 0,
            TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
END
