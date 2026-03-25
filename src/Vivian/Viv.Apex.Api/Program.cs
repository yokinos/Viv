using Autofac;


using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Viv.Aoi;
using Viv.Engine;
using Viv.Engine.Options;


public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        // Add services to the container.
        // 启用 Autofac
        builder.Services.AddAutofac();
        var vivOptions = VivEngine.LoadVivConfig();
        builder.Services.AddViv(vivOptions);
        builder.Services.AddControllers();
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
        builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
        {
            containerBuilder.VivRegister(null);
        });

        var app = builder.Build();
        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();


        app.MapControllers();

        app.Run();
    }
}
