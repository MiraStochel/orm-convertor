
using Microsoft.Extensions.Configuration;

namespace Common;
public static class DatabaseConfig
{
    private static readonly IConfigurationRoot Configuration;

    static DatabaseConfig()
    {
        Configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }

    public static string MSSQLConnectionString => Configuration.GetConnectionString("MSSQL") ?? string.Empty;
}
