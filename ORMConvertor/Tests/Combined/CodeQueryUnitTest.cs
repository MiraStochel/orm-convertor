using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A query unit in code carries a query for every place where the source hands the framework
/// a query, not for the first one alone (decision 109): every call of Dapper that sends SQL,
/// every createQuery of JPA, every LINQ chain over a query root. Each is read into a builder
/// of its own, so one that the reading refuses refuses itself alone, and the queries of one
/// unit are numbered by the position of their place in the text, as the SELECTs of a script
/// are (decision 108). The unit used to yield its first query and say nothing of the rest.
///
/// What one query is in LINQ is the tree the provider gets. A chain the code keeps in a
/// variable assigned once and continues in another statement is one query with its
/// continuation, read as if written in one expression; a variable assigned more than once
/// makes a query composed at run time, refused by name; and what the code does with rows it
/// has already loaded is no query at all.
/// </summary>
public class CodeQueryUnitTest
{
    private const string Unit = "queries";

    private const string RichSql = "SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit > 2000";
    private const string OrderedSql = "SELECT c.CustomerName FROM Sales.Customers AS c ORDER BY c.CustomerName ASC";

    private static ConversionResult Convert(ORMEnum source, ORMEnum target, ConversionContentType type, string content) =>
        ConversionHandler.Convert(
            source,
            target,
            [
                .. CrossFrameworkInputs.MappingUnits(source),
                new() { ContentType = type, Content = content, Name = Unit },
            ]);

    private static List<string> Methods(ConversionResult result, ConversionContentType artifact) =>
        [.. result.Sources.Where(s => s.ContentType == artifact).Select(s => s.Content)];

    /// <summary>
    /// The records of the query unit alone: without a catalog a Java target refuses the
    /// entity of a Dapper source for want of a key, which is the entity's story.
    /// </summary>
    private static List<ConversionRecord> Failures(ConversionResult result) =>
        [.. result.Records.Where(r => r.Kind == ConversionRecordKind.Failure && r.Unit == Unit)];

    private static string DapperCall(string method, string sql) =>
        $"public object {method}(IDbConnection connection) => connection.Query<Customer>(\"{sql}\").ToList();";

    [Theory]
    [InlineData(ORMEnum.Dapper, ConversionContentType.CSharpQuery, "Query01(IDbConnection connection)", "Query02(IDbConnection connection)")]
    [InlineData(ORMEnum.EFCore, ConversionContentType.CSharpQuery, "Query01(DbContext ctx)", "Query02(DbContext ctx)")]
    [InlineData(ORMEnum.Hibernate, ConversionContentType.JavaQuery, "query01(EntityManager em)", "query02(EntityManager em)")]
    public void EveryDapperCallIsAQueryNamedByItsPosition(ORMEnum target, ConversionContentType artifact, string first, string second)
    {
        var result = Convert(
            ORMEnum.Dapper,
            target,
            ConversionContentType.CSharpQuery,
            DapperCall("Rich", RichSql) + "\n" + DapperCall("Ordered", OrderedSql));

        var methods = Methods(result, artifact);

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains(first, StringComparison.Ordinal) && m.Contains("2000", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains(second, StringComparison.Ordinal) && !m.Contains("2000", StringComparison.Ordinal));
        Assert.Empty(Failures(result));
    }

    /// <summary>
    /// The number is given before the reading, to every call: a call whose SQL is composed at
    /// run time keeps its place, the call after it stays the third, and the record names the
    /// query by the name it would have stood under.
    /// </summary>
    [Fact]
    public void ACallThatCannotBeReadKeepsItsNumber()
    {
        var result = Convert(
            ORMEnum.Dapper,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            DapperCall("Rich", RichSql) + "\n"
                + "public object Built(IDbConnection connection, string sql) => connection.Query<Customer>(sql).ToList();\n"
                + DapperCall("Ordered", OrderedSql));

        var methods = Methods(result, ConversionContentType.CSharpQuery);

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("Query01(IDbConnection connection)", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query03(IDbConnection connection)", StringComparison.Ordinal));

        var unread = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Unit == Unit);
        Assert.Equal("Query02", unread.Query);
        Assert.Contains("does not pass the SQL as a string literal", unread.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// QueryMultiple maps every result set of its text, so the text is a script and each of its
    /// SELECTs is a query, numbered in the order of the unit with the calls around it. It used
    /// to be no Dapper call at all: alone in a unit it drew the record that no query was found.
    /// </summary>
    [Fact]
    public void EverySelectOfQueryMultipleIsAQuery()
    {
        var result = Convert(
            ORMEnum.Dapper,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            $$"""
            public void Load(IDbConnection connection)
            {
                using var grid = connection.QueryMultiple("{{RichSql}}; {{OrderedSql}}");
                var rich = grid.Read<Customer>().ToList();
                var ordered = grid.Read<Customer>().ToList();
                var all = connection.Query<Customer>("SELECT c.CustomerName FROM Sales.Customers AS c").ToList();
            }
            """);

        var methods = Methods(result, ConversionContentType.CSharpQuery);

        Assert.Equal(3, methods.Count);
        Assert.Contains(methods, m => m.Contains("Query01(", StringComparison.Ordinal) && m.Contains("2000", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query02(", StringComparison.Ordinal) && m.Contains("ORDER BY", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query03(", StringComparison.Ordinal));
        Assert.Empty(Failures(result));
    }

    /// <summary>
    /// Execute is Dapper's call for a command that writes, and the line that refuses a write in
    /// a bare unit refuses it here by name, instead of passing it over. A call is one exchange
    /// with the server, so the write does not refuse the query beside it, as it would in a
    /// script: the translated query carries only its own text.
    /// </summary>
    [Fact]
    public void AWriteBesideAQueryIsRefusedByNameAndAloneRefused()
    {
        var result = Convert(
            ORMEnum.Dapper,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            """
            public object Purge(IDbConnection connection)
            {
                connection.Execute("DELETE FROM Sales.Customers WHERE CreditLimit < 0");
                return connection.Query<Customer>("SELECT c.CustomerName FROM Sales.Customers AS c").ToList();
            }
            """);

        var method = Assert.Single(Methods(result, ConversionContentType.CSharpQuery));
        Assert.Contains("Query02(IDbConnection connection)", method, StringComparison.Ordinal);

        var refused = Assert.Single(Failures(result));
        Assert.Equal("Query01", refused.Query);
        Assert.Contains("DELETE", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>A unit of one call is not numbered: nothing to tell its query from.</summary>
    [Fact]
    public void ASingleCallKeepsTheFixedName()
    {
        var result = Convert(ORMEnum.Dapper, ORMEnum.Dapper, ConversionContentType.CSharpQuery, DapperCall("Rich", RichSql));

        var method = Assert.Single(Methods(result, ConversionContentType.CSharpQuery));

        Assert.Contains(" Query(IDbConnection connection)", method, StringComparison.Ordinal);
        Assert.All(result.Records, r => Assert.Null(r.Query));
    }

    [Theory]
    [InlineData(ORMEnum.EFCore, "ctx.Customers")]
    [InlineData(ORMEnum.NHibernate, "session.Query<Customer>()")]
    public void EveryLinqChainIsAQueryNamedByItsPosition(ORMEnum source, string queryRoot)
    {
        var result = Convert(
            source,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            $$"""
            public object Rich() => {{queryRoot}}.Where(c => c.CreditLimit > 2000).ToList();

            public object Ordered() => {{queryRoot}}.OrderBy(c => c.CustomerName).ToList();
            """);

        var methods = Methods(result, ConversionContentType.CSharpQuery);

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("Query01(IDbConnection connection)", StringComparison.Ordinal) && m.Contains("2000", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query02(IDbConnection connection)", StringComparison.Ordinal) && m.Contains("ORDER BY", StringComparison.Ordinal));
        Assert.Empty(Failures(result));
    }

    /// <summary>A chain in a lambda of another is its subquery, not a query of the unit.</summary>
    [Fact]
    public void ASubqueryIsNoQueryOfTheUnit()
    {
        var result = Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            "public object AboveAverage() => ctx.Customers.Where(c => c.CreditLimit > ctx.Customers.Average(x => x.CreditLimit)).ToList();");

        var method = Assert.Single(Methods(result, ConversionContentType.CSharpQuery));

        Assert.Contains(" Query(IDbConnection connection)", method, StringComparison.Ordinal);
        Assert.Contains("AVG(", method, StringComparison.Ordinal);
    }

    /// <summary>
    /// The provider gets one tree from a query kept in a variable and continued in another
    /// statement, so the tool reads that tree: the same artifact as the chain written in one
    /// expression. Until then the kept part went out as the query and the continuation - here
    /// the ordering and the projection - was dropped without a word.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, "ctx.Customers")]
    [InlineData(ORMEnum.NHibernate, "session.Query<Customer>()")]
    public void AQueryContinuedThroughAVariableAssignedOnceIsReadWhole(ORMEnum source, string queryRoot)
    {
        var continued = Convert(
            source,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            $$"""
            public object Names()
            {
                var rich = {{queryRoot}}.Where(c => c.CreditLimit > 2000);
                return rich.OrderBy(c => c.CustomerName).Select(c => c.CustomerName).ToList();
            }
            """);

        var written = Convert(
            source,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            $"public object Names() => {queryRoot}.Where(c => c.CreditLimit > 2000).OrderBy(c => c.CustomerName).Select(c => c.CustomerName).ToList();");

        var method = Assert.Single(Methods(continued, ConversionContentType.CSharpQuery));

        Assert.Equal(Assert.Single(Methods(written, ConversionContentType.CSharpQuery)), method);
        Assert.Contains("ORDER BY", method, StringComparison.Ordinal);
        Assert.Empty(Failures(continued));
    }

    /// <summary>
    /// A variable continued twice holds the beginning of two queries; one also returned is a
    /// query of its own besides.
    /// </summary>
    [Fact]
    public void EveryContinuationIsAQueryAndAVariableUsedOtherwiseIsOneToo()
    {
        var result = Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            """
            public object Load()
            {
                var rich = ctx.Customers.Where(c => c.CreditLimit > 2000);
                var ordered = rich.OrderBy(c => c.CustomerName).ToList();
                var named = rich.Select(c => c.CustomerName).ToList();
                return rich;
            }
            """);

        var methods = Methods(result, ConversionContentType.CSharpQuery);

        Assert.Equal(3, methods.Count);
        Assert.All(methods, m => Assert.Contains("2000", m, StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query01(", StringComparison.Ordinal) && !m.Contains("ORDER BY", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query02(", StringComparison.Ordinal) && m.Contains("ORDER BY", StringComparison.Ordinal));
        Assert.Empty(Failures(result));
    }

    /// <summary>
    /// After ToList the variable holds rows, and what the code does with them is C# over
    /// objects in memory: the query is the chain that loaded them, and the Select after it is
    /// no part of it.
    /// </summary>
    [Fact]
    public void AVariableThatHoldsLoadedRowsTakesNoContinuation()
    {
        var result = Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            """
            public object Names()
            {
                var rich = ctx.Customers.Where(c => c.CreditLimit > 2000).ToList();
                return rich.Select(c => c.CustomerName).ToList();
            }
            """);

        var loaded = Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            "public object Names() => ctx.Customers.Where(c => c.CreditLimit > 2000).ToList();");

        Assert.Equal(
            Assert.Single(Methods(loaded, ConversionContentType.CSharpQuery)),
            Assert.Single(Methods(result, ConversionContentType.CSharpQuery)));
    }

    /// <summary>
    /// A variable assigned more than once hands the provider a tree the text does not fix -
    /// which one runs is decided by a condition at run time -, so the query in it is refused
    /// naming the variable, where the first chain used to go out without the filter.
    /// </summary>
    [Theory]
    [InlineData("""
        public object Filtered(bool onlyRich)
        {
            var q = ctx.Customers.AsQueryable();
            if (onlyRich) q = q.Where(c => c.CreditLimit > 2000);
            return q.ToList();
        }
        """)]
    [InlineData("""
        public object Filtered(bool onlyRich)
        {
            var q = onlyRich ? ctx.Customers.Where(c => c.CreditLimit > 2000) : ctx.Customers.AsQueryable();
            return q.OrderBy(c => c.CustomerName).ToList();
        }
        """)]
    public void AQueryComposedAtRunTimeIsRefusedNamingTheVariable(string unit)
    {
        var result = Convert(ORMEnum.EFCore, ORMEnum.Dapper, ConversionContentType.CSharpQuery, unit);

        Assert.Empty(Methods(result, ConversionContentType.CSharpQuery));

        var failures = Failures(result);
        Assert.NotEmpty(failures);
        Assert.All(failures, f => Assert.Contains("variable 'q'", f.Reason, StringComparison.Ordinal));
    }

    /// <summary>
    /// A query expression refused for its let clause refuses itself alone, whether the rewrite
    /// stopped inside a chain that is a query of the unit or before the chain began.
    /// </summary>
    [Theory]
    [InlineData("public object Named() => (from c in ctx.Customers let n = c.CustomerName select n).ToList();")]
    [InlineData("public object Named() { var names = from c in ctx.Customers let n = c.CustomerName select n; return names; }")]
    public void AQueryExpressionRefusedForLetRefusesItselfAlone(string refused)
    {
        var result = Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            "public object Rich() => ctx.Customers.Where(c => c.CreditLimit > 2000).ToList();\n" + refused);

        var method = Assert.Single(Methods(result, ConversionContentType.CSharpQuery));
        Assert.Contains("Query01(IDbConnection connection)", method, StringComparison.Ordinal);

        var failure = Assert.Single(Failures(result));
        Assert.Equal("Query02", failure.Query);
        Assert.Contains("let n", failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A member of an element - a lambda parameter, a foreach variable - is a navigation over
    /// objects the code loaded, not a DbSet, even though EF Core's root is recognized by its
    /// shape: read as a query of its own it would claim a query over a table named Lines.
    /// </summary>
    [Theory]
    [InlineData("return rich.Sum(c => c.Lines.Count());")]
    [InlineData("var total = 0; foreach (var c in rich) { total += c.Lines.Count(); } return total;")]
    public void ANavigationOverLoadedRowsIsNoQuery(string walk)
    {
        var result = Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            ConversionContentType.CSharpQuery,
            $$"""
            public int Lines()
            {
                var rich = ctx.Customers.Where(c => c.CreditLimit > 2000).ToList();
                {{walk}}
            }
            """);

        var method = Assert.Single(Methods(result, ConversionContentType.CSharpQuery));

        Assert.Contains(" Query(IDbConnection connection)", method, StringComparison.Ordinal);
        Assert.Empty(Failures(result));
    }

    [Theory]
    [InlineData(ORMEnum.EFCore, ConversionContentType.CSharpQuery, "Query01(DbContext ctx)", "Query02(DbContext ctx)")]
    [InlineData(ORMEnum.Hibernate, ConversionContentType.JavaQuery, "query01(EntityManager em)", "query02(EntityManager em)")]
    public void EveryCreateQueryIsAQueryNamedByItsPosition(ORMEnum target, ConversionContentType artifact, string first, string second)
    {
        var result = Convert(
            ORMEnum.Hibernate,
            target,
            ConversionContentType.JavaQuery,
            """
            public List<Customer> rich(EntityManager em) {
                return em.createQuery("select c from Customer c where c.CreditLimit > 2000", Customer.class).getResultList();
            }

            public List<Customer> ordered(EntityManager em) {
                return em.createQuery("select c from Customer c order by c.CustomerName asc", Customer.class).getResultList();
            }
            """);

        var methods = Methods(result, artifact);

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains(first, StringComparison.Ordinal) && m.Contains("2000", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains(second, StringComparison.Ordinal) && !m.Contains("2000", StringComparison.Ordinal));
        Assert.Empty(Failures(result));
    }

    /// <summary>A createQuery composed at run time refuses itself alone and keeps its number.</summary>
    [Fact]
    public void ACreateQueryComposedAtRunTimeKeepsItsNumber()
    {
        var result = Convert(
            ORMEnum.Hibernate,
            ORMEnum.EFCore,
            ConversionContentType.JavaQuery,
            """
            public List<Customer> rich(EntityManager em) {
                return em.createQuery("select c from Customer c where c.CreditLimit > 2000", Customer.class).getResultList();
            }

            public List<Customer> above(EntityManager em, String floor) {
                return em.createQuery("select c from Customer c where c.CreditLimit > " + floor, Customer.class).getResultList();
            }

            public List<Customer> ordered(EntityManager em) {
                return em.createQuery("select c from Customer c order by c.CustomerName asc", Customer.class).getResultList();
            }
            """);

        var methods = Methods(result, ConversionContentType.CSharpQuery);

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("Query01(DbContext ctx)", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query03(DbContext ctx)", StringComparison.Ordinal));

        var unread = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Unit == Unit);
        Assert.Equal("Query02", unread.Query);
    }
}
