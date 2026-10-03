using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Tests.LdbcJudge;

/// <summary>
/// Where the judge of the LDBC catalog takes its database from (decision 117): LdbcSnb with the
/// data set loaded and the validation set beside it - which the test database of decision 016
/// does not have and is not meant to have. The connection is configuration, like the test
/// database's, and never in the repository (S4); without it the tests of the judge skip with
/// the reason, because the tool translates without any database and so does most of the suite.
/// </summary>
public static class LdbcDatabase
{
    /// <summary>
    /// Configuration key of the connection string - user secrets key
    /// <c>ConnectionStrings:LdbcDatabase</c>, environment variable
    /// <c>ConnectionStrings__LdbcDatabase</c>.
    /// </summary>
    public const string ConnectionStringName = "LdbcDatabase";

    /// <summary>
    /// The schema of the LDBC tables in LdbcSnb, wherever it is loaded: the loader substitutes
    /// dbo for the placeholder of its scripts, and so do the suites for the scripts of the inserts.
    /// </summary>
    public const string Schema = "dbo";

    /// <summary>
    /// The variable by which an environment states that it provides the judge. It is not the
    /// variable of the test database: that one promises a database, not a data set (decision 117).
    /// </summary>
    private const string RequireEnvironmentVariable = "ORMCONVERTOR_REQUIRE_LDBC_DATABASE";

    /// <summary>
    /// How many lines of the validation set a run replays. The state after the first n lines is
    /// consistent, so a prefix is a smaller judge of its own; without the variable the whole set
    /// is replayed.
    /// </summary>
    private const string RowsEnvironmentVariable = "ORMCONVERTOR_LDBC_VALIDATION_ROWS";

    private static readonly IConfigurationRoot Configuration =
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(LdbcDatabase).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>Connection string, or <c>null</c> when none is configured.</summary>
    public static string? ConnectionString { get; } = ResolveConnectionString();

    /// <summary>Whether a connection string was found at all.</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>Whether the environment promised the judge; where it did, a missing one fails instead of skipping.</summary>
    public static bool IsRequired { get; } = Flag(Environment.GetEnvironmentVariable(RequireEnvironmentVariable));

    /// <summary>The number of lines a run replays, or <c>null</c> for the whole set.</summary>
    public static int? Rows { get; } = ResolveRows(Environment.GetEnvironmentVariable(RowsEnvironmentVariable));

    public static string NotConfiguredReason =>
        $"No LDBC database configured. Set the connection string of LdbcSnb in user secrets as "
        + $"\"ConnectionStrings:{ConnectionStringName}\" or in the environment variable "
        + $"ConnectionStrings__{ConnectionStringName} (the compose profile \"test\" does it), with the data "
        + "set and the validation set loaded by database/ldbc/load-ldbc.sh or by validation.sql (decision 117).";

    /// <summary>
    /// Skips the current test with the reason - or fails with it where the environment promised
    /// the judge, because a run in which the judge skipped would claim a verdict it does not have
    /// (decision 039).
    /// </summary>
    public static void Unavailable(string reason, bool required)
    {
        if (FailureOf(reason, required) is { } failure)
        {
            Assert.Fail(failure);
        }

        Assert.Skip(reason);
    }

    /// <summary>What a missing judge fails with, or null where it only skips with its reason.</summary>
    internal static string? FailureOf(string reason, bool required)
        => required
            ? $"{RequireEnvironmentVariable} is set, so this environment states that it provides LdbcSnb with "
              + $"the validation set - a skipped judge here would claim a verdict it does not have. {reason}"
            : null;

    /// <summary>The prefix as the variable states it: a positive number of lines, or nothing.</summary>
    internal static int? ResolveRows(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (!int.TryParse(configured.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var rows) || rows <= 0)
        {
            throw new InvalidOperationException(
                $"{RowsEnvironmentVariable} must be a positive number of lines, but was \"{configured}\".");
        }

        return rows;
    }

    private static bool Flag(string? configured)
        => configured?.Trim() is { } value
            && (value.Equals("1", StringComparison.Ordinal) || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static string? ResolveConnectionString()
    {
        var configured = Configuration.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }
}
