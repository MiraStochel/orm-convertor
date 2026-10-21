using System.Data.Common;
using System.Text.RegularExpressions;

namespace DatabaseCatalog;

/// <summary>
/// The one redaction of decision 160. The second sentence of S4 binds every text the tool
/// gives out about a run - a line of the log, the reason of a record, the detail of a failure
/// response - and some of that text is not the tool's own: the message of an exception thrown
/// by SqlClient, a framework or measured code. Such text leaves only through here. Built from
/// the connection strings a component holds, it replaces every occurrence of what the tool
/// proves itself with - the password, the user name it belongs to, the connection string as a
/// whole and, under integrated authentication, the account of the process, which SQL Server
/// names in its error text just the same - with <see cref="Mask"/>. Measured on 2026-10-21
/// against Microsoft.Data.SqlClient 7.0.2: a refused login names the user, no message carried
/// the password.
/// <para>
/// An occurrence is the exact value, compared without regard to case, with no letter or digit
/// beside it, so that a short user name does not eat into the words around it. The server's
/// text stays, only the credential goes: <c>Login failed for user '***'.</c> What foreign code
/// encodes on its own is beyond any redaction; that is the isolation of the first sentence of
/// S4, not this.
/// </para>
/// </summary>
public sealed class CredentialRedaction
{
    public const string Mask = "***";

    private static readonly string[] CredentialKeys =
        ["password", "pwd", "user id", "userid", "uid", "user", "username", "user name"];

    private static readonly string[] IntegratedKeys = ["integrated security", "trusted_connection"];

    private readonly Regex? pattern;

    private CredentialRedaction(IEnumerable<string> values)
    {
        // Longest first: the whole connection string goes as one, not as the parts it carries.
        var alternatives = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .Select(Regex.Escape)
            .ToList();

        pattern = alternatives.Count == 0
            ? null
            : new Regex(
                $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', alternatives)})(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>A redaction that knows no credential and returns every text unchanged.</summary>
    public static CredentialRedaction None { get; } = new([]);

    /// <summary>
    /// The redaction of the credentials the given connection strings carry; a blank one
    /// carries none. A string the parser refuses still yields itself and every credential
    /// its <c>key=value</c> segments state.
    /// </summary>
    public static CredentialRedaction For(IEnumerable<string?> connectionStrings)
    {
        var values = new List<string>();

        foreach (var connectionString in connectionStrings)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                continue;
            }

            values.Add(connectionString.Trim());
            var pairs = Parse(connectionString);

            foreach (var key in CredentialKeys)
            {
                if (pairs.TryGetValue(key, out var value))
                {
                    values.Add(value.Trim());
                }
            }

            if (IntegratedKeys.Any(key => pairs.TryGetValue(key, out var value) && IsOn(value)))
            {
                values.Add($@"{Environment.UserDomainName}\{Environment.UserName}");
            }
        }

        return new CredentialRedaction(values);
    }

    public string Redact(string text) =>
        pattern is null || string.IsNullOrEmpty(text) ? text : pattern.Replace(text, Mask);

    private static Dictionary<string, string> Parse(string connectionString)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // The general builder rather than SqlConnectionStringBuilder: it takes a keyword
            // SqlClient does not know, so a string carrying one still gives up its password.
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (string key in builder.Keys)
            {
                pairs[key] = Convert.ToString(builder[key]) ?? string.Empty;
            }
        }
        catch (ArgumentException)
        {
            foreach (var segment in connectionString.Split(';'))
            {
                var parts = segment.Split('=', 2);
                if (parts.Length == 2)
                {
                    pairs[parts[0].Trim()] = parts[1];
                }
            }
        }

        return pairs;
    }

    private static bool IsOn(string value) =>
        value.Trim().ToLowerInvariant() is "true" or "yes" or "sspi";
}
