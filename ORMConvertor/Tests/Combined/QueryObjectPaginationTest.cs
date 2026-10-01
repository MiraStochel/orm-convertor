using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The slice a Java unit sets on the JPA query object (decision 060). JPQL has no limit, so
/// a Java source pages only through setFirstResult and setMaxResults - the very form the JPA
/// builder writes -, and the reading is the inverse of that writing: chained onto the
/// createQuery call, with a non-negative integer literal or a value from the enclosing scope
/// (decision 085), in the offset-then-limit normal form. What cannot be read refuses the
/// query, because the query without its slice returns other rows (decision 070). The parser
/// used to take the literal alone, so every such query went out over all the rows without a
/// word.
/// </summary>
public class QueryObjectPaginationTest
{
    private const string Unit = "queries";

    private const string Ordered = "select c from Customer c order by c.CustomerName asc";

    private static ConversionResult Convert(ORMEnum source, ORMEnum target, string content) =>
        ConversionHandler.Convert(
            source,
            target,
            [
                .. CrossFrameworkInputs.MappingUnits(source),
                new() { ContentType = ConversionContentType.Java, Content = content, Name = Unit },
            ]);

    private static string Method(string chain, string parameters = "") =>
        $$"""
        public List<Customer> page(EntityManager em{{parameters}}) {
            return em.createQuery("{{Ordered}}", Customer.class){{chain}}.getResultList();
        }
        """;

    private static string Output(ConversionResult result) =>
        string.Join("\n", result.Sources.Select(s => s.Content));

    private static List<ConversionRecord> Failures(ConversionResult result) =>
        [.. result.Records.Where(r => r.Kind == ConversionRecordKind.Failure && r.Unit == Unit)];

    private static List<ConversionSource> Queries(ConversionResult result) =>
        [.. result.Sources.Where(s => s.ContentType is ConversionContentType.CSharpQuery or ConversionContentType.JavaQuery)];

    /// <summary>Asserts that the query was refused for its pagination, and returns the reason.</summary>
    private static string AssertRefused(ConversionResult result)
    {
        Assert.Empty(Queries(result));

        var failure = Assert.Single(Failures(result));
        Assert.Equal(QueryFeature.Pagination, failure.Feature);
        return failure.Reason;
    }

    [Theory]
    [InlineData(ORMEnum.Hibernate, ORMEnum.Dapper, "OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY")]
    [InlineData(ORMEnum.Hibernate, ORMEnum.MyBatis, "OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY")]
    [InlineData(ORMEnum.Hibernate, ORMEnum.EFCore, ".Skip(20)", ".Take(10)")]
    [InlineData(ORMEnum.Hibernate, ORMEnum.NHibernate, ".SetFirstResult(20)", ".SetMaxResults(10)")]
    [InlineData(ORMEnum.Hibernate, ORMEnum.Hibernate, ".setFirstResult(20)", ".setMaxResults(10)")]
    [InlineData(ORMEnum.Hibernate, ORMEnum.EclipseLink, ".setFirstResult(20)", ".setMaxResults(10)")]
    [InlineData(ORMEnum.EclipseLink, ORMEnum.EFCore, ".Skip(20)", ".Take(10)")]
    [InlineData(ORMEnum.EclipseLink, ORMEnum.Hibernate, ".setFirstResult(20)", ".setMaxResults(10)")]
    public void TheSliceOfTheQueryObjectReachesEveryTarget(ORMEnum source, ORMEnum target, params string[] expected)
    {
        var result = Convert(source, target, Method(".setFirstResult(20).setMaxResults(10)"));

        var output = Output(result);
        Assert.All(expected, e => Assert.Contains(e, output, StringComparison.Ordinal));
        Assert.Empty(Failures(result));
    }

    /// <summary>
    /// The two calls are setters of the query object, not steps of a chain: the order they are
    /// called in changes nothing, and of two calls of one the last is what the query runs with.
    /// </summary>
    [Theory]
    [InlineData(".setMaxResults(10).setFirstResult(20)")]
    [InlineData(".setParameter(\"unused\", 1).setFirstResult(20).setMaxResults(100).setMaxResults(10)")]
    public void TheSettersAreReadAsTheQueryRunsThem(string chain)
    {
        var expected = Assert.Single(Queries(Convert(ORMEnum.Hibernate, ORMEnum.EFCore, Method(".setFirstResult(20).setMaxResults(10)"))));
        var read = Assert.Single(Queries(Convert(ORMEnum.Hibernate, ORMEnum.EFCore, Method(chain))));

        Assert.Equal(expected.Content, read.Content);
    }

    /// <summary>A page size from the enclosing scope is the parameter of the same name, typed Int by the clause (decision 085).</summary>
    [Fact]
    public void AValueFromTheEnclosingScopeIsAParameterOfTheMethod()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.EFCore, Method(".setFirstResult(skip).setMaxResults(take)", ", int skip, int take"));

        var method = Assert.Single(Queries(result)).Content;
        Assert.Contains("(DbContext ctx, int skip, int take)", method, StringComparison.Ordinal);
        Assert.Contains(".Skip(skip)", method, StringComparison.Ordinal);
        Assert.Contains(".Take(take)", method, StringComparison.Ordinal);
        Assert.Empty(Failures(result));
    }

    /// <summary>A limit alone, without an ordering, is a TOP in T-SQL as from any other source.</summary>
    [Fact]
    public void ALimitAloneIsReadToo()
    {
        var result = Convert(
            ORMEnum.Hibernate,
            ORMEnum.Dapper,
            """
            public List<Customer> some(EntityManager em) {
                return em.createQuery("select c from Customer c", Customer.class).setMaxResults(5).getResultList();
            }
            """);

        Assert.Contains("TOP (5)", Output(result), StringComparison.Ordinal);
        Assert.Empty(Failures(result));
    }

    [Theory]
    [InlineData(".setFirstResult((page - 1) * size).setMaxResults(size)", "setFirstResult")]
    [InlineData(".setMaxResults(this.size)", "setMaxResults")]
    [InlineData(".setMaxResults(010)", "setMaxResults")]
    public void AValueComputedAtRunTimeRefusesTheQuery(string chain, string setter)
    {
        var reason = AssertRefused(Convert(ORMEnum.Hibernate, ORMEnum.EFCore, Method(chain, ", int page, int size")));

        Assert.Contains($"The argument of {setter}()", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A query object kept in a variable and sliced in another statement may be sliced on a
    /// condition, so the slice is named and refused rather than read.
    /// </summary>
    [Theory]
    [InlineData("q.setMaxResults(10);")]
    [InlineData("if (paged) { q.setParameter(\"unused\", 1).setFirstResult(20); }")]
    public void ASliceSetInAnotherStatementRefusesTheQuery(string statement)
    {
        var result = Convert(
            ORMEnum.Hibernate,
            ORMEnum.EFCore,
            $$"""
            public List<Customer> page(EntityManager em, boolean paged) {
                TypedQuery<Customer> q = em.createQuery("{{Ordered}}", Customer.class);
                {{statement}}
                return q.getResultList();
            }
            """);

        Assert.Contains("the variable 'q'", AssertRefused(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// A variable that is never sliced is no reason to refuse, and one assigned again holds
    /// another query object after that: the slice below belongs to the second query alone.
    /// </summary>
    [Fact]
    public void AVariableIsFollowedOnlyUntilItIsAssignedAgain()
    {
        var result = Convert(
            ORMEnum.Hibernate,
            ORMEnum.EFCore,
            """
            public List<Customer> load(EntityManager em) {
                TypedQuery<Customer> q = em.createQuery("select c from Customer c where c.CreditLimit > 2000", Customer.class);
                List<Customer> rich = q.getResultList();
                q = em.createQuery("select c from Customer c order by c.CustomerName asc", Customer.class);
                q.setMaxResults(10);
                return q.getResultList();
            }
            """);

        var rich = Assert.Single(Queries(result));
        Assert.Contains("Query01(DbContext ctx)", rich.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(", rich.Content, StringComparison.Ordinal);

        var failure = Assert.Single(Failures(result));
        Assert.Equal("Query02", failure.Query);
        Assert.Contains("the variable 'q'", failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>A slice belongs to the call it is chained onto, not to the other queries of the unit.</summary>
    [Fact]
    public void TheSliceBelongsToItsOwnQuery()
    {
        var result = Convert(
            ORMEnum.Hibernate,
            ORMEnum.EFCore,
            Method(".setMaxResults(10)") + "\n\n" +
            """
            public List<Customer> all(EntityManager em) {
                return em.createQuery("select c from Customer c", Customer.class).getResultList();
            }
            """);

        var queries = Queries(result);
        Assert.Equal(2, queries.Count);
        Assert.Contains(queries, q => q.Content.Contains("Query01(", StringComparison.Ordinal) && q.Content.Contains(".Take(10)", StringComparison.Ordinal));
        Assert.Contains(queries, q => q.Content.Contains("Query02(", StringComparison.Ordinal) && !q.Content.Contains(".Take(", StringComparison.Ordinal));
    }

    /// <summary>
    /// Over a set operation the slice applies to the composed result, which the representation
    /// has no place for: the same refusal as a trailing OFFSET in T-SQL.
    /// </summary>
    [Fact]
    public void ASliceOverASetOperationRefusesTheQuery()
    {
        var result = Convert(
            ORMEnum.Hibernate,
            ORMEnum.Dapper,
            """
            public List<String> names(EntityManager em) {
                return em.createQuery("select c.CustomerName from Customer c where c.CreditLimit > 2000 union select c.CustomerName from Customer c where c.CreditLimit < 100", String.class)
                    .setMaxResults(5)
                    .getResultList();
            }
            """);

        Assert.Contains("set operation", AssertRefused(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// HQL has a limit of its own, read by the dialect hook; a slice in the text and another
    /// on the query object are two paginations of one query, which is refused, not merged.
    /// </summary>
    [Fact]
    public void ASliceInTheTextAndOnTheObjectRefusesTheQuery()
    {
        var result = Convert(
            ORMEnum.Hibernate,
            ORMEnum.EFCore,
            """
            public List<Customer> page(EntityManager em) {
                return em.createQuery("select c from Customer c order by c.CustomerName asc limit 5", Customer.class).setMaxResults(10).getResultList();
            }
            """);

        AssertRefused(result);
    }
}
