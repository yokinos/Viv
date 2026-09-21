using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Viv.Contracts.Interface
{
    public interface IVivAppModule
    {
        IServiceCollection AddThisModule(IServiceCollection services);
    }
}
