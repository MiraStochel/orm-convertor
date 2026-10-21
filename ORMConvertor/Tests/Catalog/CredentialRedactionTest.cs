using DatabaseCatalog;

namespace Tests.Catalog;

/// <summary>
/// The one redaction of decision 160, on the texts SqlClient wrote in the measurement the
/// decision cites and on the shapes a connection string takes. The texts the tool sends out
/// through it - the log, the reason of a record, the detail of a failure response - have tests
/// of their own; this one holds what counts as an occurrence.
/// </summary>
public class CredentialRedactionTest
{
    private const string User = "s4probe_user";
    private const string Secret = "S4probe-Secret-7f3a";
    private const string ConnectionString =
        $"Server=tcp:127.0.0.1,1444;Database=WideWorldImporters;User ID={User};Password={Secret};TrustServerCertificate=True";

    private static readonly CredentialRedaction Redaction = CredentialRedaction.For([ConnectionString]);

    [Fact]
    public void TheServerTextStaysAndTheLoginGoes()
    {
        Assert.Equal(
            "The catalog could not be read (Login failed for user '***'.)",
            Redaction.Redact($"The catalog could not be read (Login failed for user '{User}'.)"));
    }

    [Fact]
    public void TheWholeConnectionStringGoesAsOne()
    {
        Assert.Equal("opened ***.", Redaction.Redact($"opened {ConnectionString}."));
    }

    [Fact]
    public void ThePasswordGoesWhereverItStands()
    {
        Assert.Equal("Password=***; and '***'", Redaction.Redact($"Password={Secret}; and '{Secret}'"));
    }

    [Fact]
    public void AnOccurrenceIsComparedWithoutRegardToCase()
    {
        Assert.Equal("user '***'", Redaction.Redact($"user '{User.ToUpperInvariant()}'"));
    }

    /// <summary>A short user name does not eat into the words around it.</summary>
    [Fact]
    public void AValueNextToALetterOrDigitIsNotAnOccurrence()
    {
        var redaction = CredentialRedaction.For(["Server=x;User ID=sa;Password=pw-0"]);

        Assert.Equal("usage of *** by sa1", redaction.Redact("usage of sa by sa1"));
    }

    /// <summary>
    /// A keyword SqlClient does not know makes its own builder refuse the string; the general
    /// builder still takes it, so the password is still known.
    /// </summary>
    [Fact]
    public void AStringWithAnUnknownKeywordStillGivesUpItsCredentials()
    {
        var redaction = CredentialRedaction.For([$"Server=x;User ID={User};Password={Secret};Bogus Key=1"]);

        Assert.Equal("*** / ***", redaction.Redact($"{User} / {Secret}"));
    }

    [Fact]
    public void AStringNoBuilderTakesStillGivesUpWhatItsSegmentsState()
    {
        var malformed = $"Server=x;User ID={User};Password={Secret};orphan;";
        var redaction = CredentialRedaction.For([malformed]);

        Assert.Equal("*** / *** / ***", redaction.Redact($"{User} / {Secret} / {malformed}"));
    }

    /// <summary>
    /// Under integrated authentication the string carries no credential, but SQL Server names
    /// the account of the process in its error text, as it names the user of a SQL login.
    /// </summary>
    [Fact]
    public void IntegratedAuthenticationHidesTheAccountOfTheProcess()
    {
        var redaction = CredentialRedaction.For(["Server=(localdb)\\MSSQLLocalDB;Database=X;Integrated Security=True"]);
        var account = $@"{Environment.UserDomainName}\{Environment.UserName}";

        Assert.Equal(
            "Login failed for user '***'.",
            redaction.Redact($"Login failed for user '{account}'."));
    }

    [Fact]
    public void WithoutAConnectionNothingChanges()
    {
        const string text = $"Login failed for user '{User}'.";

        Assert.Equal(text, CredentialRedaction.None.Redact(text));
        Assert.Equal(text, CredentialRedaction.For([null, " "]).Redact(text));
    }
}
