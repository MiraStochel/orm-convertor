using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using HibernateWrappers;
using Model;
using OrmConvertor;
using Tests.Combined;

namespace Tests.Hibernate;

/// <summary>
/// The named queries of JPA, read where they are defined (decision 143): on a class of a
/// Java unit - <c>@NamedQuery</c>, its container, the native pair - and in an orm.xml, on
/// the root or inside an <c>&lt;entity&gt;</c>. The unit is a mapping and a query at once
/// (decision 081), so the query pass reads it a second time and the entity pass stops
/// reporting the definition as a loss. What the definition states beside its text is
/// answered as the calls on a query object are: a hint that changes the rows refuses the
/// query under the implementation that reads it, a lock mode is a loss, a statement that
/// writes is refused by name, and the reference - <c>createNamedQuery</c> - yields no
/// query of its own.
/// </summary>
public class JpaNamedQueryTest
{
    private const string OrmXmlNamespace = "https://jakarta.ee/xml/ns/persistence/orm";

    /// <summary>
    /// The matrix's own JPA entity with the given annotations written above its header, and
    /// an orm.xml beside it where a test sends one.
    /// </summary>
    private static ConversionResult Convert(
        ORMEnum source,
        string classAnnotations,
        string? ormXml = null,
        ORMEnum target = ORMEnum.Dapper,
        params ConversionSource[] more)
    {
        var entity = CrossFrameworkInputs.MappingUnits(source).Single(u => u.ContentType == ConversionContentType.Java);

        var units = new List<ConversionSource>
        {
            new()
            {
                Name = "Customer.java",
                ContentType = ConversionContentType.Java,
                Content = entity.Content.Replace("@Entity", classAnnotations + Environment.NewLine + "@Entity"),
            },
        };

        if (ormXml is not null)
        {
            units.Add(new() { Name = "orm.xml", ContentType = ConversionContentType.XML, Content = ormXml });
        }

        units.AddRange(more);

        return ConversionHandler.Convert(source, target, units);
    }

    private static List<string> QueryArtifacts(ConversionResult result, ConversionContentType type = ConversionContentType.CSharpQuery)
        => [.. result.Sources.Where(s => s.ContentType == type).Select(s => s.Content)];

    private static void AssertNoFailure(ConversionResult result)
        => Assert.True(
            !result.Records.Any(r => r.Kind == ConversionRecordKind.Failure),
            string.Join(Environment.NewLine, result.Records.Where(r => r.Kind == ConversionRecordKind.Failure).Select(r => $"{r.Query}: {r.Reason}")));

    /// <summary>The entity pass used to report the annotation as a loss; now the query pass reads it and nothing says it was dropped.</summary>
    private static void AssertTheDefinitionIsNotALoss(ConversionResult result)
        => Assert.DoesNotContain(
            result.Records,
            r => r.Kind == ConversionRecordKind.Loss
                && (r.Reason.Contains("NamedQuer", StringComparison.Ordinal) || r.Reason.Contains("named-query", StringComparison.Ordinal) || r.Reason.Contains("named-native-query", StringComparison.Ordinal)));

    /* ---- the annotation ------------------------------------------------------------- */

    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void TheQueriesOfAContainerAreReadEachUnderItsName(ORMEnum source)
    {
        var result = Convert(source, """
            @NamedQueries({
                @NamedQuery(name = "Customer.all", query = "select c from Customer c"),
                @NamedQuery(name = "Customer.rich", query = "select c.CustomerName from Customer c where c.CreditLimit > :limit")
            })
            """);

        AssertNoFailure(result);
        AssertTheDefinitionIsNotALoss(result);

        var methods = QueryArtifacts(result);
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("CustomerAll(", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("CustomerRich(IDbConnection connection, decimal limit)", StringComparison.Ordinal));
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);
    }

    /// <summary>The annotation is repeatable since Jakarta Persistence 2.2, and the bare repetition reads as the container does.</summary>
    [Fact]
    public void ARepeatedAnnotationIsReadWithoutItsContainer()
    {
        var result = Convert(ORMEnum.Hibernate, """
            @NamedQuery(name = "Customer.all", query = "select c from Customer c")
            @NamedQuery(name = "Customer.names", query = "select c.CustomerName from Customer c")
            """);

        AssertNoFailure(result);
        Assert.Equal(2, QueryArtifacts(result).Count);
    }

    /// <summary>A Java target spells the source's name its own way, as it does for an hbm.xml query.</summary>
    [Fact]
    public void AJavaTargetSpellsTheNameInCamelCase()
    {
        var result = Convert(ORMEnum.Hibernate, """
            @NamedQuery(name = "Customer.all", query = "select c from Customer c")
            """, target: ORMEnum.EclipseLink);

        AssertNoFailure(result);
        Assert.Contains("customerAll(EntityManager em)", QueryArtifacts(result, ConversionContentType.JavaQuery).Single(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every record of a named query names it, which is the only thing telling two refused
    /// queries of one class apart; the neighbour, read into a builder of its own, is untouched.
    /// </summary>
    [Fact]
    public void TheRecordsOfARefusedQueryNameItAndItsNeighbourIsUntouched()
    {
        var result = Convert(ORMEnum.Hibernate, """
            @NamedQueries({
                @NamedQuery(name = "Customer.all", query = "select c from Customer c"),
                @NamedQuery(name = "Customer.window", query = "select c.CustomerName from Customer c where :floor > :ceiling")
            })
            """);

        var refused = result.Records.Where(r => r.Kind == ConversionRecordKind.Failure).ToList();

        Assert.NotEmpty(refused);
        Assert.All(refused, r =>
        {
            Assert.Equal("Customer.window", r.Query);
            Assert.Equal("Customer.java", r.Unit);
        });
        Assert.Single(QueryArtifacts(result));
    }

    /// <summary>
    /// A named query that writes is refused by name as a bulk statement (decisions 053 and
    /// 143), the answer the SQL and MyBatis readings give a statement that is not a query -
    /// not a syntax error on its first token.
    /// </summary>
    [Theory]
    [InlineData("delete from Customer c where c.CreditLimit < 0", "delete")]
    [InlineData("update Customer c set c.CreditLimit = 0 where c.CreditLimit < 0", "update")]
    public void AQueryThatWritesIsRefusedAsABulkStatement(string jpql, string statement)
    {
        var result = Convert(ORMEnum.Hibernate, $$"""
            @NamedQueries({
                @NamedQuery(name = "Customer.all", query = "select c from Customer c"),
                @NamedQuery(name = "Customer.purge", query = "{{jpql}}")
            })
            """);

        var refused = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("Customer.purge", refused.Query);
        Assert.Contains($"bulk {statement} statement", refused.Reason, StringComparison.Ordinal);
        Assert.Single(QueryArtifacts(result));
    }

    /// <summary>
    /// The native form goes through the shared T-SQL reader as createNativeQuery does
    /// (decision 113): <c>?1</c> is the positional parameter, typed by the gate; the result
    /// class is what every target derives anyway and says nothing, the result set mapping is
    /// a loss of the projection.
    /// </summary>
    [Fact]
    public void ANativeNamedQueryIsReadBySharedReaderAndItsResultSetMappingIsALoss()
    {
        var result = Convert(ORMEnum.Hibernate, """
            @NamedNativeQuery(name = "Customer.raw", query = "SELECT c.CustomerName AS Name FROM Sales.Customers AS c WHERE c.CreditLimit > ?1", resultClass = Customer.class, resultSetMapping = "rows")
            """);

        AssertNoFailure(result);

        var sql = result.Sources.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;
        Assert.Contains("c.CreditLimit > @p1", sql, StringComparison.Ordinal);
        Assert.Contains("CustomerRaw(IDbConnection connection, decimal p1)", QueryArtifacts(result).Single(), StringComparison.Ordinal);
        Assert.Contains(
            result.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Projection && r.Query == "Customer.raw" && r.Reason.Contains("'rows'", StringComparison.Ordinal));
    }

    /// <summary>
    /// A hint of the definition is answered by the implementation's vocabulary, as a setHint
    /// call is: EclipseLink caps the rows under this key, so the query is refused there, while
    /// Hibernate ignores the key and the hint is a loss.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EclipseLink, ConversionRecordKind.Failure)]
    [InlineData(ORMEnum.Hibernate, ConversionRecordKind.Loss)]
    public void AHintThatChangesTheRowsRefusesTheQueryWhereTheImplementationReadsIt(ORMEnum source, ConversionRecordKind expected)
    {
        var result = Convert(source, """
            @NamedQuery(name = "Customer.few", query = "select c from Customer c", hints = { @QueryHint(name = "eclipselink.jdbc.max-rows", value = "10") })
            """);

        var record = Assert.Single(result.Records, r => r.Reason.Contains("\"eclipselink.jdbc.max-rows\"", StringComparison.Ordinal));
        Assert.Equal(expected, record.Kind);
        Assert.Equal("Customer.few", record.Query);
        Assert.Equal(expected == ConversionRecordKind.Failure ? 0 : 1, QueryArtifacts(result).Count);
    }

    [Fact]
    public void ALockModeIsALossAndTheQueryIsRead()
    {
        var result = Convert(ORMEnum.Hibernate, """
            @NamedQuery(name = "Customer.locked", query = "select c from Customer c", lockMode = LockModeType.PESSIMISTIC_READ)
            """);

        AssertNoFailure(result);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("lock mode PESSIMISTIC_READ", StringComparison.Ordinal));
        Assert.Single(QueryArtifacts(result));
    }

    /// <summary>Every form requires the name, so a definition without one is malformed input, not a query under the fixed name.</summary>
    [Fact]
    public void ANamelessDefinitionIsRefused()
    {
        var result = Convert(ORMEnum.Hibernate, """
            @NamedQuery(query = "select c from Customer c")
            """);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("@NamedQuery", StringComparison.Ordinal) && r.Reason.Contains("carries none", StringComparison.Ordinal));
        Assert.Empty(QueryArtifacts(result));
    }

    /// <summary>
    /// The reference yields no query of its own: the definition is where the query is read,
    /// so a unit calling createNamedQuery beside the entity that defines the query gives one
    /// artifact and a record saying so - and, as every handover that yields no query, the
    /// builder of the reference is refused as carrying no instructions (decision 113), which
    /// is the one failure of the conversion.
    /// </summary>
    [Fact]
    public void TheReferenceYieldsNoQueryBesideItsDefinition()
    {
        var result = Convert(ORMEnum.Hibernate, """
            @NamedQuery(name = "Customer.all", query = "select c from Customer c")
            """, more: new ConversionSource
            {
                Name = "CustomerQueries.java",
                ContentType = ConversionContentType.Java,
                Content = """
                    import jakarta.persistence.EntityManager;
                    import jakarta.persistence.TypedQuery;

                    public class CustomerQueries {
                        public static TypedQuery<Customer> all(EntityManager em) {
                            return em.createNamedQuery("Customer.all", Customer.class);
                        }
                    }
                    """,
            });

        Assert.Single(QueryArtifacts(result));
        Assert.Contains(
            result.Records,
            r => r.Kind == ConversionRecordKind.Incompleteness && r.Unit == "CustomerQueries.java" && r.Reason.Contains("'Customer.all'", StringComparison.Ordinal) && r.Reason.Contains("read where it is defined", StringComparison.Ordinal));

        var refused = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("CustomerQueries.java", refused.Unit);
        Assert.Contains("carries no instructions", refused.Reason, StringComparison.Ordinal);
    }

    /* ---- orm.xml -------------------------------------------------------------------- */

    /// <summary>
    /// Both places the schema gives a definition - the root and the entity - and both forms;
    /// the mapping half of the document is read by the entity pass as before and the element
    /// is no longer among its losses.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void TheNamedQueriesOfAnOrmXmlAreReadOnTheRootAndInsideTheEntity(ORMEnum source)
    {
        var result = Convert(source, string.Empty, $"""
            <entity-mappings xmlns="{OrmXmlNamespace}" version="3.2">
                <named-query name="Customer.all">
                    <description>Every customer.</description>
                    <query>select c from Customer c</query>
                    <hint name="jakarta.persistence.query.timeout" value="500"/>
                </named-query>
                <entity class="Customer">
                    <named-native-query name="Customer.raw" result-class="Customer" result-set-mapping="rows">
                        <query><![CDATA[SELECT c.CustomerName AS Name FROM Sales.Customers AS c WHERE c.CreditLimit > :limit]]></query>
                    </named-native-query>
                </entity>
            </entity-mappings>
            """);

        AssertNoFailure(result);
        AssertTheDefinitionIsNotALoss(result);

        var methods = QueryArtifacts(result);
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("CustomerAll(", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("CustomerRaw(IDbConnection connection, decimal limit)", StringComparison.Ordinal));

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Query == "Customer.all" && r.Reason.Contains("\"jakarta.persistence.query.timeout\"", StringComparison.Ordinal));
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Query == "Customer.raw" && r.Feature == QueryFeature.Projection);
        Assert.All(result.Records.Where(r => r.Query is not null), r => Assert.Equal("orm.xml", r.Unit));
    }

    /// <summary>The text stands in its own element, so a stray child cannot be a piece of it: it is a loss by name, and the query is read.</summary>
    [Fact]
    public void AChildTheSchemaDoesNotGiveANamedQueryIsALoss()
    {
        var result = Convert(ORMEnum.Hibernate, string.Empty, $"""
            <entity-mappings xmlns="{OrmXmlNamespace}" version="3.2">
                <named-query name="Customer.all">
                    <query>select c from Customer c</query>
                    <comment>not in the schema</comment>
                </named-query>
            </entity-mappings>
            """);

        AssertNoFailure(result);
        Assert.Single(QueryArtifacts(result));
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Query == "Customer.all" && r.Reason.Contains("<comment>", StringComparison.Ordinal));
    }

    /// <summary>
    /// The parser claims XML for the orm.xml alone. A document of another dialect - the
    /// hbm.xml a client may send to a JPA source by mistake - yields nothing here and says
    /// nothing: the entity pass has answered for the unit.
    /// </summary>
    [Fact]
    public void ADocumentThatIsNoOrmXmlYieldsNothing()
    {
        var hbm = CrossFrameworkInputs.MappingUnits(ORMEnum.NHibernate).Single(u => u.ContentType == ConversionContentType.XML).Content;
        var parser = new HibernateJpqlQueryParser(() => new DapperSqlQueryBuilder());

        Assert.True(parser.CanParse(ConversionContentType.XML));
        Assert.Empty(parser.Parse(ConversionContentType.XML, hbm));
        Assert.Empty(parser.Parse(ConversionContentType.XML, "   "));
    }
}
