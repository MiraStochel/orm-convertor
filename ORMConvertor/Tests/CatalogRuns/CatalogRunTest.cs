using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Tests.Combined;

namespace Tests.CatalogRuns;

/// <summary>
/// What holds of the on-demand run of a catalog at every build, without a database (decisions
/// 149 and 208): how the run reads its configuration, when it measures, how it converts the
/// values of a catalog into the arguments of a generated method, and that the Java suite's run
/// measures by the same policy. The runs themselves are <c>FibenJudge/FibenRunTest</c> and
/// <c>SedeJudge/SedeRunTest</c>.
/// </summary>
public class CatalogRunTest
{
    public static TheoryData<string> Catalogs() => [.. new[] { CatalogDatabase.Fiben, CatalogDatabase.Sede }.Select(database => database.Name)];

    private static CatalogDatabase Of(string name) => name == "FIBEN" ? CatalogDatabase.Fiben : CatalogDatabase.Sede;

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void AMissingDatabaseSkipsUnlessTheEnvironmentPromisedIt(string catalog)
    {
        var database = Of(catalog);

        Assert.Null(database.FailureOf("no database", required: false));

        var failure = database.FailureOf("no database", required: true);
        Assert.NotNull(failure);
        Assert.Contains(database.RequireEnvironmentVariable, failure);
        Assert.Contains(database.DatabaseName, failure);
        Assert.Contains("no database", failure);
    }

    /// <summary>The settings of the two runs differ by the prefix alone, so that the commands of one are the commands of the other.</summary>
    [Fact]
    public void TheTwoRunsAreConfiguredAlike()
    {
        Assert.Equal("ORMCONVERTOR_REQUIRE_FIBEN_DATABASE", CatalogDatabase.Fiben.RequireEnvironmentVariable);
        Assert.Equal("ORMCONVERTOR_SEDE_QUERIES", CatalogDatabase.Sede.QueriesEnvironmentVariable);
        Assert.Equal("ORMCONVERTOR_SEDE_RESULTS", CatalogDatabase.Sede.ResultsEnvironmentVariable);
        Assert.Equal("ORMCONVERTOR_FIBEN_MEASURE", CatalogDatabase.Fiben.MeasureEnvironmentVariable);
        Assert.Equal("FibenDatabase", CatalogDatabase.Fiben.ConnectionStringName);
        Assert.Equal("SedeDatabase", CatalogDatabase.Sede.ConnectionStringName);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("q1, q2 ,q237", "q1,q2,q237")]
    [InlineData("s1233, S2304", "S2304,s1233")]
    public void TheLimitIsAListOfKeys(string? configured, string? keys)
        => Assert.Equal(keys, CatalogDatabase.ResolveKeys(configured) is { } set ? string.Join(",", set.Order(StringComparer.Ordinal)) : null);

    /// <summary>The connection handed to a cell carries the limit of one execution (decision 146), whatever the configured string said.</summary>
    [Theory]
    [InlineData("Server=db,1433;Database=Fiben;User ID=sa;Password=p;TrustServerCertificate=true;", "Fiben")]
    [InlineData("Server=db,1433;Database=Sede;Command Timeout=30;Trusted_Connection=True;", "Sede")]
    public void TheConnectionHandedToACellCarriesTheTimeLimit(string configured, string database)
    {
        var builder = new SqlConnectionStringBuilder(CatalogDatabase.WithCommandTimeout(configured));

        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.ExecutionTimeoutSeconds, builder.CommandTimeout);
        Assert.Equal(database, builder.InitialCatalog);
    }

    /// <summary>
    /// The run measures only over a server in the measurement mode (decisions 164 and 166):
    /// single-threaded plans on the two processors of its own. A parallel server, the judge's
    /// mode with every processor of the VM, and a server that does not say how many it has leave
    /// the measurement out by themselves, and the environment can leave it out over any server.
    /// </summary>
    [Theory]
    [InlineData(true, 1, 2, null)]
    [InlineData(true, 0, 2, "MAXDOP 0")]
    [InlineData(true, 4, 4, "MAXDOP 4")]
    [InlineData(true, 1, 4, "4 processors")]
    [InlineData(true, 1, null, "unknown number")]
    [InlineData(false, 1, 2, "left out")]
    public void TheRunMeasuresOnlyOverAServerInTheMeasurementMode(bool configured, int maxdop, int? processors, string? reason)
    {
        var leftOut = CatalogServer.MeasurementLeftOut(configured, maxdop, processors);

        if (reason is null)
        {
            Assert.Null(leftOut);
        }
        else
        {
            Assert.Contains(reason, leftOut, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The value of a parameter in the catalog is a text; the run converts it to the type the
    /// generated method declares, and finds the parameter by its name without case and
    /// underscores - the catalog's UserId or p_UserId is the method's userId or pUserId.
    /// </summary>
    [Theory]
    [InlineData(typeof(int), "1192", 1192)]
    [InlineData(typeof(int?), "7", 7)]
    [InlineData(typeof(long), "4398046513938", 4398046513938L)]
    [InlineData(typeof(string), "sql-server", "sql-server")]
    [InlineData(typeof(double), "0.5", 0.5)]
    [InlineData(typeof(bool), "1", true)]
    public void AValueOfTheCatalogBecomesTheArgumentTheMethodDeclares(Type type, string text, object expected)
        => Assert.Equal(expected, CatalogRun.Argument(type, text));

    [Fact]
    public void AMomentOfTheCatalogBecomesADateTime()
        => Assert.Equal(new DateTime(2015, 1, 1), CatalogRun.Argument(typeof(DateTime?), "2015-01-01"));

    [Fact]
    public void TheArgumentsFollowTheParametersOfTheMethod()
    {
        var query = new CatalogRunQuery("s1", "Test", ["Id"], [], () => [], "SELECT Id FROM Users", [("UserId", "INT", "1192"), ("p_Tag", "NVARCHAR(MAX)", "sql-server")], true);
        var method = typeof(CatalogRunTest).GetMethod(nameof(Sample), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.Equal(new object?[] { "sql-server", 1192 }, CatalogRun.ArgumentsFor(method.GetParameters(), query));

        var missing = new CatalogRunQuery("s1", "Test", ["Id"], [], () => [], "SELECT Id FROM Users", [("UserId", "INT", "1192")], true);
        var exception = Assert.Throws<InvalidOperationException>(() => CatalogRun.ArgumentsFor(method.GetParameters(), missing));
        Assert.Contains("pTag", exception.Message);
    }

    private static void Sample(string pTag, int userId)
    {
    }

    /// <summary>
    /// The source is the catalog's text as it stands (decision 209): the values declared in
    /// front of it in the catalog's order, a number as it is written and anything else as a
    /// Unicode string with its quotes doubled, then the text unchanged.
    /// </summary>
    [Fact]
    public void TheSourceIsTheTextWithTheValuesDeclaredInFrontOfIt()
    {
        var query = new CatalogRunQuery("s1", "Test", ["Id"], [], () => [], "SELECT Id FROM Users WHERE Id = @UserId AND Location = @Where",
            [("UserId", "INT", "1192"), ("Where", "NVARCHAR(MAX)", "Prague's"), ("Ratio", "DECIMAL(5, 2)", "0.5")], true);

        Assert.Equal(
            "DECLARE @UserId INT = 1192;\nDECLARE @Where NVARCHAR(MAX) = N'Prague''s';\nDECLARE @Ratio DECIMAL(5, 2) = 0.5;\nSELECT Id FROM Users WHERE Id = @UserId AND Location = @Where",
            CatalogRun.TextBatch(query));
    }

    /// <summary>A query without parameters - every FIBEN query - runs as its text alone.</summary>
    [Fact]
    public void ATextWithoutParametersRunsAlone()
        => Assert.Equal("SELECT 1 AS One", CatalogRun.TextBatch(new CatalogRunQuery("q1", "Simple", ["One"], [], () => [], "SELECT 1 AS One", [], true)));

    /// <summary>The Java run sends the same batch over JDBC: it declares the values the same way and writes the same types unquoted.</summary>
    [Fact]
    public void TheJavaRunDeclaresTheValuesAlike()
    {
        var catalog = Path.Combine(TargetFrameworkDescriptorTest.SolutionDirectory(), "JavaTests", "src", "test", "java", "cz", "stochel", "ormconvertor", "javatests", "catalog");
        var run = File.ReadAllText(Path.Combine(catalog, "CatalogRun.java"));
        var query = File.ReadAllText(Path.Combine(catalog, "CatalogQuery.java"));

        Assert.Contains("batch.append(\"DECLARE @\").append(argument.name()).append(' ').append(argument.sqlType()).append(\" = \").append(argument.literal()).append(\";\\n\");", run);
        Assert.Contains("case \"INT\", \"BIGINT\", \"SMALLINT\", \"TINYINT\", \"FLOAT\", \"REAL\", \"DECIMAL\", \"NUMERIC\", \"BIT\" -> value;", query);
        Assert.Equal("7", CatalogRun.Literal("BIT", "7"));
        Assert.Equal("N'7'", CatalogRun.Literal("NVARCHAR(10)", "7"));
    }

    /// <summary>
    /// The Java run measures with the same policy (decision 145) and the same limit (decision
    /// 146): its constants are read from its source, as the LDBC judge's limit is.
    /// </summary>
    [Fact]
    public void TheJavaRunMeasuresByTheSamePolicy()
    {
        var source = File.ReadAllText(Path.Combine(
            TargetFrameworkDescriptorTest.SolutionDirectory(),
            "JavaTests", "src", "test", "java", "cz", "stochel", "ormconvertor", "javatests", "catalog", "CatalogRun.java"));

        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.WarmupIterations, Declared(source, "WARMUP_ITERATIONS"));
        Assert.Equal(CatalogServer.MeasurementProcessors, Declared(source, "MEASUREMENT_PROCESSORS"));
        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.MinIterations, Declared(source, "MIN_ITERATIONS"));
        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.TargetMilliseconds, Declared(source, "TARGET_MILLISECONDS"));
        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.ExecutionTimeoutSeconds, Declared(source, "QUERY_TIMEOUT_SECONDS"));
    }

    /// <summary>
    /// The processors the run expects of a server in the measurement mode are the ones the
    /// compose file gives both databases by default (decisions 145 and 166).
    /// </summary>
    [Fact]
    public void TheMeasurementProcessorsAreTheDefaultCpusetOfTheDatabases()
    {
        var compose = File.ReadAllText(Path.Combine(TargetFrameworkDescriptorTest.SolutionDirectory(), "docker-compose.yml"));
        var defaults = Regex.Matches(compose, @"cpuset: ""\$\{DATABASE_CPUSET-([0-9,\-]+)\}""").Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(2, defaults.Count);
        Assert.All(defaults, cpuset => Assert.Equal(CatalogServer.MeasurementProcessors, Processors(cpuset)));
    }

    private static int Processors(string cpuset)
        => cpuset.Split(',').Sum(part => part.Split('-') is [var first, var last] ? int.Parse(last, CultureInfo.InvariantCulture) - int.Parse(first, CultureInfo.InvariantCulture) + 1 : 1);

    private static double Declared(string source, string constant)
    {
        var match = Regex.Match(source, constant + @"\s*=\s*([0-9.]+)\s*;");
        Assert.True(match.Success, $"The Java run declares {constant} in CatalogRun.java.");
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
