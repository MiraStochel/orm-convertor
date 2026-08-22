using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Tests.Database;

/// <summary>
/// Where the test suite takes its database from. Decision 016 chose a local instance
/// whose schema the tests own, and made the connection a matter of configuration rather
/// than of code: the connection string is never in the repository (S4), so the move to
/// Docker Compose (decision 039) was a change of configuration, not of tests.
/// </summary>
public static class TestDatabase
{
    /// <summary>
    /// Configuration key of the connection string, spelled the same way as
    /// <c>ConnectionStrings__AdvisorDatabase</c> in <c>docker-compose.yml</c> - user
    /// secrets key <c>ConnectionStrings:TestDatabase</c>, environment variable
    /// <c>ConnectionStrings__TestDatabase</c>.
    /// </summary>
    public const string ConnectionStringName = "TestDatabase";

    /// <summary>
    /// Schema the fixture creates and drops. Fixed on purpose so that a run can be
    /// inspected by hand; the environment variable is the escape hatch for two runs
    /// sharing one instance.
    /// </summary>
    public const string DefaultSchemaName = "ormconvertor_test";

    private const string SchemaEnvironmentVariable = "ORMCONVERTOR_TEST_SCHEMA";

    /// <summary>
    /// Environment variable by which the environment states that it does provide a
    /// database (decision 039). The container configuration and the CI workflow set it,
    /// and set it exactly where they start a database of their own.
    /// </summary>
    private const string RequireEnvironmentVariable = "ORMCONVERTOR_REQUIRE_TEST_DATABASE";

    private static readonly IConfigurationRoot Configuration =
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(TestDatabase).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>Connection string, or <c>null</c> when none is configured.</summary>
    public static string? ConnectionString { get; } = ResolveConnectionString();

    /// <summary>Schema the fixture works in.</summary>
    public static string SchemaName { get; } = ResolveSchemaName();

    /// <summary>Whether a connection string was found at all.</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>
    /// Whether the environment promised a database. Where it did, a missing one is a
    /// failure instead of a skip: a run that skipped everything database-dependent looks
    /// almost like a run that passed, and that is precisely what would keep the criteria
    /// of F4 and F6 conditional (decision 039).
    /// </summary>
    public static bool IsRequired { get; } = ResolveRequired();

    /// <summary>
    /// Reason a database-dependent test is skipped when nothing is configured. Skipping
    /// is itself a statement about coverage (decision 016), so it always carries a reason
    /// saying what to do about it.
    /// </summary>
    public static string NotConfiguredReason =>
        $"No test database configured. Set the connection string in user secrets as "
        + $"\"ConnectionStrings:{ConnectionStringName}\" or in the environment variable "
        + $"ConnectionStrings__{ConnectionStringName} "
        + "(e.g. \"Server=(localdb)\\MSSQLLocalDB;Database=ORMConvertorTests;Trusted_Connection=True;"
        + "TrustServerCertificate=True\"), or run the suite in its container with "
        + "\"docker compose --profile test run --rm tests\".";

    /// <summary>
    /// Why a missing database is being reported as a failure rather than as a skip. Named
    /// after the variable so that whoever set it knows where to look.
    /// </summary>
    public static string RequiredButMissingReason(string reason) =>
        $"{RequireEnvironmentVariable} is set, so this environment states that it provides "
        + $"a test database - a skipped run here would claim coverage it does not have. {reason}";

    private static string? ResolveConnectionString()
    {
        var configured = Configuration.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }

    private static bool ResolveRequired()
    {
        var configured = Environment.GetEnvironmentVariable(RequireEnvironmentVariable)?.Trim();
        return configured is not null
            && (configured.Equals("1", StringComparison.Ordinal)
                || configured.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveSchemaName()
    {
        var configured = Environment.GetEnvironmentVariable(SchemaEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultSchemaName;
        }

        var name = configured.Trim();

        // The name is substituted straight into DDL, so anything that is not a plain
        // identifier is refused rather than quoted away.
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            throw new InvalidOperationException(
                $"{SchemaEnvironmentVariable} must be a plain SQL identifier, but was \"{configured}\".");
        }

        return name;
    }
}
