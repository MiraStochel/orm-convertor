using System.Text.RegularExpressions;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of TrimEnd, measured by its own query pipeline without a connection
/// (the third level of decision 027) - the facts the LINQ spelling of <c>TrimEnd</c> rests on
/// (decision 169): <c>TrimEnd()</c> without an argument is RTRIM, over a column and over its
/// maximum alike, while <c>TrimEnd(' ')</c> leaves the column bare in the SQL and trims on
/// the client, which is why the writer passes no argument.
/// </summary>
public class EFCoreTrimEndProviderTest
{
    private const string Entities = """
        namespace Fixed;

        public class Flag
        {
            public int Id { get; set; }
            public string? Code { get; set; }
        }
        """;

    [Theory]
    [InlineData("ctx.Set<Flag>().Select(f => new { Code = f.Code.TrimEnd() })", "SELECT RTRIM([f].[Code]) AS [Code] FROM [Flag] AS [f]")]
    [InlineData("ctx.Set<Flag>().GroupBy(f => 1).Select(g => g.Max(e => e.Code).TrimEnd())", "SELECT RTRIM(MAX([f0].[Code])) FROM")]
    public void TheProviderTranslatesTrimEndAsRtrim(string chain, string sql)
    {
        Assert.StartsWith(sql, Translate(chain), StringComparison.Ordinal);
    }

    [Fact]
    public void TrimEndWithACharacterIsLeftToTheClient()
    {
        Assert.Equal("SELECT [f].[Code] FROM [Flag] AS [f]", Translate("ctx.Set<Flag>().Select(f => f.Code.TrimEnd(' '))"));
    }

    private static string Translate(string chain)
    {
        var method = $$"""
            public static IQueryable Query(DbContext ctx)
            {
                return {{chain}};
            }
            """;

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "EFCoreTrimEnd_" + string.Concat(chain.Where(char.IsLetterOrDigit)),
            method,
            [Entities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Fixed;");

        return Regex.Replace(EFCoreQueryAcceptance.Translate(compiled), @"\s+", " ").Trim();
    }
}
