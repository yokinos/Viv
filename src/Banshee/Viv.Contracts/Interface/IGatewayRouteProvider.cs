using System;
using System.Collections.Generic;
using System.Text;
using Yarp.ReverseProxy.Configuration;

namespace Viv.Contracts.Interface
{
    public interface IGatewayRouteProvider
    {
        (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build();
    }
}
