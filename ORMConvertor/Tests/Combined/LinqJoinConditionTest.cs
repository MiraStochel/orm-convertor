using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A join condition beyond equalities of key columns in LINQ (decision 113). Until it, the EF
/// Core target refused every join whose condition was not a conjunction of column
/// equalities, although LINQ says such a join: a conjunct that names the joined row alone is
/// a filter of the joined sequence - exact under an inner and a left join -, and a condition
/// that names both rows otherwise is the correlated SelectMany, with DefaultIfEmpty() for a
/// left join. A right or a full join with such a condition has no LINQ form and goes to
/// native SQL. The shared LINQ reader reads both shapes back into the condition, so the
/// identity direction holds and EF Core is a source of them too. The categories of T2 carry
/// the shapes through every direction and the fourth level (<c>QueryShapeMatrixTest</c>, the
/// differential matrix); this class names the forms and the boundaries.
/// </summary>
public class LinqJoinConditionTest
{
    private static List<EntityMap> Maps()
    {
        var customerId = new Property { Name = "CustomerId", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "Name", Type = LangType.Scalar(ScalarType.String) };
        var creditLimit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal) };

        var customers = new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [customerId, name, creditLimit] },
            Table = "Customers",
            PropertyMaps =
            [
                new PropertyMap { Property = customerId, ColumnName = "CustomerId" },
                new PropertyMap { Property = name, ColumnName = "Name" },
                new PropertyMap { Property = creditLimit, ColumnName = "CreditLimit" },
            ],
        };

        var orderId = new Property { Name = "OrderId", Type = LangType.Scalar(ScalarType.Int) };
        var orderCustomer = new Property { Name = "CustomerId", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };
        var note = new Property { Name = "Note", Type = LangType.Scalar(ScalarType.String, isNullable: true) };

        var orders = new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [orderId, orderCustomer, total, note] },
            Table = "Orders",
            PropertyMaps =
            [
                new PropertyMap { Property = orderId, ColumnName = "OrderId" },
                new PropertyMap { Property = orderCustomer, ColumnName = "CustomerId" },
                new PropertyMap { Property = total, ColumnName = "Total" },
                new PropertyMap { Property = note, ColumnName = "Note" },
            ],
        };

        return [customers, orders];
    }

    private const string Entities = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace JoinConditionDomain;

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }

            public string Name { get; set; } = "";

            public decimal CreditLimit { get; set; }
        }

        [Table("Orders")]
        public class Order
        {
            [Key]
            public int OrderId { get; set; }

            public int CustomerId { get; set; }

            public decimal Total { get; set; }

            public string? Note { get; set; }
        }
        """;

    private static AbstractQueryBuilder Sql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = Maps();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, Maps());
        return builder;
    }

    private static AbstractQueryBuilder Linq(AbstractQueryBuilder builder, string query)
    {
        builder.EntityMaps = Maps();
        var source = $$"""
            public void Query()
            {
                var q = ({{query}}).ToList();
            }
            """;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, Maps());
        return builder;
    }

    private static List<ConversionSource> Built(AbstractQueryBuilder builder)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        return built;
    }

    /// <summary>The query in the target's own language: no record of a failure, a fallback or a loss.</summary>
    private static string Text(AbstractQueryBuilder builder, ConversionContentType contentType)
    {
        var built = Built(builder);
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback or ConversionRecordKind.Loss);
        return OneLine(built.First(s => s.ContentType == contentType).Content);
    }

    private static string Linq(string sql) => Text(Sql(new EFCoreLinqQueryBuilder(), sql), ConversionContentType.CSharpQuery);

    private static string Dapper(string sql) => Text(Sql(new DapperSqlQueryBuilder(), sql), ConversionContentType.SqlQuery);

    private static ConversionRecord Refused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());
        var record = builder.Records.FirstOrDefault(r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
        Assert.True(record is not null, "No Failure under " + feature + ":\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        return record!;
    }

    private static string OneLine(string text) => string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    /// <summary>The method compiled in the consumer's frame and the SQL EF Core 10 translates it to - the third level (decision 027).</summary>
    private static string Translated(string sql, string name)
    {
        var builder = Sql(new EFCoreLinqQueryBuilder(), sql);
        var method = Built(builder).Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            name,
            method,
            [Entities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore;\nusing JoinConditionDomain;");

        return EFCoreQueryAcceptance.Translate(compiled);
    }

    // ---- the three shapes ------------------------------------------------------------------

    private const string LeftJoinFilteredOnTheRight =
        "SELECT c.Name AS Name, o.Note AS Note FROM Customers AS c LEFT JOIN Orders o ON o.CustomerId = c.CustomerId AND o.Total > 100";

    private const string InnerJoinBeyondEqualities =
        "SELECT c.Name AS Name, o.Note AS Note FROM Customers AS c INNER JOIN Orders o ON o.CustomerId = c.CustomerId AND o.Total > c.CreditLimit";

    private const string LeftJoinBeyondEqualities =
        "SELECT c.Name AS Name, o.Note AS Note FROM Customers AS c LEFT JOIN Orders o ON o.CustomerId = c.CustomerId AND o.Total > c.CreditLimit";

    /// <summary>
    /// A conjunct over the joined row alone filters the joined sequence, and the keys stay the
    /// keys: exact under a left join, where a customer without a matching order keeps a row
    /// of nulls exactly as under the ON.
    /// </summary>
    [Fact]
    public void AFilterOverTheJoinedRowAloneFiltersTheJoinedSequence()
    {
        Assert.Contains(
            ".LeftJoin(ctx.Set<Order>().Where(o => o.Total > 100), c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o })",
            Linq(LeftJoinFilteredOnTheRight),
            StringComparison.Ordinal);

        Assert.Contains(
            ".Join(ctx.Set<Order>().Where(o => o.Total > 100), c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o })",
            Linq(LeftJoinFilteredOnTheRight.Replace("LEFT JOIN", "INNER JOIN", StringComparison.Ordinal)),
            StringComparison.Ordinal);
    }

    /// <summary>A condition that names both rows other than as keys is the correlated SelectMany over the whole condition.</summary>
    [Fact]
    public void AConditionOverBothRowsIsTheCorrelatedSelectMany()
    {
        Assert.Contains(
            ".SelectMany(c => ctx.Set<Order>().Where(o => o.CustomerId == c.CustomerId && o.Total > c.CreditLimit), (c, o) => new { c, o })",
            Linq(InnerJoinBeyondEqualities),
            StringComparison.Ordinal);

        Assert.Contains(
            ".SelectMany(c => ctx.Set<Order>().Where(o => o.CustomerId == c.CustomerId && o.Total > c.CreditLimit).DefaultIfEmpty(), (c, o) => new { c, o })",
            Linq(LeftJoinBeyondEqualities),
            StringComparison.Ordinal);
    }

    /// <summary>A join with no key at all is the correlated SelectMany as well; a LINQ join takes at least one key.</summary>
    [Fact]
    public void AJoinWithoutAKeyIsTheCorrelatedSelectMany()
    {
        Assert.Contains(
            ".SelectMany(c => ctx.Set<Order>().Where(o => o.Total > c.CreditLimit), (c, o) => new { c, o })",
            Linq("SELECT c.Name AS Name, o.Note AS Note FROM Customers AS c INNER JOIN Orders o ON o.Total > c.CreditLimit"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// After a first join the row of the chain is the joined row, and the condition reaches
    /// the earlier tables through its members - the nested lambda takes a name the chain's
    /// own does not hold.
    /// </summary>
    [Fact]
    public void ASecondJoinReachesTheEarlierRowsThroughTheJoinedRow()
    {
        var linq = Linq(
            "SELECT c.Name AS Name, o2.Note AS Note FROM Customers AS c INNER JOIN Orders o ON o.CustomerId = c.CustomerId " +
            "LEFT JOIN Orders AS o2 ON o2.CustomerId = c.CustomerId AND o2.Total > o.Total");

        Assert.Contains(
            ".SelectMany(t => ctx.Set<Order>().Where(o2 => o2.CustomerId == t.c.CustomerId && o2.Total > t.o.Total).DefaultIfEmpty(), (t, o2) => new { t.c, t.o, o2 })",
            linq,
            StringComparison.Ordinal);
    }

    // ---- EF Core translates them --------------------------------------------------------------

    /// <summary>
    /// EF Core 10.0.10 translates the filtered sequence of a left join into the joined
    /// table's own filter and the correlated SelectMany over DefaultIfEmpty() into a left
    /// join with the whole condition: both compile in the consumer's frame and ToQueryString
    /// gives a LEFT JOIN whose rows are the ON's.
    /// </summary>
    [Theory]
    [InlineData(LeftJoinFilteredOnTheRight, "JoinCondition_LeftFiltered", "LEFT JOIN")]
    [InlineData(InnerJoinBeyondEqualities, "JoinCondition_InnerBeyond", "INNER JOIN")]
    [InlineData(LeftJoinBeyondEqualities, "JoinCondition_LeftBeyond", "LEFT JOIN")]
    public void EFCoreTranslatesTheJoin(string sql, string name, string join)
    {
        var translated = Translated(sql, name);

        Assert.Contains(join, translated, StringComparison.Ordinal);
        Assert.DoesNotContain("APPLY", translated, StringComparison.Ordinal);
    }

    // ---- what LINQ cannot say -------------------------------------------------------------

    /// <summary>
    /// A right or a full join with a condition beyond its keys: a filter of the joined
    /// sequence would remove rows the join keeps, and neither join has a correlated form, so
    /// the query goes out in native SQL with a record naming the join (decision 113) - where
    /// it was refused as a failure until then. With keys alone both joins stay LINQ.
    /// </summary>
    [Theory]
    [InlineData("RIGHT JOIN")]
    [InlineData("FULL JOIN")]
    public void ARightOrFullJoinBeyondItsKeysFallsBack(string join)
    {
        var builder = Sql(new EFCoreLinqQueryBuilder(), LeftJoinFilteredOnTheRight.Replace("LEFT JOIN", join, StringComparison.Ordinal));
        var built = Built(builder);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Join);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains($"{join} Orders o ON o.CustomerId = c.CustomerId AND o.Total > 100", OneLine(built.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content), StringComparison.Ordinal);

        var keysOnly = "SELECT c.Name AS Name, o.Note AS Note FROM Customers AS c " + join + " Orders AS o ON o.CustomerId = c.CustomerId";
        Assert.Contains(".RightJoin(ctx.Set<Order>()", Linq(keysOnly), StringComparison.Ordinal);
    }

    /// <summary>A column of the condition without its table belongs to neither row for certain, so LINQ has no place for it; native SQL writes it as the source did.</summary>
    [Fact]
    public void AnUnqualifiedColumnInTheConditionFallsBack()
    {
        var builder = Sql(new EFCoreLinqQueryBuilder(), "SELECT c.Name AS Name, o.Note AS Note FROM Customers AS c LEFT JOIN Orders o ON o.CustomerId = c.CustomerId AND Total > 100");
        Built(builder);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Join && r.Reason.Contains("without its table", StringComparison.Ordinal));
    }

    // ---- reading -----------------------------------------------------------------------

    /// <summary>
    /// What the EF Core target writes reads back as the same condition, so the identity
    /// direction holds and the SQL a target writes from it is the SQL of the source - the
    /// keys first, then the filters of the joined sequence, the condition of a SelectMany as
    /// written.
    /// </summary>
    [Theory]
    [InlineData(LeftJoinFilteredOnTheRight, "LEFT JOIN Orders o ON c.CustomerId = o.CustomerId AND o.Total > 100")]
    [InlineData(InnerJoinBeyondEqualities, "INNER JOIN Orders o ON o.CustomerId = c.CustomerId AND o.Total > c.CreditLimit")]
    [InlineData(LeftJoinBeyondEqualities, "LEFT JOIN Orders o ON o.CustomerId = c.CustomerId AND o.Total > c.CreditLimit")]
    public void TheEFCoreTargetReadsBackAsTheSameJoin(string sql, string expected)
    {
        var linq = Built(Sql(new EFCoreLinqQueryBuilder(), sql)).Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        var builder = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharp, linq, Maps());

        Assert.Contains(expected, Text(builder, ConversionContentType.SqlQuery), StringComparison.Ordinal);

        var identity = new EFCoreLinqQueryBuilder { EntityMaps = Maps() };
        new EFCoreLinqQueryParser(() => identity).Parse(ConversionContentType.CSharp, linq, Maps());
        Assert.Equal(OneLine(linq), Text(identity, ConversionContentType.CSharpQuery));
    }

    /// <summary>The shapes a person writes by hand read the same way: a query expression whose join ranges over a filtered set, and a second from over a filtered set correlated to the first.</summary>
    [Fact]
    public void TheQueryExpressionsReadAsTheJoin()
    {
        Assert.Contains(
            "INNER JOIN Orders o ON c.CustomerId = o.CustomerId AND o.Total > 100",
            Text(Linq(new DapperSqlQueryBuilder(), "from c in ctx.Customers join o in ctx.Orders.Where(x => x.Total > 100) on c.CustomerId equals o.CustomerId select new { c.Name, o.Note }"), ConversionContentType.SqlQuery),
            StringComparison.Ordinal);

        var left = Text(
            Linq(new DapperSqlQueryBuilder(), "from c in ctx.Customers from o in ctx.Orders.Where(x => x.CustomerId == c.CustomerId && x.Total > c.CreditLimit).DefaultIfEmpty() select new { c.Name, o.Note }"),
            ConversionContentType.SqlQuery);

        Assert.Contains("SELECT c.Name AS Name, o.Note AS Note FROM Customers AS c LEFT JOIN Orders o ON o.CustomerId = c.CustomerId AND o.Total > c.CreditLimit", left, StringComparison.Ordinal);
    }

    /// <summary>The filters of the joined sequence are its own: several Where calls are one condition, a string method over the joined row a pattern of it.</summary>
    [Fact]
    public void EveryFilterOfTheJoinedSequenceIsPartOfTheCondition()
    {
        var sql = Text(
            Linq(
                new DapperSqlQueryBuilder(),
                "ctx.Customers.LeftJoin(ctx.Orders.Where(o => o.Total > 100).Where(o => o.Note.StartsWith(\"VIP\")), c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o }).Select(x => new { x.c.Name, x.o.Note })"),
            ConversionContentType.SqlQuery);

        Assert.Contains("LEFT JOIN Orders o ON c.CustomerId = o.CustomerId AND o.Total > 100 AND o.Note LIKE 'VIP%'", sql, StringComparison.Ordinal);
    }

    /// <summary>NHibernate reads the same shapes through the shared reader: its root is Query&lt;T&gt;(), and the sequence a join filters is one too.</summary>
    [Fact]
    public void NHibernateReadsTheFilteredSequenceToo()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        const string source = """
            public void Query()
            {
                var q = session.Query<Customer>()
                    .LeftJoin(session.Query<Order>().Where(o => o.Total > 100), c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o })
                    .Select(x => new { x.c.Name, x.o.Note })
                    .ToList();
            }
            """;
        new NHibernateLinqQueryParser(() => builder).Parse(ConversionContentType.CSharp, source, Maps());

        Assert.Contains("LEFT JOIN Orders o ON c.CustomerId = o.CustomerId AND o.Total > 100", Text(builder, ConversionContentType.SqlQuery), StringComparison.Ordinal);
    }

    /// <summary>
    /// A joined sequence that is a query of its own - projected, ordered, sliced - is carried
    /// only under the name of a variable (decision 112), and was taken for a table named
    /// after the last call until now; a SelectMany over a second source with no condition is
    /// a cross join. Both are refused by name.
    /// </summary>
    [Fact]
    public void ASequenceThatIsAQueryOfItsOwnOrACrossJoinIsRefused()
    {
        var composed = Refused(
            Linq(new DapperSqlQueryBuilder(), "ctx.Customers.Join(ctx.Orders.OrderBy(o => o.Total), c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o })"),
            QueryFeature.IntermediateResult);
        Assert.Contains("OrderBy()", composed.Reason, StringComparison.Ordinal);

        Refused(
            Linq(new DapperSqlQueryBuilder(), "ctx.Customers.SelectMany(c => ctx.Orders.Where(o => o.CustomerId == c.CustomerId).Take(1), (c, o) => new { c, o })"),
            QueryFeature.IntermediateResult);

        var cross = Refused(
            Linq(new DapperSqlQueryBuilder(), "ctx.Customers.SelectMany(c => ctx.Set<Order>().DefaultIfEmpty(), (c, o) => new { c, o })"),
            QueryFeature.Join);
        Assert.Contains("cross join", cross.Reason, StringComparison.Ordinal);
    }

    /// <summary>A collection that is a navigation of the row stays the association path's (decision 101): filtered, it is not a join the reader derives, and it is refused as before.</summary>
    [Fact]
    public void AFilteredNavigationIsNotReadAsAJoinOverASecondSource()
    {
        Refused(
            Linq(new DapperSqlQueryBuilder(), "ctx.Customers.SelectMany(c => c.Orders.Where(o => o.Total > 100), (c, o) => new { c, o })"),
            QueryFeature.Join);
    }
}
