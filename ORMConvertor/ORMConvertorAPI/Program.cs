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

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // app.UseHttpsRedirection();

        app.UseAuthorization();

        Endpoints.Map(app);

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapFallbackToFile("{*path:nonfile}", "index.html");

        app.Run();
    }
}
