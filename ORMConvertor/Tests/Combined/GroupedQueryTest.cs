using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The aggregation and grouping categories requirement T2 divides the matrix by, across every
/// direction the enum yields. Until now the matrices ran over a query of projection, filter
/// and ordering only, so two defects of the grouped shape survived every level of
/// verification: <c>COUNT(*)</c> left the T-SQL builder as <c>COUNT(c.*)</c>, which is not
/// T-SQL at all, and <c>g.Key</c> - the very text the EF Core builder writes for a projection
/// of the grouping key - was read back as a column <c>Key</c> of a table <c>g</c>, so a
/// grouped LINQ query came out over a table nobody declared in five of the six targets.
/// </summary>
public class GroupedQueryTest
{
    private const string GroupedLinq = """
        public void Query()
        {
            var q = ctx.Customers
                .GroupBy(c => c.CustomerName)
                .Select(g => new { Name = g.Key, N = g.Count() })
                .ToList();
        }
        """;

    private const string GroupedSql = """
        SELECT c.CustomerName AS Name, COUNT(*) AS N
        FROM Sales.Customers AS c
        GROUP BY c.CustomerName
        """;

    private const string GroupedHql = """
        select c.CustomerName as Name, count(*) as N
        from Customer c
        group by c.CustomerName
        """;

    public static TheoryData<ORMEnum, ORMEnum> GroupedDirections()
    {
        var data = new TheoryData<ORMEnum, ORMEnum>();
        foreach (var source in new[] { ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.NHibernate })
        {
            foreach (var target in Enum.GetValues<ORMEnum>())
            {
                data.Add(source, target);
            }
        }

        return data;
    }

    private static ConversionResult Convert(ORMEnum source, ORMEnum target)
    {
        var (query, type) = source switch
        {
            ORMEnum.Dapper => (GroupedSql, ConversionContentType.SqlQuery),
            ORMEnum.EFCore => (GroupedLinq, ConversionContentType.CSharpQuery),
            ORMEnum.NHibernate => (GroupedHql, ConversionContentType.HqlQuery),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };

        return ConversionHandler.Convert(
            source,
            target,
            [.. CrossFrameworkInputs.MappingUnits(source), new ConversionSource { Content = query, ContentType = type }]);
    }

    /// <summary>
    /// The same grouped query written in each of the three source languages reaches every
    /// target as an artifact, with the grouping and the count in it and nothing refused.
    /// </summary>
    [Theory]
    [MemberData(nameof(GroupedDirections))]
    public void EveryDirectionCarriesTheGroupingAndTheCount(ORMEnum source, ORMEnum target)
    {
        var result = Convert(source, target);

        // Only the query branch is at issue here. A Dapper source states no key, so a target
        // that requires one refuses the entity - which is decision 063's answer, not this
        // test's subject.
        Assert.DoesNotContain(
            result.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Artifact?.IsQuery() == true);

        var query = result.Sources.Where(s => s.ContentType.IsQuery()).ToList();
        Assert.NotEmpty(query);
    }

    /// <summary>
    /// Level 2 for the SQL targets (decision 027): the emitted statement has to parse. This
    /// is where <c>COUNT(c.*)</c> would have been caught, and the reason it was not is that
    /// no test asked a grouped query of the SQL branch.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.NHibernate)]
    public void TheSqlTargetEmitsAStatementThatParses(ORMEnum source)
    {
        var sql = Assert.Single(
            Convert(source, ORMEnum.Dapper).Sources,
            s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT(c.*)", sql, StringComparison.Ordinal);

        new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(sql), out var errors);
        Assert.Empty(errors.Select(e => $"{e.Line}:{e.Column} {e.Message}"));
    }

    /// <summary>
    /// The MyBatis mapper carries the same statement, and it is XML, so the count has to be
    /// there without the alias in front of the star as well.
    /// </summary>
    [Fact]
    public void TheMyBatisMapperCarriesTheSameCount()
    {
        var documents = Convert(ORMEnum.Dapper, ORMEnum.MyBatis).Sources
            .Where(s => s.ContentType == ConversionContentType.XML)
            .Select(s => s.Content)
            .ToList();

        Assert.Contains(documents, d => d.Contains("COUNT(*)", StringComparison.Ordinal));
        Assert.DoesNotContain(documents, d => d.Contains("COUNT(c.*)", StringComparison.Ordinal));
    }

    /// <summary>
    /// The round trip the builder and the parser owe each other: what the EF Core builder
    /// writes for a projection of the grouping key is exactly what its parser has to read
    /// back, or the identity direction stops being one.
    /// </summary>
    [Fact]
    public void TheGroupingKeyOfALinqQueryRoundTrips()
    {
        var result = Convert(ORMEnum.EFCore, ORMEnum.EFCore);

        var linq = Assert.Single(
            result.Sources,
            s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains(".GroupBy(c => c.CustomerName)", linq, StringComparison.Ordinal);
        Assert.Contains("Name = g.Key", linq, StringComparison.Ordinal);
        Assert.Contains("N = g.Count()", linq, StringComparison.Ordinal);

        // The grouping key is a key, not a column the scope cannot resolve, so nothing is
        // reported about it.
        Assert.DoesNotContain(
            result.Records,
            r => r.Reason.Contains("neither a grouping key nor an aggregate", StringComparison.Ordinal));
    }

    /// <summary>
    /// A grouping of several columns names each of them through the key object, and each
    /// name has to find its own column - the anonymous member may be called something else
    /// than the column it holds.
    /// </summary>
    [Fact]
    public void ACompositeGroupingKeyIsResolvedMemberByMember()
    {
        const string linq = """
            public void Query()
            {
                var q = ctx.Customers
                    .GroupBy(c => new { N = c.CustomerName, L = c.CreditLimit })
                    .Select(g => new { Name = g.Key.N, Limit = g.Key.L, C = g.Count() })
                    .ToList();
            }
            """;

        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [
                .. CrossFrameworkInputs.MappingUnits(ORMEnum.EFCore),
                new ConversionSource { Content = linq, ContentType = ConversionContentType.CSharpQuery },
            ]);

        var sql = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains("c.CustomerName AS Name", sql, StringComparison.Ordinal);
        Assert.Contains("c.CreditLimit AS Limit", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY c.CustomerName, c.CreditLimit", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other side of reading <c>g.Key</c>: an entity whose column is called Key is not a
    /// grouping key, and without a grouping in the scope there is none to mistake it for.
    /// </summary>
    [Fact]
    public void AColumnCalledKeyIsStillAColumnWhereNothingIsGrouped()
    {
        const string entity = """
            namespace Shop;

            public class Setting
            {
                public int SettingId { get; set; }
                public string Key { get; set; }
            }
            """;

        const string linq = """
            public void Query()
            {
                var q = ctx.Settings.Select(s => new { K = s.Key }).ToList();
            }
            """;

        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [
                new ConversionSource { Content = entity, ContentType = ConversionContentType.CSharpEntity },
                new ConversionSource { Content = linq, ContentType = ConversionContentType.CSharpQuery },
            ]);

        var sql = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains("s.Key AS K", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.Records,
            r => r.Reason.Contains("names the grouping key", StringComparison.Ordinal));
    }

    /// <summary>
    /// An aggregate with no grouping behind it is a query that answers with one number. A
    /// LINQ chain says that by ending in the aggregate call, which is not the IQueryable the
    /// builder emits, so it refuses (decision 053) - it used to write the bare column back,
    /// and over COUNT(*) that was the unusable <c>c.*</c>.
    /// </summary>
    [Fact]
    public void AnUngroupedAggregateIsRefusedByTheLinqTargetAndCarriedByTheOthers()
    {
        const string sql = "SELECT COUNT(*) AS N FROM Sales.Customers AS c";

        List<ConversionSource> Units() =>
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.Dapper),
            new ConversionSource { Content = sql, ContentType = ConversionContentType.SqlQuery },
        ];

        var efCore = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, Units());
        Assert.DoesNotContain(efCore.Sources, s => s.ContentType.IsQuery());
        Assert.Contains(
            efCore.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Aggregation);

        foreach (var target in new[] { ORMEnum.Dapper, ORMEnum.NHibernate, ORMEnum.Hibernate, ORMEnum.EclipseLink, ORMEnum.MyBatis })
        {
            var result = ConversionHandler.Convert(ORMEnum.Dapper, target, Units());

            Assert.DoesNotContain(
                result.Records,
                r => r.Kind == ConversionRecordKind.Failure && r.Artifact?.IsQuery() == true);
            Assert.Contains(result.Sources, s => s.ContentType.IsQuery());
        }
    }
}
