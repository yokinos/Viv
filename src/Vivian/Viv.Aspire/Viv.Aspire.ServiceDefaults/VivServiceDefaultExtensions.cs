using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Hosting
{
    public static class VivServiceDefaultExtensions
    {
        /// <summary>
        /// 从配置文件读取 OtlpOptions，并支持采样率配置
        /// </summary>
        public static TBuilder AddVivServiceDefaults<TBuilder>(this TBuilder builder, Action<OtlpOptions>? configure = null, double sampler = 0.3)
            where TBuilder : IHostApplicationBuilder
        {
            var options = new OtlpOptions();
            builder.Configuration.GetSection("Otlp").Bind(options);
            configure?.Invoke(options);
            return AddVivServiceDefaults(builder, options, sampler);
        }

        /// <summary>
        /// 直接传入配置对象
        /// </summary>
        public static TBuilder AddVivServiceDefaults<TBuilder>(this TBuilder builder, OtlpOptions options, double sampler = 0.3)
            where TBuilder : IHostApplicationBuilder
        {
            if (!options.Enabled || options.Endpoints == null || options.Endpoints.Count == 0)
            {
                return builder;
            }

            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing =>
                {
                    tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampler)));
                    tracing.AddAspNetCoreInstrumentation()
                           .AddHttpClientInstrumentation();

                    foreach (var endpoint in options.Endpoints)
                    {
                        tracing.AddOtlpExporter(exporter =>
                        {
                            if (string.IsNullOrEmpty(endpoint.Endpoint))
                            {
                                throw new ArgumentException("Endpoint cannot be null or empty", nameof(endpoint.Endpoint));
                            }
                            exporter.Endpoint = new Uri(endpoint.Endpoint);
                            exporter.Protocol = endpoint.Protocol ?? OtlpExportProtocol.HttpProtobuf;
                            exporter.TimeoutMilliseconds = endpoint.TimeoutMilliseconds ?? 10000;

                            if (endpoint.Headers != null && endpoint.Headers.Count != 0)
                            {
                                exporter.Headers = string.Join(",", endpoint.Headers.Select(h => $"{h.Key}={h.Value}"));
                            }
                        });
                    }
                });

            return builder;
        }
    }

    public class OtlpOptions
    {
        public bool Enabled { get; set; } = true;
        public List<OtlpEndpointConfig>? Endpoints { get; set; }
    }

    public class OtlpEndpointConfig
    {
        public string? Name { get; set; }
        public string? Endpoint { get; set; }
        public OtlpExportProtocol? Protocol { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public int? TimeoutMilliseconds { get; set; }
    }
}