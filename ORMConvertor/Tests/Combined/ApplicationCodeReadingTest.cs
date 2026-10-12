using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// What the reading takes out of a C# file that is mostly application code (decisions 109,
/// 111 and 114). A root known only by its position - <c>x.M</c>, where the unit says nothing
/// of what <c>x</c> is - begins a query only in a chain with a LINQ step or a step of the
/// provider's query API: <c>Random.Shared.Next()</c>, <c>span.TotalHours.ToString()</c>,
/// <c>scope.ServiceProvider.GetRequiredService&lt;T&gt;()</c> and <c>x.Ledgers.Add(…)</c>
/// have the position and no query in them, and read as queries they claimed tables named
/// after the member. A computed property - no setter, a getter that is code - is behaviour
/// no .NET framework maps, and is no column.
/// </summary>
public class ApplicationCodeReadingTest
{
    private const string Domain = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Books;

        [Table("Ledgers")]
        public class Ledger
        {
            [Key]
            public int LedgerId { get; set; }

            public decimal Amount { get; set; }

            public bool IsOpen => Amount > 0;

            public string Label
            {
                get { return "L" + LedgerId; }
            }
        }
        """;

    private const string Code = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        namespace Books;

        public static class Formatting
        {
            public static string Hours(TimeSpan span) => span.TotalHours.ToString("0.0");
        }

        public class LedgerService(LedgerContext db, IServiceScopeFactory scopes)
        {
            public List<Ledger> Large(decimal minimum)
            {
                var code = Random.Shared.Next(1, 9);
                using var scope = scopes.CreateScope();
                var other = scope.ServiceProvider.GetRequiredService<LedgerContext>();
                other.Ledgers.Add(new Ledger { Amount = code });

                return db.Ledgers.Where(l => l.Amount >= minimum).ToList();
            }

            public IQueryable<Ledger> Untracked() => db.Ledgers.AsNoTracking();
        }
        """;

    private static ConversionResult Convert(ORMEnum target) => ConversionHandler.Convert(ORMEnum.EFCore, target,
    [
        new() { ContentType = ConversionContentType.CSharp, Content = Domain, Name = "Domain.cs" },
        new() { ContentType = ConversionContentType.CSharp, Content = Code, Name = "LedgerService.cs" },
    ]);

    /// <summary>The two chains over the context's set are the queries of the unit, and nothing else in it is.</summary>
    [Fact]
    public void OnlyChainsWithAQueryStepAreQueries()
    {
        var result = Convert(ORMEnum.Dapper);

        Assert.Equal(2, result.Sources.Count(s => s.ContentType == ConversionContentType.SqlQuery));
        Assert.All(
            result.Sources.Where(s => s.ContentType == ConversionContentType.SqlQuery),
            sql => Assert.Contains("FROM Ledgers AS l", sql.Content, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("'Shared'", StringComparison.Ordinal)
            || r.Reason.Contains("'TotalHours'", StringComparison.Ordinal)
            || r.Reason.Contains("'ServiceProvider'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        // The static helper is excluded as static, not as code that hands a query over.
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Convention
            && r.Reason.Contains("'Formatting' is static", StringComparison.Ordinal));
    }

    /// <summary>The computed properties stay in the source; the entity carries the two that hold a value.</summary>
    [Fact]
    public void AComputedPropertyIsNoColumn()
    {
        var result = Convert(ORMEnum.NHibernate);

        var mapping = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.XML && s.Content.Contains("<class name=\"Ledger\"", StringComparison.Ordinal)).Content;
        var entity = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity).Content;

        Assert.Contains("name=\"Amount\"", mapping, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOpen", mapping + entity, StringComparison.Ordinal);
        Assert.DoesNotContain("Label", mapping + entity, StringComparison.Ordinal);
    }
}
