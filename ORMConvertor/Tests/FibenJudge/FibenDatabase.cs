using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Tests.FibenJudge;

/// <summary>
/// Where the on-demand run of the FIBEN catalog takes its database from (decision 149): the
/// Fiben database of the container with the benchmark loaded and, from the first documented
/// run on, the judge of the catalog beside it. The connection is configuration, like the LDBC
/// judge's, and never in the repository (S4); without it every test of the run skips with the
/// reason, because the catalog runs on demand and never as part of a build.
/// </summary>
public static class FibenDatabase
{
    /// <summary>
    /// Configuration key of the connection string - user secrets key
    /// <c>ConnectionStrings:FibenDatabase</c>, environment variable
    /// <c>ConnectionStrings__FibenDatabase</c>.
    /// </summary>
    public const string ConnectionStringName = "FibenDatabase";

    /// <summary>The schema of the FIBEN tables and of the judge in the Fiben database.</summary>
    public const string Schema = "dbo";

    /// <summary>
    /// The time limit of every command of the run in seconds (decision 146): the same limit the
    /// LDBC judge gives a read and the Advisor's policy gives an execution, carried by the
    /// connection string as <c>Command Timeout</c>, so that every framework gets it without
    /// being told. A read over it fails its cell.
    /// </summary>
    public const int CommandTimeoutSeconds = AdvisorBenchmarking.BenchmarkPolicy.ExecutionTimeoutSeconds;

    /// <summary>The variable by which an environment states that it provides the database; a missing one then fails instead of skipping.</summary>
    private const string RequireEnvironmentVariable = "ORMCONVERTOR_REQUIRE_FIBEN_DATABASE";

    /// <summary>A comma-separated list of query keys that limits a run to those queries - a check, never the record of a version.</summary>
    private const string QueriesEnvironmentVariable = "ORMCONVERTOR_FIBEN_QUERIES";

    /// <summary>The directory the run writes its files into: the cells, the source rows in the form of the judge, and the summary. Without it the run prints its summary only.</summary>
    private const string ResultsEnvironmentVariable = "ORMCONVERTOR_FIBEN_RESULTS";

    /// <summary>Whether the run measures (decision 145) after it has compared; 0 leaves the measurement out, for a check.</summary>
    private const string MeasureEnvironmentVariable = "ORMCONVERTOR_FIBEN_MEASURE";

    private static readonly IConfigurationRoot Configuration =
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(FibenDatabase).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>Connection string with the run's time limit, or <c>null</c> when none is configured.</summary>
    public static string? ConnectionString { get; } = ResolveConnectionString();

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public static bool IsRequired { get; } = Flag(Environment.GetEnvironmentVariable(RequireEnvironmentVariable));

    /// <summary>The keys a run is limited to, or null for the whole catalog.</summary>
    public static IReadOnlySet<string>? Keys { get; } = ResolveKeys(Environment.GetEnvironmentVariable(QueriesEnvironmentVariable));

    public static string? ResultsDirectory { get; } = Environment.GetEnvironmentVariable(ResultsEnvironmentVariable) is { } directory && !string.IsNullOrWhiteSpace(directory) ? directory.Trim() : null;

    public static bool Measures { get; } = Environment.GetEnvironmentVariable(MeasureEnvironmentVariable) is not { } measure || string.IsNullOrWhiteSpace(measure) || Flag(measure);

    public static string NotConfiguredReason =>
        $"No FIBEN database configured. Set the connection string of Fiben in user secrets as "
        + $"\"ConnectionStrings:{ConnectionStringName}\" or in the environment variable "
        + $"ConnectionStrings__{ConnectionStringName}, with the benchmark loaded by database/fiben/load-fiben.sh (decision 149).";

    /// <summary>Skips the current test with the reason, or fails with it where the environment promised the database.</summary>
    public static void Unavailable(string reason, bool required)
    {
        if (FailureOf(reason, required) is { } failure)
        {
            Assert.Fail(failure);
        }

        Assert.Skip(reason);
    }

    internal static string? FailureOf(string reason, bool required)
        => required
            ? $"{RequireEnvironmentVariable} is set, so this environment states that it provides the Fiben database - "
              + $"a skipped run here would claim a result it does not have. {reason}"
            : null;

    internal static IReadOnlySet<string>? ResolveKeys(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var keys = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return keys.Count == 0 ? null : keys;
    }

    private static bool Flag(string? configured)
        => configured?.Trim() is { } value
            && (value.Equals("1", StringComparison.Ordinal) || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static string? ResolveConnectionString()
    {
        var configured = Configuration.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(configured) ? null : WithCommandTimeout(configured.Trim());
    }

    internal static string WithCommandTimeout(string connectionString)
        => new SqlConnectionStringBuilder(connectionString) { CommandTimeout = CommandTimeoutSeconds }.ConnectionString;
}
