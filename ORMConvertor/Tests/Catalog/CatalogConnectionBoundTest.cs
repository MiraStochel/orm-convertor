using DatabaseCatalog;
using Microsoft.Data.SqlClient;

namespace Tests.Catalog;

/// <summary>
/// The catalog connection waits a bounded time for a server that does not answer (decision
/// 197): the reader states <c>Connect Timeout</c> and no connect retry where the operator's
/// string does not, and leaves alone whatever the string states itself.
/// </summary>
public class CatalogConnectionBoundTest
{
    private const string Bare = "Server=tcp:10.255.255.1,1433;Database=Catalog;User ID=reader;Password=secret;TrustServerCertificate=True";

    private static SqlConnectionStringBuilder Bounded(string connectionString)
        => new(SqlServerCatalogReader.BoundedConnection(connectionString));

    [Fact]
    public void AStringThatSaysNothingGetsTheBoundAndNoRetry()
    {
        var bounded = Bounded(Bare);

        Assert.Equal(SqlServerCatalogReader.DefaultConnectTimeoutSeconds, bounded.ConnectTimeout);
        Assert.Equal(0, bounded.ConnectRetryCount);

        // Everything else is as the operator wrote it.
        Assert.Equal("tcp:10.255.255.1,1433", bounded.DataSource);
        Assert.Equal("Catalog", bounded.InitialCatalog);
        Assert.Equal("reader", bounded.UserID);
        Assert.Equal("secret", bounded.Password);
        Assert.True(bounded.TrustServerCertificate);
    }

    [Theory]
    [InlineData("Connect Timeout=30")]
    [InlineData("Connection Timeout=30")]
    [InlineData("Timeout=30")]
    public void AStatedTimeoutWinsUnderAnyOfItsSpellings(string keyword)
        => Assert.Equal(30, Bounded(Bare + ";" + keyword).ConnectTimeout);

    [Theory]
    [InlineData("Connect Retry Count=2")]
    [InlineData("ConnectRetryCount=2")]
    public void AStatedRetryCountWins(string keyword)
        => Assert.Equal(2, Bounded(Bare + ";" + keyword).ConnectRetryCount);

    [Fact]
    public void AStringSqlClientCannotParseIsLeftAsItIs()
    {
        const string broken = "this is not a connection string ;;= =";

        Assert.Equal(broken, SqlServerCatalogReader.BoundedConnection(broken));
    }
}
