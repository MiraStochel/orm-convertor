using System.Net;
using System.Net.Http.Json;
using DatabaseCatalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Model;
using ORMConvertorAPI.Logging;
using Tests.Database;

namespace Tests.Api;

/// <summary>
/// The second sentence of S4 as decision 160 reads it: no text the tool gives out about a run
/// carries the credentials it connects with - not the response of <c>/convert</c> with its
/// records and ledger, not a line of the log. The instance is configured with a catalog whose
/// user and password are probes, against the server of the test database, which refuses the
/// login; SQL Server with mixed authentication (CI, the compose profile) names the user in its
/// error text, so there the redaction is what keeps it out. Without a test database the
/// probes point at a port nothing listens on, the text names neither, and the claim is held
/// all the same.
/// </summary>
public class CredentialsLeaveNoTextTest(CredentialProbeHost host) : IClassFixture<CredentialProbeHost>
{
    private readonly HttpClient client = host.CreateClient();

    /// <summary>A bare Dapper entity: its key and columns come only from the catalog (F6).</summary>
    private const string BareDapperEntity = """
        namespace Shop;

        public class Customer
        {
            public int CustomerID { get; set; }
            public string CustomerName { get; set; } = null!;
        }
        """;

    [Fact]
    public async Task TheResponseOfARefusedCatalogNamesNeitherUserNorPassword()
    {
        var response = await client.PostAsJsonAsync($"{ApiTestHost.BasePath}/convert", new
        {
            sourceOrm = (int)ORMEnum.Dapper,
            targetOrm = (int)ORMEnum.EFCore,
            sources = new[] { new { contentType = (int)ConversionContentType.CSharp, content = BareDapperEntity } },
        }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The catalog could not be read", body, StringComparison.Ordinal);
        Assert.DoesNotContain(CredentialProbeHost.User, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CredentialProbeHost.Secret, body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The log has one output - the console, through the redacting formatter - so a line
    /// written there is the whole question. The line is the one the Advisor writes when a cell
    /// fails, with the text of the exception SqlClient throws for a refused login.
    /// </summary>
    [Fact]
    public void TheLogGoesOnlyThroughTheRedactingFormatter()
    {
        var services = host.Services;

        Assert.All(services.GetServices<ILoggerProvider>(), provider => Assert.IsType<ConsoleLoggerProvider>(provider));
        Assert.Equal(
            RedactingConsoleFormatter.FormatterName,
            services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName);

        var formatter = services.GetServices<ConsoleFormatter>()
            .Single(f => f.Name == RedactingConsoleFormatter.FormatterName);
        var refused = $"Login failed for user '{CredentialProbeHost.User}'.";
        var exception = new InvalidOperationException($"{refused} ({CredentialProbeHost.ConnectionString})");
        using var output = new StringWriter();

        formatter.Write(
            new LogEntry<string>(LogLevel.Error, "Probe", new EventId(7), $"Benchmark execution failed: {refused}", exception, (state, _) => state),
            null,
            output);
        var line = output.ToString();

        Assert.Contains("fail: Probe[7]", line, StringComparison.Ordinal);
        Assert.Contains($"Login failed for user '{CredentialRedaction.Mask}'.", line, StringComparison.Ordinal);
        Assert.DoesNotContain(CredentialProbeHost.User, line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CredentialProbeHost.Secret, line, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The application with probe credentials for both of its connections, in Production like
/// <see cref="ApiTestHost"/>. A host of its own, because the contract tests need the catalog
/// blank and this one needs it refused.
/// </summary>
public sealed class CredentialProbeHost : WebApplicationFactory<ORMConvertorAPI.Program>
{
    internal const string User = "s4probe_user";
    internal const string Secret = "S4probe-Secret-7f3a";

    internal static string ConnectionString { get; } = new SqlConnectionStringBuilder(
        TestDatabase.ConnectionString ?? "Server=tcp:127.0.0.1,1;Database=ORMConvertorTests;Connect Timeout=2")
    {
        IntegratedSecurity = false,
        UserID = User,
        Password = Secret,
        TrustServerCertificate = true,
    }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CatalogDatabase"] = ConnectionString,
                ["ConnectionStrings:AdvisorDatabase"] = ConnectionString,
            }));
    }
}
