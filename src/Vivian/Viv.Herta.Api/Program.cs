using Viv.Engine;

namespace Viv.Herta.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        var vivOptions = VivEngine.LoadVivConfig(builder.Configuration);
        ArgumentNullException.ThrowIfNull(vivOptions);

        builder.Services.AddViv(vivOptions);
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.MapDefaultEndpoints();

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
