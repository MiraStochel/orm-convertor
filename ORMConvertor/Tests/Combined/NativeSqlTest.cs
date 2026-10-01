using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EclipseLinkWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Enums;
using NHibernateWrappers;
using OrmConvertor;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// Native SQL in both directions (decision 113). As a target: what the query language of
/// EF Core, NHibernate, Hibernate or EclipseLink does not speak, the target writes whole in
/// the native SQL of its dialect - the text the Dapper target writes - and hands it to its
/// framework's API for native queries, with a record of kind Fallback; what that API cannot
/// take, and what a target without such an API meets, is refused. As a source: the native
/// SQL a unit hands over in code - SqlQuery and a whole FromSql in EF Core, CreateSQLQuery
/// in NHibernate, createNativeQuery in JPA - is read by the shared T-SQL reader, HQL handed
/// to CreateQuery by the HQL parser, a query handed over by its name or composed at run time
/// is named. And the two meet: what the escape path writes reads back as the same query.
/// The categories of T2 carry the escape path through the matrices and the fourth level
/// (<c>QueryShapeMatrixTest</c>, the differential matrix); this class names the rules.
/// </summary>
public class NativeSqlTest
{
    private static EntityMap Map(string entity, string table, params (string Name, ScalarType Type)[] columns)
    {
        var map = new EntityMap { Entity = new Entity { Name = entity }, Table = table };
        foreach (var (name, type) in columns)
        {
            var property = new Property { Name = name, Type = LangType.Scalar(type) };
            map.Entity.Properties.Add(property);
            map.PropertyMaps.Add(new PropertyMap { Property = property, ColumnName = name });
        }

        return map;
    }

    private static List<EntityMap> Maps() =>
    [
        Map("Customer", "Customers", ("CustomerId", ScalarType.Int), ("Name", ScalarType.String), ("CreditLimit", ScalarType.Decimal)),
        Map("Order", "Orders", ("OrderId", ScalarType.Int), ("CustomerId", ScalarType.Int), ("Total", ScalarType.Decimal)),
    ];

    private static AbstractQueryBuilder FromSql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = Maps();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
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

    private static string Artifact(List<ConversionSource> built, ConversionContentType type)
        => built.Single(s => s.ContentType == type).Content;

    /// <summary>The SQL the Dapper target writes from the same query - the text of every escape path.</summary>
    private static string DapperSql(string sql) => Artifact(Built(FromSql(new DapperSqlQueryBuilder(), sql)), ConversionContentType.SqlQuery);

    private static void AssertRefused(AbstractQueryBuilder builder, QueryFeature feature, string named)
    {
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature && r.Reason.Contains(named, StringComparison.Ordinal));
    }

    // Four shapes, each one a target's language does not speak.
    private const string Count = "SELECT COUNT(*) AS Customers FROM Customers AS c WHERE c.CreditLimit > @limit";

    private const string FullJoin = """
        SELECT c.Name AS Name, o.Total AS Total
        FROM Customers AS c
        FULL JOIN Orders AS o ON o.CustomerId = c.CustomerId
        """;

    private const string SliceInASubquery = """
        SELECT c.CustomerId AS CustomerId, c.Name AS Name
        FROM Customers AS c
        WHERE c.CustomerId IN (SELECT TOP (5) o.CustomerId FROM Orders AS o WHERE o.Total > @minTotal)
        """;

    private const string Intermediate = """
        WITH big AS (SELECT o.CustomerId AS CustomerId, SUM(o.Total) AS Spent FROM Orders AS o GROUP BY o.CustomerId)
        SELECT c.Name AS Name, big.Spent AS Spent
        FROM Customers AS c
        INNER JOIN big ON big.CustomerId = c.CustomerId
        """;

    private const string ListInASubquery = """
        SELECT c.CustomerId AS CustomerId
        FROM Customers AS c
        WHERE c.CustomerId IN (SELECT TOP (5) o.CustomerId FROM Orders AS o WHERE o.OrderId IN @ids)
        """;

    // ---- the escape path ------------------------------------------------------------

    /// <summary>The four targets with a query language of their own name the API they fall back on; the two whose language is the dialect's SQL have nothing to fall back from.</summary>
    [Fact]
    public void EveryTargetWithALanguageOfItsOwnNamesItsNativeApi()
    {
        Assert.Equal("DatabaseFacade.SqlQuery and DbSet.FromSql", EFCoreDescriptor.Instance.NativeSqlApi);
        Assert.Equal("ISession.CreateSQLQuery", NHibernateDescriptor.Instance.NativeSqlApi);
        Assert.Equal("EntityManager.createNativeQuery", HibernateDescriptor.Instance.NativeSqlApi);
        Assert.Equal("EntityManager.createNativeQuery", EclipseLinkDescriptor.Instance.NativeSqlApi);
        Assert.Null(DapperDescriptor.Instance.NativeSqlApi);
        Assert.Null(MyBatisWrappers.MyBatisDescriptor.Instance.NativeSqlApi);
    }

    /// <summary>
    /// A target whose descriptor names no API for native SQL refuses what its language does not
    /// speak, each construct named, as every target did before the decision - which is what a
    /// seventh framework without such an API meets without anything in AbstractWrappers
    /// changing (S1).
    /// </summary>
    [Fact]
    public void ATargetWithoutANativeApiRefusesWhatItsLanguageDoesNotSpeak()
    {
        var builder = new LanguageWithoutSetOperations { EntityMaps = Maps() };
        builder.Push();
        builder.From("Customers", "c");
        builder.Pop();
        builder.SetOperation(SetOperationType.Union);
        builder.Push();
        builder.From("Customers", "c");
        builder.Pop();

        AssertRefused(builder, QueryFeature.SetOperation, "has no API for a query in native SQL to fall back on");
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Fallback);
    }

    /// <summary>The record says what the language does not speak, which dialect the artifact is bound to, and which API it calls.</summary>
    [Fact]
    public void TheRecordNamesTheConstructTheDialectAndTheApi()
    {
        var builder = FromSql(new NHibernateHqlQueryBuilder(), FullJoin);
        Built(builder);

        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.Equal(QueryFeature.JoinKind, record.Feature);
        Assert.Contains("no full outer join", record.Reason, StringComparison.Ordinal);
        Assert.Contains(nameof(DatabaseDialect.SqlServer2022), record.Reason, StringComparison.Ordinal);
        Assert.Contains("ISession.CreateSQLQuery", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>A query the target's language speaks is never written in native SQL: the escape path is the third value of a cell, never passed off as the first.</summary>
    [Theory]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void AQueryTheLanguageSpeaksNeverFallsBack(ORMEnum target)
    {
        var builder = FromSql(Builder(target), "SELECT c.Name AS Name FROM Customers AS c WHERE c.CreditLimit > @limit ORDER BY c.Name ASC");
        var built = Built(builder);

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.DoesNotContain(built, s => s.ContentType == ConversionContentType.SqlQuery);
    }

    /// <summary>
    /// EF Core materializes the row of a native query into a class generated beside the
    /// method - a nullable property per column, named as the column comes back, a COUNT the
    /// int SQL Server answers with - and binds the parameters as the holes of the interpolated
    /// text. The method compiles in the consumer's frame, and EF Core translates it: the text
    /// comes back from ToQueryString unchanged, with the parameter EF Core made of the hole.
    /// </summary>
    [Fact]
    public void EFCoreWritesTheRowOfANativeQueryIntoAClassBesideTheMethod()
    {
        var builder = FromSql(new EFCoreLinqQueryBuilder(), Count);
        var method = Artifact(Built(builder), ConversionContentType.CSharpQuery);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Aggregation);
        Assert.Contains("public static IQueryable<QueryRow> Query(DbContext ctx, decimal limit)", method, StringComparison.Ordinal);
        Assert.Contains("return ctx.Database.SqlQuery<QueryRow>(", method, StringComparison.Ordinal);
        Assert.Contains("WHERE c.CreditLimit > {limit}", method, StringComparison.Ordinal);
        Assert.Contains("public sealed class QueryRow", method, StringComparison.Ordinal);
        Assert.Contains("public int? Customers { get; set; }", method, StringComparison.Ordinal);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "NativeSql_EFCore_Row",
            method,
            [CustomerEntity],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore;\nusing NativeSqlDomain;");

        var sql = EFCoreQueryAcceptance.Translate(compiled);
        Assert.Contains("SELECT COUNT(*) AS Customers", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE c.CreditLimit > @p0", sql, StringComparison.Ordinal);
    }

    private const string CustomerEntity = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace NativeSqlDomain;

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }

            public string Name { get; set; } = "";

            public decimal CreditLimit { get; set; }
        }
        """;

    /// <summary>A query over the whole of an entity goes through FromSql over its set, which materializes the entity.</summary>
    [Fact]
    public void EFCoreWritesAWholeEntityThroughFromSql()
    {
        const string sql = "SELECT * FROM Customers AS c WHERE c.CreditLimit > (SELECT o.Total FROM Orders AS o WHERE o.OrderId = 1)";

        var builder = FromSql(new EFCoreLinqQueryBuilder(), sql);
        var method = Artifact(Built(builder), ConversionContentType.CSharpQuery);

        Assert.Contains("public static IQueryable<Customer> Query(DbContext ctx)", method, StringComparison.Ordinal);
        Assert.Contains("return ctx.Set<Customer>().FromSql(", method, StringComparison.Ordinal);
        Assert.DoesNotContain("class QueryRow", method, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one shape the native API of EF Core cannot take: an interpolated collection is one
    /// value to it, a JSON text, not the list IN ranges over (measured against 10.0.10).
    /// EclipseLink's native query hands a list to the driver as one value as well (measured
    /// against 5.0.0). Both refuse, by name; Hibernate expands it (measured against 7.4.5) and
    /// NHibernate binds it by its own call, as in HQL.
    /// </summary>
    [Fact]
    public void AListParameterIsRefusedWhereTheNativeApiCannotExpandIt()
    {
        // LINQ speaks a slice inside a subquery, so EF Core falls back over a count instead.
        AssertRefused(
            FromSql(new EFCoreLinqQueryBuilder(), "SELECT COUNT(*) AS Orders FROM Orders AS o WHERE o.OrderId IN @ids"),
            QueryFeature.QueryParameter,
            "binds an interpolated collection as one value");
        AssertRefused(FromSql(new EclipseLinkJpqlQueryBuilder(), ListInASubquery), QueryFeature.QueryParameter, "does not expand a collection");

        var hibernate = Artifact(Built(FromSql(new HibernateJpqlQueryBuilder(), ListInASubquery)), ConversionContentType.JavaQuery);
        Assert.Contains("public static Query query(EntityManager em, Collection<Integer> ids)", hibernate, StringComparison.Ordinal);
        Assert.Contains("WHERE o.OrderId IN (?1)", hibernate, StringComparison.Ordinal);
        Assert.Contains(".setParameter(1, ids)", hibernate, StringComparison.Ordinal);

        var nhibernate = Artifact(Built(FromSql(new NHibernateHqlQueryBuilder(), ListInASubquery)), ConversionContentType.CSharpQuery);
        Assert.Contains("WHERE o.OrderId IN (:ids)", nhibernate, StringComparison.Ordinal);
        Assert.Contains(".SetParameterList(\"ids\", ids)", nhibernate, StringComparison.Ordinal);
    }

    /// <summary>
    /// The JPA targets bind by position, the form the specification gives a native query, and
    /// return Query whatever the result: Jakarta Persistence 3.2.0 declares createNativeQuery
    /// with a result class as returning Query, not TypedQuery.
    /// </summary>
    [Fact]
    public void TheJpaTargetsBindByPositionAndReturnQuery()
    {
        var method = Artifact(Built(FromSql(new HibernateJpqlQueryBuilder(), SliceInASubquery)), ConversionContentType.JavaQuery);

        Assert.StartsWith("public static Query query(EntityManager em, ", method, StringComparison.Ordinal);
        Assert.Contains(" minTotal) {", method, StringComparison.Ordinal);
        Assert.Contains("return em.createNativeQuery(\"\"\"", method, StringComparison.Ordinal);
        Assert.Contains("WHERE o.Total > ?1)", method, StringComparison.Ordinal);
        Assert.Contains(".setParameter(1, minTotal)", method, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the API materializes the rows into one entity - FromSql, AddEntity,
    /// createNativeQuery with the class - a set operation over two different entities would
    /// come back as the right one, so it is refused by name.
    /// </summary>
    [Fact]
    public void ASetOperationOverTwoEntitiesIsRefusedWhereTheApiMaterializesOne()
    {
        const string sql = "SELECT * FROM Customers AS c UNION SELECT * FROM Orders AS o";

        AssertRefused(FromSql(new NHibernateHqlQueryBuilder(), sql), QueryFeature.SetOperation, "AddEntity would materialize all of them");
    }

    // ---- reading native SQL handed over in code ---------------------------------------

    private static AbstractQueryBuilder FromEFCore(string csharp)
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        var read = new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharp, csharp, Maps());
        Assert.Single(read);
        return builder;
    }

    private static AbstractQueryBuilder FromNHibernate(string csharp)
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        var read = new NHibernateLinqQueryParser(() => builder).Parse(ConversionContentType.CSharp, csharp, Maps());
        Assert.Single(read);
        return builder;
    }

    private static AbstractQueryBuilder FromJpa(string java)
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        var read = new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.Java, java, Maps());
        Assert.Single(read);
        return builder;
    }

    [Fact]
    public void EFCoreSqlQueryIsReadAsTheQueryItHandsOver()
    {
        var builder = FromEFCore("""
            public IQueryable<Row> Rich(AppContext ctx, decimal limit)
                => ctx.Database.SqlQuery<Row>($"SELECT c.Name AS Name FROM Customers AS c WHERE c.CreditLimit > {limit}");
            """);

        Assert.Equal(
            DapperSql("SELECT c.Name AS Name FROM Customers AS c WHERE c.CreditLimit > @limit"),
            Artifact(Built(builder), ConversionContentType.SqlQuery));
    }

    [Fact]
    public void EFCoreSqlQueryRawIsReadWithItsFormatItems()
    {
        var builder = FromEFCore("""
            public IQueryable<Row> Rich(AppContext ctx, decimal limit)
                => ctx.Database.SqlQueryRaw<Row>("SELECT c.Name AS Name FROM Customers AS c WHERE c.CreditLimit > {0}", limit);
            """);

        Assert.Equal(
            DapperSql("SELECT c.Name AS Name FROM Customers AS c WHERE c.CreditLimit > @limit"),
            Artifact(Built(builder), ConversionContentType.SqlQuery));
    }

    /// <summary>A FromSql with nothing composed over it is the whole query; ToList() after it says nothing about the rows.</summary>
    [Fact]
    public void EFCoreFromSqlThatIsTheWholeQueryIsRead()
    {
        var builder = FromEFCore("""
            public List<Customer> Rich(AppContext ctx, decimal limit)
                => ctx.Set<Customer>().FromSql($"SELECT * FROM Customers AS c WHERE c.CreditLimit > {limit}").ToList();
            """);

        Assert.Equal(
            DapperSql("SELECT * FROM Customers AS c WHERE c.CreditLimit > @limit"),
            Artifact(Built(builder), ConversionContentType.SqlQuery));
    }

    /// <summary>LINQ composed over native SQL runs as EF Core's own subquery around it, a shape the escape path never writes and the reading refuses.</summary>
    [Fact]
    public void EFCoreCompositionOverNativeSqlIsRefused()
    {
        var builder = FromEFCore("""
            public IQueryable<Row> Rich(AppContext ctx)
                => ctx.Database.SqlQuery<Row>($"SELECT c.Name AS Name FROM Customers AS c").Where(r => r.Name != null);
            """);

        AssertRefused(builder, QueryFeature.Filtering, "composes Where() over the native SQL");
    }

    [Fact]
    public void EFCoreExecuteSqlIsNoQueryToRead()
    {
        var builder = FromEFCore("""
            public int Purge(AppContext ctx) => ctx.Database.ExecuteSql($"DELETE FROM Orders WHERE Total = 0");
            """);

        AssertRefused(builder, QueryFeature.Projection, "returns no rows");
    }

    /// <summary>A hole holding a value computed where it stands is no parameter of the model, so the text is not read.</summary>
    [Fact]
    public void EFCoreAValueComputedInAHoleIsNotRead()
    {
        var builder = FromEFCore("""
            public IQueryable<Row> Rich(AppContext ctx, decimal limit)
                => ctx.Database.SqlQuery<Row>($"SELECT c.Name AS Name FROM Customers AS c WHERE c.CreditLimit > {limit * 2}");
            """);

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("{limit * 2}", StringComparison.Ordinal));
    }

    /// <summary>
    /// NHibernate's native query: its <c>:name</c> respelled for the grammar, the parameter
    /// bound by SetParameterList read as the list it is, the slice set on the query object read
    /// into the query, and the declared scalars named as the result mapping the representation
    /// does not carry.
    /// </summary>
    [Fact]
    public void NHibernateCreateSqlQueryIsReadWithItsListAndItsSlice()
    {
        var builder = FromNHibernate("""
            public IQuery Some(ISession session, IEnumerable<int> ids)
            {
                return session.CreateSQLQuery("SELECT c.CustomerId AS CustomerId FROM Customers AS c WHERE c.CustomerId IN (:ids)")
                    .AddScalar("CustomerId", NHibernateUtil.Int32)
                    .SetParameterList("ids", ids)
                    .SetMaxResults(10);
            }
            """);

        var sql = Artifact(Built(builder), ConversionContentType.SqlQuery);

        Assert.Contains("SELECT TOP (10) c.CustomerId AS CustomerId", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE c.CustomerId IN @ids", sql, StringComparison.Ordinal);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("AddScalar", StringComparison.Ordinal));
    }

    /// <summary>HQL handed to CreateQuery in C# is read by the HQL parser of the wrapper, with the slice of the query object.</summary>
    [Fact]
    public void NHibernateCreateQueryIsReadAsHql()
    {
        var builder = FromNHibernate("""
            public IQuery Rich(ISession session, decimal limit)
                => session.CreateQuery("select c.Name from Customer c where c.CreditLimit > :limit")
                    .SetParameter("limit", limit)
                    .SetFirstResult(20)
                    .SetMaxResults(10);
            """);

        var sql = Artifact(Built(builder), ConversionContentType.SqlQuery);

        Assert.Contains("WHERE c.CreditLimit > @limit", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY", sql, StringComparison.Ordinal);
    }

    /// <summary>A query handed over by its name is read where its mapping defines it; the reference yields nothing, and says so instead of being silent.</summary>
    [Fact]
    public void NHibernateANamedQueryIsNamedAndNotRead()
    {
        var builder = FromNHibernate("""
            public IList<Customer> Rich(ISession session) => session.GetNamedQuery("RichCustomers").List<Customer>();
            """);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("\"RichCustomers\"", StringComparison.Ordinal));
    }

    [Fact]
    public void NHibernateAQueryComposedAtRunTimeIsRefused()
    {
        var builder = FromNHibernate("""
            public IList<Customer> Rich(ISession session) => session.QueryOver<Customer>().Where(c => c.CreditLimit > 100).List();
            """);

        AssertRefused(builder, QueryFeature.Projection, "QueryOver");
    }

    /// <summary>A query object kept in a variable and sliced in another statement may be sliced on a condition, so the slice is named and the query refused, as the JPA reading does.</summary>
    [Fact]
    public void NHibernateASliceSetInAnotherStatementIsRefused()
    {
        var builder = FromNHibernate("""
            public IList<object> Some(ISession session, bool paged)
            {
                var query = session.CreateSQLQuery("SELECT c.Name AS Name FROM Customers AS c");
                if (paged) { query.SetMaxResults(10); }
                return query.List<object>();
            }
            """);

        AssertRefused(builder, QueryFeature.Pagination, "kept in the variable 'query'");
    }

    /// <summary>
    /// JPA's native query: <c>?1</c> read as the positional parameter it is, a parameter bound
    /// to a variable the method declares as a collection read as the list it is, the slice of
    /// the query object read into the query.
    /// </summary>
    [Fact]
    public void JpaCreateNativeQueryIsReadWithPositionalParametersAListAndASlice()
    {
        var builder = FromJpa("""
            public static Query some(EntityManager em, Collection<Integer> ids, java.math.BigDecimal limit) {
                return em.createNativeQuery("SELECT * FROM Customers AS c WHERE c.CustomerId IN (?1) AND c.CreditLimit > ?2", Customer.class)
                    .setParameter(1, ids)
                    .setParameter(2, limit)
                    .setMaxResults(5);
            }
            """);

        var built = Built(builder);
        var sql = Artifact(built, ConversionContentType.SqlQuery);
        var method = Artifact(built, ConversionContentType.CSharpQuery);

        Assert.Contains("SELECT TOP (5) *", sql, StringComparison.Ordinal);
        Assert.Contains("c.CustomerId IN @p1 AND c.CreditLimit > @p2", sql, StringComparison.Ordinal);
        Assert.Contains("IEnumerable<int> p1, decimal p2", method, StringComparison.Ordinal);
    }

    [Fact]
    public void JpaANamedQueryIsNamedAndNotRead()
    {
        var builder = FromJpa("""
            public static Query rich(EntityManager em) {
                return em.createNamedQuery("Customer.rich");
            }
            """);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("'Customer.rich'", StringComparison.Ordinal));
    }

    /// <summary>A source that declares its SQL written for another database system is not read, here as for every SQL (decision 088).</summary>
    [Fact]
    public void ADeclaredForeignDialectStopsTheReadingOfNativeSql()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [
                .. CrossFrameworkInputs.MappingUnits(ORMEnum.EFCore),
                new ConversionSource
                {
                    ContentType = ConversionContentType.CSharp,
                    Content = "public IQueryable<Row> Rich(AppContext ctx) => ctx.Database.SqlQuery<Row>($\"SELECT c.CustomerName AS Name FROM Sales.Customers AS c\");",
                },
            ],
            catalogReader: null,
            declaredSourceDialect: SourceSqlDialect.AnotherSystem);

        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Artifact == ConversionContentType.SqlQuery);
    }

    // ---- the round trip ---------------------------------------------------------------

    /// <summary>
    /// What the escape path writes reads back as the same query (decision 113): the target's
    /// own reading of its own artifact gives the statement the Dapper target writes from the
    /// source. So the direction target to source holds for an artifact that fell back, too.
    /// The JPA rows bind nothing, because a native query of JPA binds by position and a named
    /// parameter of the source would come back positional - the same value, by another road.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, Count)]
    [InlineData(ORMEnum.NHibernate, FullJoin)]
    [InlineData(ORMEnum.NHibernate, Intermediate)]
    [InlineData(ORMEnum.Hibernate, "SELECT c.Name AS Name FROM Customers AS c WHERE c.CustomerId IN (SELECT TOP (5) o.CustomerId FROM Orders AS o)")]
    [InlineData(ORMEnum.EclipseLink, Intermediate)]
    public void TheOutputOfTheEscapePathReadsBackAsTheSameQuery(ORMEnum target, string sql)
    {
        var builder = FromSql(Builder(target), sql);
        var built = Built(builder);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback);

        var method = Artifact(built, target is ORMEnum.Hibernate or ORMEnum.EclipseLink ? ConversionContentType.JavaQuery : ConversionContentType.CSharpQuery);
        var reread = target switch
        {
            ORMEnum.EFCore => FromEFCore(method),
            ORMEnum.NHibernate => FromNHibernate(method),
            _ => FromJpa(method),
        };

        Assert.Equal(DapperSql(sql), Artifact(Built(reread), ConversionContentType.SqlQuery));
    }

    // ---- the role of a class ----------------------------------------------------------

    /// <summary>
    /// A class that hands its provider native SQL alone holds code around a query, not an
    /// entity: the recognizer of decision 111 sees the hand-over as it sees a LINQ chain, so
    /// the class does not come out as an entity of the conversion.
    /// </summary>
    [Fact]
    public void AClassThatHandsOverNativeSqlAloneIsNoEntity()
    {
        const string file = """
            using Microsoft.EntityFrameworkCore;

            namespace Sales;

            public class Customer
            {
                public int CustomerId { get; set; }

                public string CustomerName { get; set; } = "";

                public decimal CreditLimit { get; set; }
            }

            public class CustomerRow
            {
                public string? Name { get; set; }
            }

            public class CustomerQueries
            {
                public IQueryable<CustomerRow> Rich(DbContext ctx)
                    => ctx.Database.SqlQuery<CustomerRow>($"SELECT c.CustomerName AS Name FROM Customers AS c WHERE c.CreditLimit > 1000");
            }
            """;

        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.EFCore,
            [new ConversionSource { ContentType = ConversionContentType.CSharp, Content = file }]);

        var entities = result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content).ToList();

        Assert.DoesNotContain(entities, entity => entity.Contains("class CustomerQueries", StringComparison.Ordinal));
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery);
    }

    // ---- helpers ----------------------------------------------------------------------

    private static AbstractQueryBuilder Builder(ORMEnum target) => target switch
    {
        ORMEnum.EFCore => new EFCoreLinqQueryBuilder(),
        ORMEnum.NHibernate => new NHibernateHqlQueryBuilder(),
        ORMEnum.Hibernate => new HibernateJpqlQueryBuilder(),
        ORMEnum.EclipseLink => new EclipseLinkJpqlQueryBuilder(),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    /// <summary>
    /// A target whose language has no set operation and whose framework has no API for native
    /// SQL: the case of a seventh framework the descriptor alone decides.
    /// </summary>
    private sealed class LanguageWithoutSetOperations : AbstractQueryBuilder
    {
        public override TargetFrameworkDescriptor Descriptor { get; } = new()
        {
            Framework = ORMEnum.Dapper,
            Ecosystem = Ecosystem.DotNet,
            Version = "1.0.0",
            Dialect = DatabaseDialect.SqlServer2022,
            Support = DapperDescriptor.Instance.Support,
            QuerySupport = Enum.GetValues<QueryFeature>().ToDictionary(
                feature => feature,
                feature => feature == QueryFeature.SetOperation ? FactSupport.NotExpressible : FactSupport.Expressible),
            Functions = QueryFunctionVocabulary.All,
            NativeSqlApi = null,
        };

        protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact) => artifact.Source.Append(clauses.From.Table);

        protected override void BuildJoins(QueryClauses clauses, QueryArtifact artifact) { }

        protected override void BuildFilter(QueryClauses clauses, QueryArtifact artifact) { }

        protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact) { }

        protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact) { }

        protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact) { }

        protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact) { }

        protected override void BuildPagination(QueryClauses clauses, QueryArtifact artifact) { }

        protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact)
            => [new() { Content = artifact.Source.ToString(), ContentType = ConversionContentType.SqlQuery }];
    }
}
