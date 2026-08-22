using AdvisorBenchmarking;
using ORMConvertorAPI.Services;

namespace ORMConvertorAPI;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IBenchmarkExecutor, BenchmarkExecutor>();
        builder.Services.AddSingleton<IAdvisorRunCoordinator, AdvisorRunCoordinator>();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UsePathBase("/orm");
        // Before UseRouting: static assets are endpoints, so the rewrite of "/" to
        // index.html has to happen before endpoint matching, not after it.
        app.UseDefaultFiles();
        app.UseRouting();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthorization();

        Endpoints.Map(app);

        // Serves the .br/.gz copies the publish already produces, chosen by the
        // request's Accept-Encoding; UseStaticFiles never looked at them.
        app.MapStaticAssets();

        app.Run();
    }
}
