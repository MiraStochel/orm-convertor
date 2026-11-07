using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Tests.CatalogRuns;

/// <summary>
/// Where the on-demand run of a query catalog takes its database from: the database of the
/// container with the catalog's data loaded and, from the first documented run on, the judge
/// of the catalog beside it - <c>Fiben</c> for the FIBEN catalog (decision 149), <c>Sede</c> for
/// the SEDE catalog (decision 208). The connection is configuration, like the LDBC judge's, and
/// never in the repository (S4); without it every test of the run skips with the reason,
/// because a catalog runs on demand and never as part of a build. One class for both catalogs,
/// because the two runs are one mechanism with two data sets: the names of the settings differ
/// by the prefix alone.
/// </summary>
public sealed class CatalogDatabase
{
    // Before the two instances below: static initializers run in textual order, and the
    // instances read the configuration while they are made.
    private static readonly IConfigurationRoot Configuration =
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(CatalogDatabase).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

    public static CatalogDatabase Fiben { get; } = new("FIBEN", "Fiben", "FibenDatabase", "FIBEN", "database/fiben/load-fiben.sh", "149");

    public static CatalogDatabase Sede { get; } = new("SEDE", "Sede", "SedeDatabase", "SEDE", "database/sede/load-sede.sh", "208");

    /// <summary>The schema of the catalog's tables and of the judge in its database.</summary>
    public const string Schema = "dbo";

    /// <summary>
    /// The time limit of every command of the run in seconds (decision 146): the same limit the
    /// LDBC judge gives a read and the Advisor's policy gives an execution, carried by the
    /// connection string as <c>Command Timeout</c>, so that every framework gets it without
    /// being told. A read over it fails its cell.
    /// </summary>
    public const int CommandTimeoutSeconds = AdvisorBenchmarking.BenchmarkPolicy.ExecutionTimeoutSeconds;

    private CatalogDatabase(string name, string databaseName, string connectionStringName, string prefix, string loader, string decision)
    {
        Name = name;
        DatabaseName = databaseName;
        ConnectionStringName = connectionStringName;
        Prefix = prefix;
        Loader = loader;
        Decision = decision;

        ConnectionString = Configuration.GetConnectionString(connectionStringName) is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? WithCommandTimeout(configured.Trim())
            : null;
        IsRequired = Flag(Environment.GetEnvironmentVariable(RequireEnvironmentVariable));
        Keys = ResolveKeys(Environment.GetEnvironmentVariable(QueriesEnvironmentVariable));
        ResultsDirectory = Environment.GetEnvironmentVariable(ResultsEnvironmentVariable) is { } directory && !string.IsNullOrWhiteSpace(directory) ? directory.Trim() : null;
        Measures = Environment.GetEnvironmentVariable(MeasureEnvironmentVariable) is not { } measure || string.IsNullOrWhiteSpace(measure) || Flag(measure);
    }

    /// <summary>The catalog's name as the records write it: FIBEN, SEDE.</summary>
    public string Name { get; }

    /// <summary>The database of the container: Fiben, Sede.</summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Configuration key of the connection string - user secrets key
    /// <c>ConnectionStrings:&lt;name&gt;</c>, environment variable <c>ConnectionStrings__&lt;name&gt;</c>.
    /// </summary>
    public string ConnectionStringName { get; }

    /// <summary>The middle of the run's environment variables: ORMCONVERTOR_&lt;prefix&gt;_QUERIES and the others.</summary>
    public string Prefix { get; }

    /// <summary>The loader of the container that fills the database.</summary>
    public string Loader { get; }

    /// <summary>The decision the catalog rests on.</summary>
    public string Decision { get; }

    /// <summary>The variable by which an environment states that it provides the database; a missing one then fails instead of skipping.</summary>
    public string RequireEnvironmentVariable => $"ORMCONVERTOR_REQUIRE_{Prefix}_DATABASE";

    /// <summary>A comma-separated list of query keys that limits a run to those queries - a check, never the record of a version.</summary>
    public string QueriesEnvironmentVariable => $"ORMCONVERTOR_{Prefix}_QUERIES";

    /// <summary>The directory the run writes its files into: the cells, the source rows in the form of the judge, and the summary. Without it the run prints its summary only.</summary>
    public string ResultsEnvironmentVariable => $"ORMCONVERTOR_{Prefix}_RESULTS";

    /// <summary>Whether the run measures (decision 145) after it has compared; 0 leaves the measurement out, for a check.</summary>
    public string MeasureEnvironmentVariable => $"ORMCONVERTOR_{Prefix}_MEASURE";

    /// <summary>Connection string with the run's time limit, or <c>null</c> when none is configured.</summary>
    public string? ConnectionString { get; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public bool IsRequired { get; }

    /// <summary>The keys a run is limited to, or null for the whole catalog.</summary>
    public IReadOnlySet<string>? Keys { get; }

    public string? ResultsDirectory { get; }

    public bool Measures { get; }

    public string NotConfiguredReason =>
        $"No {Name} database configured. Set the connection string of {DatabaseName} in user secrets as "
        + $"\"ConnectionStrings:{ConnectionStringName}\" or in the environment variable "
        + $"ConnectionStrings__{ConnectionStringName}, with the data loaded by {Loader} (decision {Decision}).";

    /// <summary>Skips the current test with the reason, or fails with it where the environment promised the database.</summary>
    public void Unavailable(string reason, bool required)
    {
        if (FailureOf(reason, required) is { } failure)
        {
            Assert.Fail(failure);
        }

        Assert.Skip(reason);
    }

    internal string? FailureOf(string reason, bool required)
        => required
            ? $"{RequireEnvironmentVariable} is set, so this environment states that it provides the {DatabaseName} database - "
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

    internal static string WithCommandTimeout(string connectionString)
        => new SqlConnectionStringBuilder(connectionString) { CommandTimeout = CommandTimeoutSeconds }.ConnectionString;
}
