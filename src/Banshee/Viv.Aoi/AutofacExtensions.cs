using Autofac;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Vva.Magic;

namespace Viv.Aoi
{
    public static class AutofacExtensions
    {
        public static void VivRegister(this ContainerBuilder builder, DIOptions diOptions, Action<ContainerBuilder> customSet = null)
        {
            var serviceImplTypes = TypeScanMagic.Scan(diOptions.ServiceImplementation);

            builder.RegisterTypes(serviceImplTypes.ToArray())
                   .AsImplementedInterfaces()
                   .InstancePerLifetimeScope();

            var repoImplTypes = TypeScanMagic.Scan(diOptions.RepositoryImplementation);

            builder.RegisterTypes(repoImplTypes.ToArray())
                   .AsImplementedInterfaces()
                   .InstancePerLifetimeScope();

            customSet?.Invoke(builder);
        }
    }
}
