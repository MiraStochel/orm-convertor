using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;

namespace Tests.Hibernate;

/// <summary>
/// The JPQL parser of decision 077. The strongest assertion is the round-trip identity:
/// the bare JPQL the builder emits, read back and rebuilt, is the same text - what pins
/// the parser's grammar to the builder's language, as decision 062 did for HQL.
/// </summary>
public class HibernateJpqlQueryParserTest
{
    /// <summary>The column deliberately differs from the property, to prove both name mappings.</summary>
    private static EntityMap Customers()
    {
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var limit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal) };
        var opened = new Property { Name = "OpenedOn", Type = LangType.Scalar(ScalarType.Date) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [name, limit, opened] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
                new PropertyMap { Property = limit, ColumnName = "CreditLimitAmount" },
                new PropertyMap { Property = opened, ColumnName = "OpenedOn" },
            ],
        };
    }

    private static EntityMap Orders()
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [id, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = total, ColumnName = "Total" },
            ],
        };
    }

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string jpql, params EntityMap[] maps)
    {
        builder.EntityMaps = maps;
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, maps);
        return builder;
    }

    private static (string Jpql, IReadOnlyList<ConversionRecord> Records) RoundTrip(string jpql, params EntityMap[] maps)
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), jpql, maps);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return (artifacts.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content, builder.Records);
    }

    private static void AssertFixedPoint(string jpql, params EntityMap[] maps)
        => Assert.Equal(jpql, RoundTrip(jpql, maps).Jpql, ignoreLineEndingDifferences: true);

    /* ---- round-trip identity -------------------------------------------------------- */

    [Fact]
    public void ProjectionFilterAndOrderingRoundTrip()
        => AssertFixedPoint("""
            select c.CustomerName as Name
            from Customer c
            where c.CreditLimit > 2000
            order by c.CustomerName desc
            """, Customers());

    [Fact]
    public void AWholeEntityProjectionRoundTrips()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.CustomerName like 'A%'
            """, Customers());

    [Fact]
    public void AnEntityJoinRoundTrips()
        => AssertFixedPoint("""
            select o
            from Order o
                join Customer c on o.CustomerID = c.CustomerID
            where c.CreditLimit > 2000
            """, Customers(), Orders());

    [Fact]
    public void ALeftJoinRoundTrips()
        => AssertFixedPoint("""
            select o.Total
            from Order o
                left join Customer c on o.CustomerID = c.CustomerID
            """, Customers(), Orders());

    [Fact]
    public void AggregationGroupingAndHavingRoundTrip()
        => AssertFixedPoint("""
            select o.CustomerID, count(o) as n, sum(o.Total) as total
            from Order o
            group by o.CustomerID
            having count(o) > 5
            """, Orders());

    [Fact]
    public void SubQueriesRoundTrip()
        => AssertFixedPoint("""
            select o
            from Order o
            where o.CustomerID in (select c.CustomerID from Customer c where c.CreditLimit > 2000) and exists (select x from Customer x where x.CustomerID = o.CustomerID)
            """, Customers(), Orders());

    [Fact]
    public void AValueListNullTestsAndNegationRoundTrip()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.CreditLimit in (1000, 2500.50) and c.CustomerName is not null and not (c.CustomerName = 'x')
            """, Customers());

    [Fact]
    public void DistinctRoundTrips()
        => AssertFixedPoint("""
            select distinct c.CustomerName
            from Customer c
            """, Customers());

    [Fact]
    public void ATemporalLiteralRoundTripsInTheJdbcEscapeSyntax()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.OpenedOn >= {d '2025-01-01'}
            """, Customers());

    [Fact]
    public void ASetOperationRoundTrips()
        => AssertFixedPoint("""
            select c.CustomerName from Customer c where c.CreditLimit > 2000
            union
            select c.CustomerName from Customer c where c.CreditLimit < 100
            """, Customers());

    [Fact]
    public void MixedAndOrIsParenthesized()
    {
        var (jpql, _) = RoundTrip("""
            select c
            from Customer c
            where (c.CreditLimit > 2000 or c.CreditLimit < 10) and c.CustomerName like 'A%'
            """, Customers());

        Assert.Contains("where (c.CreditLimit > 2000 or c.CreditLimit < 10) and c.CustomerName like 'A%'", jpql);
    }

    /* ---- what the model rewrites --------------------------------------------------- */

    [Fact]
    public void BetweenBecomesTwoComparisons()
    {
        var (jpql, records) = RoundTrip("select c from Customer c where c.CreditLimit between 10 and 20", Customers());

        Assert.Contains("where c.CreditLimit >= 10 and c.CreditLimit <= 20", jpql);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("between"));
    }

    /// <summary>The fourth hook of decision 076: HQL's limit and offset become the pagination of the scope.</summary>
    [Fact]
    public void HqlLimitAndOffsetBecomePagination()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c order by c.CustomerName limit 5 offset 10", Customers());
        var artifacts = builder.Build();

        var method = artifacts.Single(a => a.ContentType == ConversionContentType.JavaQuery).Content;
        Assert.Contains(".setFirstResult(10)", method);
        Assert.Contains(".setMaxResults(5)", method);
        Assert.DoesNotContain("limit", artifacts.Single(a => a.ContentType == ConversionContentType.JpqlQuery).Content);
    }

    [Fact]
    public void AConstructorExpressionKeepsItsColumnsAndDropsTheClass()
    {
        var (jpql, records) = RoundTrip("select new Shop.Dto(c.CustomerName, c.CreditLimit) from Customer c", Customers());

        Assert.StartsWith("select c.CustomerName, c.CreditLimit", jpql);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("Shop.Dto"));
    }

    [Fact]
    public void TheJavaMethodIsReadThroughItsLiteral()
    {
        var builder = new HibernateJpqlQueryBuilder { EntityMaps = [Customers()] };
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JavaQuery, SampleData.CustomerSampleHibernate.Query, [Customers()]);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("where c.CreditLimit > 2000", artifacts.Single(a => a.ContentType == ConversionContentType.JpqlQuery).Content);
    }

    /* ---- refusals (decision 070) ---------------------------------------------------- */

    [Fact]
    public void ASyntaxErrorRefusesWithAPosition()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c where", Customers());

        Assert.Empty(builder.Build());
        var failure = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("line 1, column", failure.Reason);
    }

    [Fact]
    public void AParameterRefusesUnderItsOwnCategory()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c where c.CreditLimit > :limit", Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.QueryParameter && r.Reason.Contains(":limit"));
    }

    [Fact]
    public void AnAssociationJoinRefuses()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o join o.customer c where c.CreditLimit > 1", Orders(), Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("o.customer"));
    }

    [Fact]
    public void ACrossJoinRefuses()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o, Customer c", Orders(), Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join);
    }

    [Fact]
    public void AQueryComposedAtRunTimeIsAnIncompleteness()
    {
        var builder = new HibernateJpqlQueryBuilder();
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JavaQuery, """
            public static Query query(EntityManager em, String where) {
                return em.createQuery("select c from Customer c " + where);
            }
            """);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("composed at run time"));
    }

    [Fact]
    public void TheParserClaimsBothQueryLanguages()
    {
        var parser = new HibernateJpqlQueryParser(() => new HibernateJpqlQueryBuilder());

        Assert.True(parser.CanParse(ConversionContentType.JpqlQuery));
        Assert.True(parser.CanParse(ConversionContentType.JavaQuery));
        Assert.False(parser.CanParse(ConversionContentType.HqlQuery));
        Assert.False(parser.CanParse(ConversionContentType.CSharpQuery));
    }

    /// <summary>
    /// The instruction list the parser produces, checked once directly: names go through
    /// the mapping IR to the column the query builder of another target needs.
    /// </summary>
    [Fact]
    public void NamesGoThroughTheMappingToColumns()
    {
        var builder = new DummyQueryBuilder { EntityMaps = [Customers()] };
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, "select c.CreditLimit from Customer c where c.CreditLimit > 1", [Customers()]);

        var body = ((SubQueryInstruction)builder.Instructions.Single()).Instructions;
        var from = Assert.Single(body.OfType<FromInstruction>());
        Assert.Equal("Sales.Customers", from.Table);
        Assert.Equal("CreditLimitAmount", Assert.Single(body.OfType<ProjectInstruction>()).Attribute);
    }

    private sealed class DummyQueryBuilder : AbstractQueryBuilder
    {
        public override TargetFrameworkDescriptor Descriptor => HibernateDescriptor.Instance;

        public IReadOnlyList<QueryInstruction> Instructions => instructions;

        protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildJoins(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildFilter(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildPagination(QueryClauses clauses, QueryArtifact artifact) { }
        protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact) => [];
    }
}
