using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// Level 1 of decision 016 for the query branch: what each target actually writes. Only this
/// level can assert <em>how</em> a query is written, which is what S2 needs.
/// </summary>
public class QueryTargetShapeTest
{
    private static EntityMap CustomerMap()
    {
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var limit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [name, limit] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
                new PropertyMap { Property = limit, ColumnName = "CreditLimitAmount" },
            ],
        };
    }

    private static void Record(AbstractQueryBuilder builder)
    {
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Project("c", "CustomerName", "Name");
        builder.Where(new ComparisonCondition(
            QueryOperand.Column("c", "CreditLimitAmount"),
            ComparisonOperator.GreaterThan,
            QueryOperand.Value(QueryConstant.Of("2000", ScalarType.Decimal))));
        builder.OrderBy("c", "CustomerName", asc: false);
        builder.Pop();
    }

    private static string Emit(AbstractQueryBuilder builder, ConversionContentType type)
    {
        builder.EntityMaps = [CustomerMap()];
        Record(builder);
        return builder.Build().Single(s => s.ContentType == type).Content;
    }

    [Fact]
    public void DapperWritesSqlOverTablesAndColumns()
    {
        var sql = Emit(new DapperSqlQueryBuilder(), ConversionContentType.SqlQuery);

        string expected = """
        SELECT c.CustomerName AS Name
        FROM Sales.Customers AS c
        WHERE c.CreditLimitAmount > 2000
        ORDER BY c.CustomerName DESC
        """;

        Assert.Equal(expected, sql, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// HQL names entities and properties, so the column CreditLimitAmount comes back out as
    /// the property CreditLimit and the qualified table name never appears.
    /// </summary>
    [Fact]
    public void NHibernateWritesHqlOverEntitiesAndProperties()
    {
        var hql = Emit(new NHibernateHqlQueryBuilder(), ConversionContentType.HqlQuery);

        string expected = """
        select c.CustomerName as Name
        from Customer c
        where c.CreditLimit > 2000
        order by c.CustomerName desc
        """;

        Assert.Equal(expected, hql, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// The LINQ chain runs in relational evaluation order, so the projection comes last -
    /// the very difference decision 023's step order is built around.
    /// </summary>
    [Fact]
    public void EFCoreWritesALinqChainInEvaluationOrder()
    {
        var linq = Emit(new EFCoreLinqQueryBuilder(), ConversionContentType.CSharpQuery);

        string expected = """
        public static IQueryable Query(DbContext ctx)
        {
            return ctx.Set<Customer>()
                .Where(c => c.CreditLimit > 2000m)
                .OrderByDescending(c => c.CustomerName)
                .Select(c => new { Name = c.CustomerName });
        }
        """;

        Assert.Equal(expected, linq, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// The same constant is written three ways, which is the point of the typed operand
    /// (decision 024): SQL and HQL take a bare number, C# takes the decimal suffix back.
    /// </summary>
    [Fact]
    public void AStringConstantIsQuotedForEachTargetLanguage()
    {
        static void RecordName(AbstractQueryBuilder builder)
        {
            builder.Push();
            builder.From("Sales.Customers", alias: "c");
            builder.Where(new ComparisonCondition(
                QueryOperand.Column("c", "CustomerName"),
                ComparisonOperator.Equal,
                QueryOperand.Value(QueryConstant.Of("O'Brien", ScalarType.String))));
            builder.Pop();
        }

        var dapper = new DapperSqlQueryBuilder { EntityMaps = [CustomerMap()] };
        RecordName(dapper);
        Assert.Contains("= 'O''Brien'", dapper.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content);

        var nhibernate = new NHibernateHqlQueryBuilder { EntityMaps = [CustomerMap()] };
        RecordName(nhibernate);
        Assert.Contains("= 'O''Brien'", nhibernate.Build().Single(s => s.ContentType == ConversionContentType.HqlQuery).Content);

        var efcore = new EFCoreLinqQueryBuilder { EntityMaps = [CustomerMap()] };
        RecordName(efcore);
        Assert.Contains("== \"O'Brien\"", efcore.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
    }

    /// <summary>
    /// Grouping changes what a LINQ lambda parameter holds, which is why the projection step
    /// has to run after grouping rather than where SQL writes it.
    /// </summary>
    [Fact]
    public void EFCoreProjectsAGroupingThroughKeyAndAggregates()
    {
        var builder = new EFCoreLinqQueryBuilder { EntityMaps = [CustomerMap()] };

        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.GroupBy("c", "CustomerName");
        builder.Project("c", "CustomerName", "Name");
        builder.Project("c", "CreditLimitAmount", "Total", "SUM");
        builder.Pop();

        var linq = builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains(".GroupBy(c => c.CustomerName)", linq);
        Assert.Contains("Name = g.Key", linq);
        Assert.Contains("Total = g.Sum(c => c.CreditLimit)", linq);
    }

    /// <summary>
    /// NHibernate 5.7.0 has no set operations in HQL, and the descriptor says so, so the
    /// mechanical capability check refuses rather than emitting something invalid.
    /// </summary>
    [Fact]
    public void NHibernateRefusesASetOperationWithARecord()
    {
        var builder = new NHibernateHqlQueryBuilder { EntityMaps = [CustomerMap()] };

        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Pop();
        builder.SetOperation(SetOperationType.Union);
        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Pop();

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Feature == AbstractWrappers.Descriptors.QueryFeature.SetOperation);
    }
}

/// <summary>
/// The most distant pair of the matrix: T-SQL is read by a language parser and written back
/// out as HQL, which names entities and properties. Nothing of the source's own vocabulary -
/// schema, table, column - survives into the output, which is what makes it a translation
/// rather than a copy.
/// </summary>
public class DapperSqlToNHibernateHqlTest
{
    private const string Entity = """
        namespace Shop;

        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
            public virtual decimal CreditLimit { get; set; }
        }
        """;

    private const string Mapping = """
        <?xml version="1.0" encoding="utf-8"?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Shop">
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32">
              <generator class="identity" />
            </id>
            <property name="CustomerName" column="CustomerNm" type="String" />
            <property name="CreditLimit" column="CreditLimitAmount" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;

    private const string Sql = """
        SELECT c.CustomerNm
        FROM Sales.Customers AS c
        WHERE c.CreditLimitAmount > 2000
        ORDER BY c.CustomerNm ASC
        """;

    [Fact]
    public void ColumnsBecomePropertiesAndTheTableBecomesAnEntity()
    {
        // The mapping is read only to establish the column-to-property correspondence the
        // HQL builder has to invert; the query itself arrives as Dapper SQL.
        var builder = new NHibernateHqlQueryBuilder { EntityMaps = [.. ReadMaps()] };
        new DapperWrappers.DapperSqlQueryParser(builder).Parse(Sql);

        var hql = builder.Build().Single(s => s.ContentType == Model.ConversionContentType.HqlQuery).Content;

        string expected = """
        select c.CustomerName
        from Customer c
        where c.CreditLimit > 2000
        order by c.CustomerName asc
        """;

        Assert.Equal(expected, hql, ignoreLineEndingDifferences: true);

        static IEnumerable<Model.AbstractRepresentation.EntityMap> ReadMaps()
        {
            var entityBuilder = new NHibernateEntityBuilder();
            new NHibernateEntityParser(entityBuilder).Parse(Entity);
            new NHibernateXMLMappingParser(entityBuilder).Parse(Mapping);
            return entityBuilder.EntityMaps;
        }
    }
}
