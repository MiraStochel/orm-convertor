using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Combined;

namespace Tests.Hibernate;

/// <summary>
/// JPQL as a target through the real orchestration (decision 077): the same queries the
/// .NET sources state, in the standard form and with the two artifacts of decision 025.
/// </summary>
public class HibernateJpqlQueryBuilderTest
{
    private static ConversionResult Convert(ORMEnum source, string query, ConversionContentType language)
        => ConversionHandler.Convert(source, ORMEnum.Hibernate,
        [
            .. CrossFrameworkInputs.MappingUnits(source),
            new() { Content = query, ContentType = language },
        ]);

    private static string Jpql(ConversionResult result)
    {
        // The Dapper sample states no key, so the entity is refused by the Hibernate target
        // (decision 010); only the query branch is under test here.
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Artifact?.IsQuery() == true);
        return result.Sources.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
    }

    private static string Method(ConversionResult result)
        => result.Sources.Single(s => s.ContentType == ConversionContentType.JavaQuery).Content;

    [Fact]
    public void AWholeEntityQueryFromLinqIsATypedQuery()
    {
        var result = Convert(ORMEnum.EFCore, """
            public void Query()
            {
                var q = ctx.Customers.Where(c => c.CreditLimit > 2000).OrderBy(c => c.CustomerName).ToList();
            }
            """, ConversionContentType.CSharpQuery);

        var jpql = Jpql(result);
        Assert.Equal("select c\nfrom Customer c\nwhere c.CreditLimit > 2000\norder by c.CustomerName asc", jpql.Replace("\r\n", "\n"));

        var method = Method(result);
        Assert.Contains("public static TypedQuery<Customer> query(EntityManager em) {", method);
        Assert.Contains("em.createQuery(\"\"\"", method);
        Assert.Contains("\"\"\", Customer.class);", method);
    }

    [Fact]
    public void AProjectionFromSqlIsAnUntypedQuery()
    {
        var result = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName, COUNT(*) AS n
            FROM Sales.Customers AS c
            GROUP BY c.CustomerName
            HAVING SUM(c.CreditLimit) > 1
            """, ConversionContentType.SqlQuery);

        var jpql = Jpql(result);
        Assert.Contains("select c.CustomerName, count(c) as n", jpql);
        Assert.Contains("group by c.CustomerName", jpql);
        Assert.Contains("having sum(c.CreditLimit) > 1", jpql);
        Assert.Contains("public static Query query(EntityManager em) {", Method(result));
    }

    [Fact]
    public void PaginationLandsOnTheQueryObject()
    {
        var result = Convert(ORMEnum.EFCore, """
            public void Query()
            {
                var q = ctx.Customers.OrderBy(c => c.CustomerName).Skip(20).Take(10).ToList();
            }
            """, ConversionContentType.CSharpQuery);

        Assert.DoesNotContain("limit", Jpql(result));
        Assert.Contains(".setFirstResult(20)", Method(result));
        Assert.Contains(".setMaxResults(10)", Method(result));
    }

    [Fact]
    public void ASetOperationFromSqlIsWrittenInJpql()
    {
        var result = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit > 2000
            UNION ALL
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit < 10
            """, ConversionContentType.SqlQuery);

        var jpql = Jpql(result);
        Assert.Contains("union all", jpql);
        Assert.Contains("select c.CustomerName from Customer c where c.CreditLimit > 2000", jpql);
    }

    [Fact]
    public void ASubQueryFromSqlIsWrittenInJpql()
    {
        var result = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            WHERE c.CustomerId IN (SELECT o.CustomerId FROM Sales.Orders AS o WHERE o.Total > 100)
            """, ConversionContentType.SqlQuery);

        Assert.Contains("where c.CustomerId in (select o.CustomerId from Order o where o.Total > 100)", Jpql(result));
    }

    [Fact]
    public void AFullOuterJoinIsExpressible()
    {
        var result = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            FULL JOIN Sales.Orders AS o ON o.CustomerId = c.CustomerId
            """, ConversionContentType.SqlQuery);

        var jpql = Jpql(result);
        Assert.True(jpql.Contains("full join Order o on o.CustomerId = c.CustomerId"), jpql);
        Assert.Equal(FactSupport.Expressible, HibernateWrappers.HibernateDescriptor.Instance.SupportOf(QueryFeature.JoinKind));
    }

    [Fact]
    public void ALikePatternAndAnInListFromSqlAreWrittenAsTheyAre()
    {
        var result = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            WHERE c.CustomerName LIKE 'A%' AND c.CreditLimit IN (1000, 2000)
            """, ConversionContentType.SqlQuery);

        Assert.Contains("where c.CustomerName like 'A%' and c.CreditLimit in (1000, 2000)", Jpql(result));
    }

    [Fact]
    public void AnExistsSubQueryIsWrittenInJpql()
    {
        var result = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            WHERE EXISTS (SELECT o.CustomerId FROM Sales.Orders AS o WHERE o.CustomerId = c.CustomerId)
            """, ConversionContentType.SqlQuery);

        Assert.Contains("where exists (select o.CustomerId from Order o where o.CustomerId = c.CustomerId)", Jpql(result));
    }

    [Fact]
    public void DistinctIsWrittenInsideTheSelectClause()
    {
        var result = Convert(ORMEnum.Dapper, "SELECT DISTINCT c.CustomerName FROM Sales.Customers AS c", ConversionContentType.SqlQuery);

        Assert.StartsWith("select distinct c.CustomerName", Jpql(result));
    }

    [Fact]
    public void TheRecordsOfTheQueryBranchNameTheJavaArtifact()
    {
        var result = Convert(ORMEnum.Dapper, "SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit > @limit", ConversionContentType.SqlQuery);

        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.QueryParameter);
    }
}
