using Viv.Contracts.Interface;

namespace Viv.Engine.Tests
{
    /// <summary>启动自检：只认「名字像服务/仓储 + 两条注册路径都没走」的类，其余一律放过</summary>
    public class DependencySelfCheckTests
    {
        private class MarkedService : IDependency { }

        private class PlainService { }

        private class PlainRepository { }

        private class ScannedService { }

        private abstract class AbstractService { }

        private class GenericService<T> { }

        private class BusinessRule { }

        [Fact]
        public void 没标记也没被扫描到的服务与仓储会被报出来()
        {
            var found = DependencySelfCheck.FindUnregistered(
                [typeof(PlainService), typeof(PlainRepository)],
                []);

            Assert.Equal([typeof(PlainRepository), typeof(PlainService)], found.OrderBy(x => x.Name));
            Assert.Equal(2, found.Count);
        }

        [Fact]
        public void 实现了IDependency或被扫描注册的都不算漏()
        {
            var found = DependencySelfCheck.FindUnregistered(
                [typeof(MarkedService), typeof(ScannedService)],
                [typeof(ScannedService)]);

            Assert.Empty(found);
        }

        [Fact]
        public void 抽象类_开放泛型_名字不匹配的都不参与()
        {
            var found = DependencySelfCheck.FindUnregistered(
                [typeof(AbstractService), typeof(GenericService<>), typeof(BusinessRule)],
                []);

            Assert.Empty(found);
        }

        [Fact]
        public void 测试项目与测试替身程序集被排除()
        {
            Assert.True(DependencySelfCheck.IsExcludedAssembly(typeof(DependencySelfCheckTests).Assembly));
            Assert.False(DependencySelfCheck.IsExcludedAssembly(typeof(DependencySelfCheck).Assembly));
        }

        [Fact]
        public void 报错文案带上类型全名和做法()
        {
            var message = DependencySelfCheck.BuildMessage([typeof(PlainService)]);

            Assert.Contains(nameof(PlainService), message);
            Assert.Contains("IDependency", message);
            Assert.Contains("DIOption", message);
        }
    }
}
