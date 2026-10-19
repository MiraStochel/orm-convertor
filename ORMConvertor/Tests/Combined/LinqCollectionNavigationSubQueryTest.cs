using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Tests.Combined;

/// <summary>
/// A subquery written over a collection navigation of the row - <c>c.Orders.Any(o =&gt; …)</c>,
/// <c>c.Orders.All(…)</c>, <c>c.Orders.Count(…)</c> - ranges over the rows of the navigation's
/// target that belong to the outer row, and the provider derives that correlation from the
/// relation. Until 2026-10-16 the shared LINQ parser took the head for a set of the context,
/// which is what the root recognizer makes of the two-identifier shape (decision 026), and
/// the subquery went out without its correlation and without a word: the query returned
/// every customer as soon as any order met the predicate (decision 053). Now the navigation
/// is matched to the relation of the entity behind the row, exactly as the join along an
/// association path is (decision 101), the source is the relation's target and FK = PK over
/// its column pairs is the first condition of the nested scope. What the maps do not hold is
/// refused by name, never guessed (decision 067).
/// </summary>
public class LinqCollectionNavigationSubQueryTest
{
    /// <summary>
    /// Customers and orders linked by one relation seen from both sides, as the resolution
    /// phase leaves them: the owning many-to-one from the order and the inverse one-to-many
    /// from the customer. The pairs sit on both sides, on the owning side alone, or nowhere.
    /// </summary>
    private static (EntityMap Orders, EntityMap Customers) Linked(
        bool pairsOnInverse = true,
        bool pairsOnOwning = true,
        bool composite = false,
        Cardinality inverseCardinality = Cardinality.OneToMany)
    {
        var customerKey = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var customerCompany = new Property { Name = "CompanyID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var limit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal) };
        var customerKeyMap = new PropertyMap { Property = customerKey, ColumnName = "CustomerID" };
        var customerCompanyMap = new PropertyMap { Property = customerCompany, ColumnName = "CompanyID" };

        var customers = new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [customerCompany, customerKey, name, limit] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                customerCompanyMap,
                customerKeyMap,
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
                new PropertyMap { Property = limit, ColumnName = "CreditLimit" },
            ],
        };

        var orderCustomer = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var orderCompany = new Property { Name = "CompanyID", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };
        var orderCustomerMap = new PropertyMap { Property = orderCustomer, ColumnName = "CustomerID" };
        var orderCompanyMap = new PropertyMap { Property = orderCompany, ColumnName = "CompanyID" };

        var orders = new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [orderCompany, orderCustomer, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps = [orderCompanyMap, orderCustomerMap, new PropertyMap { Property = total, ColumnName = "Total" }],
        };

        List<ColumnPair> pairs = [];
        if (composite)
        {
            pairs.Add(new ColumnPair { Source = orderCompanyMap, Target = customerCompanyMap });
        }

        pairs.Add(new ColumnPair { Source = orderCustomerMap, Target = customerKeyMap });

        orders.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Order",
            TargetEntity = "Customer",
            SourceNavigationProperty = "Customer",
            ColumnPairs = pairsOnOwning ? pairs : [],
        });

        customers.Relations.Add(new Relation
        {
            Cardinality = inverseCardinality,
            Role = RelationRole.Inverse,
            SourceEntity = "Customer",
            TargetEntity = "Order",
            SourceNavigationProperty = "Orders",
            ColumnPairs = pairsOnInverse ? pairs : [],
        });

        return (orders, customers);
    }

    private static string Method(string chain) =>
        $$"""
        public void Query()
        {
            var q = {{chain}}.ToList();
        }
        """;

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string source, params EntityMap[] maps)
    {
        builder.EntityMaps = maps;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, maps);
        return builder;
    }

    private static string Sql(string chain, params EntityMap[] maps)
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(chain), maps);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);
        return Normalized(artifacts.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content);
    }

    private static string Normalized(string text) => string.Join(" ", text.Split((char[])[' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static ConversionRecord Refusal(string chain, params EntityMap[] maps)
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(chain), maps);

        Assert.Empty(builder.Build());
        return Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    // ---- the correlation comes from the relation -----------------------------------------

    [Fact]
    public void AnyOverACollectionNavigationIsExistsCorrelatedByTheRelation()
    {
        var (orders, customers) = Linked();

        var sql = Sql("ctx.Customers.Where(c => c.Orders.Any(o => o.Total > 100))", orders, customers);

        // Rule Q7 over the inverse side: the foreign key sits on the navigation's target, so
        // the nested row carries it - o.CustomerID = c.CustomerID -, and the predicate the
        // source wrote follows.
        Assert.Contains("FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID AND o.Total > 100", sql);
        Assert.DoesNotContain("Sales.Orders AS o WHERE o.Total > 100)", sql);
    }

    [Fact]
    public void AnyWithoutAPredicateIsExistsOverTheCorrelationAlone()
    {
        var (orders, customers) = Linked();

        var sql = Sql("ctx.Customers.Where(c => c.Orders.Any())", orders, customers);

        Assert.Contains("FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)", sql);
    }

    [Fact]
    public void TheNestedAliasStaysClearOfTheOuterOne()
    {
        var (orders, customers) = Linked();

        // The outer row is named o; the first letter of Order is taken, so the nested rows
        // take the next free name - otherwise the correlation compared a row with itself.
        var sql = Sql("ctx.Customers.Where(o => o.Orders.Any())", orders, customers);

        Assert.Contains("FROM Sales.Orders AS o2 WHERE o2.CustomerID = o.CustomerID)", sql);
    }

    [Fact]
    public void AllOverACollectionNavigationIsTheQuantifiedComparisonOverTheCorrelatedRows()
    {
        var (orders, customers) = Linked();

        var sql = Sql("ctx.Customers.Where(c => c.Orders.All(o => c.CreditLimit > o.Total))", orders, customers);

        Assert.Contains("c.CreditLimit > ALL (SELECT o.Total AS Total FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)", sql);
    }

    [Fact]
    public void CountOverACollectionNavigationIsTheCorrelatedScalarSubquery()
    {
        var (orders, customers) = Linked();

        var withPredicate = Sql("ctx.Customers.Where(c => c.Orders.Count(o => o.Total > 100) > 2)", orders, customers);
        var bare = Sql("ctx.Customers.Where(c => c.Orders.Count() > 2)", orders, customers);

        Assert.Contains("(SELECT COUNT(*) FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID AND o.Total > 100) > 2", withPredicate);
        Assert.Contains("(SELECT COUNT(*) FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID) > 2", bare);
    }

    [Fact]
    public void AnAggregateOverACollectionNavigationIsCorrelatedToo()
    {
        var (orders, customers) = Linked();

        var sql = Sql("ctx.Customers.Where(c => c.Orders.Sum(o => o.Total) > 1000)", orders, customers);

        // The Sum of EF Core is 0 over no rows (decision 155).
        Assert.Contains("COALESCE((SELECT SUM(o.Total) FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID), 0) > 1000", sql);
    }

    [Fact]
    public void AProjectedNavigationUnderInIsCorrelatedToo()
    {
        var (orders, customers) = Linked();

        var sql = Sql("ctx.Customers.Where(c => c.Orders.Select(o => o.Total).Contains(c.CreditLimit))", orders, customers);

        Assert.Contains("c.CreditLimit IN (SELECT o.Total AS Total FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)", sql);
    }

    [Fact]
    public void ACompositeRelationCorrelatesOverEveryPairInTheirOrder()
    {
        var (orders, customers) = Linked(composite: true);

        var sql = Sql("ctx.Customers.Where(c => c.Orders.Any(o => o.Total > 100))", orders, customers);

        Assert.Contains("o.CompanyID = c.CompanyID AND o.CustomerID = c.CustomerID", sql);
        Assert.Contains("AND o.Total > 100", sql);
    }

    [Fact]
    public void ThePairsOfTheOwningSideServeTheCollectionSide()
    {
        // A source that states the foreign key on the many-to-one alone - the usual shape of
        // an EF Core or JPA mapping - leaves the inverse relation without pairs; the pairs of
        // its counterpart are the same pairs (decision 101).
        var (orders, customers) = Linked(pairsOnInverse: false);

        var sql = Sql("ctx.Customers.Where(c => c.Orders.Any(o => o.Total > 100))", orders, customers);

        Assert.Contains("WHERE o.CustomerID = c.CustomerID AND o.Total > 100", sql);
    }

    [Fact]
    public void ASetOfTheContextKeepsTheCorrelationTheSourceWrote()
    {
        var (orders, customers) = Linked();

        var sql = Sql("ctx.Customers.Where(c => ctx.Orders.Any(o => o.CustomerID == c.CustomerID && o.Total > 100))", orders, customers);

        Assert.Contains("FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID AND o.Total > 100", sql);
        Assert.Equal(1, sql.Split("o.CustomerID = c.CustomerID").Length - 1);
    }

    [Fact]
    public void TheCorrelatedSubqueryGoesOutAsEfCoreAgain()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new EFCoreLinqQueryBuilder(), Method("ctx.Customers.Where(c => c.Orders.Any(o => o.Total > 100))"), orders, customers);

        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        var method = Normalized(artifacts.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
        Assert.Contains("o.CustomerID == c.CustomerID && o.Total > 100", method);
    }

    // ---- what the maps do not hold is refused by name --------------------------------------

    [Fact]
    public void ANavigationTheMappingDoesNotDeclareIsRefused()
    {
        var (orders, customers) = Linked();

        var record = Refusal("ctx.Customers.Where(c => c.Invoices.Any(i => i.Total > 100))", orders, customers);

        Assert.Equal(QueryFeature.Subquery, record.Feature);
        Assert.Contains("'c.Invoices', which names no association the mapping of 'Customer' declares", record.Reason);
        Assert.Contains("no artifact was generated", record.Reason);
    }

    [Fact]
    public void ARelationWithoutColumnPairsIsRefused()
    {
        var (orders, customers) = Linked(pairsOnInverse: false, pairsOnOwning: false);

        var record = Refusal("ctx.Customers.Where(c => c.Orders.Any(o => o.Total > 100))", orders, customers);

        Assert.Equal(QueryFeature.Subquery, record.Feature);
        Assert.Contains("states no foreign key columns and the database catalog supplied none", record.Reason);
    }

    [Fact]
    public void ATargetOutsideTheConversionIsRefused()
    {
        var (_, customers) = Linked();

        var record = Refusal("ctx.Customers.Where(c => c.Orders.Any(o => o.Total > 100))", customers);

        Assert.Equal(QueryFeature.Subquery, record.Feature);
        Assert.Contains("a navigation to the entity 'Order', which is not part of the conversion", record.Reason);
    }

    [Fact]
    public void ARowWithoutAMappingIsRefused()
    {
        var record = Refusal("ctx.Customers.Where(c => c.Orders.Any(o => o.Total > 100))");

        Assert.Equal(QueryFeature.Subquery, record.Feature);
        Assert.Contains("a navigation of the row 'c', whose entity mapping is not part of the conversion", record.Reason);
    }

    [Fact]
    public void AManyToManyNavigationIsRefusedAsTheTwoJoinsItWouldTake()
    {
        var (orders, customers) = Linked(inverseCardinality: Cardinality.ManyToMany);

        var record = Refusal("ctx.Customers.Where(c => c.Orders.Any(o => o.Total > 100))", orders, customers);

        Assert.Equal(QueryFeature.Subquery, record.Feature);
        Assert.Contains("the many-to-many relation to 'Order'", record.Reason);
    }
}
