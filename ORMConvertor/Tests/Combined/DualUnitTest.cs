using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A unit that is a mapping and a query at once (decision 081). The orchestration no longer
/// splits the units by language: it offers every non-blank one to both passes and each takes
/// what its parsers claim, so an hbm.xml with a class beside a named query is read twice -
/// by <c>NHibernateXMLMappingParser</c> on the entity pass and by <c>NHibernateXmlQueryParser</c>
/// on the query one - without the client cutting it up and without a value of its own in the
/// content-type vocabulary.
///
/// NHibernate is the first framework to send such a unit and the cheapest one to prove it
/// with: both halves already existed, the HQL of a &lt;query&gt; is read by the parser of
/// decision 062, and the element used to be reported as a loss.
/// </summary>
public class DualUnitTest
{
    /// <summary>
    /// The matrix's own NHibernate units, with named queries added to the mapping document.
    /// The entity class travels along, because what is at stake is the mapping document
    /// being read by both passes, not an entity declared by XML alone.
    /// </summary>
    private static List<ConversionSource> UnitsWith(params string[] queryElements)
    {
        var units = CrossFrameworkInputs.MappingUnits(ORMEnum.NHibernate);
        var mapping = units.Single(u => u.ContentType == ConversionContentType.XML);

        return
        [
            .. units.Where(u => u.ContentType != ConversionContentType.XML),
            new()
            {
                Name = "customer.hbm.xml",
                ContentType = ConversionContentType.XML,
                Content = mapping.Content.Replace(
                    "</hibernate-mapping>",
                    string.Concat(queryElements) + "</hibernate-mapping>"),
            },
        ];
    }

    private static string Query(string name, string hql) => $"  <query name=\"{name}\">{hql}</query>\n";

    [Fact]
    public void ADocumentThatIsMappingAndQueryAtOnceIsReadByBothPasses()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Dapper,
            UnitsWith(Query("findAll", "select c.CustomerName from Customer c")));

        // The mapping half reached the entity builder and the query half reached a query
        // builder, out of one unit and without either parser knowing about the other.
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A unit with more than one query is exactly the unit whose queries the source named,
    /// so the two methods cannot collide: each is named after its own query instead of the
    /// fixed fallback. The second name is spelled with a hyphen on purpose - turning a name
    /// into an identifier of the target language is the builder's job.
    /// </summary>
    [Fact]
    public void TwoNamedQueriesOfOneUnitBecomeTwoMethodsNamedApart()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Dapper,
            UnitsWith(
                Query("findAll", "select c.CustomerName from Customer c"),
                Query("rich-customers", "select c.CustomerName from Customer c where c.CreditLimit > 1000")));

        var methods = result.Sources
            .Where(s => s.ContentType == ConversionContentType.CSharpQuery)
            .Select(s => s.Content)
            .ToList();

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("FindAll(IDbConnection connection)"));
        Assert.Contains(methods, m => m.Contains("RichCustomers(IDbConnection connection)"));
        Assert.DoesNotContain(methods, m => m.Contains(" Query(IDbConnection connection)"));
    }

    /// <summary>The same name in a target whose methods are camelCase (decision 081).</summary>
    [Fact]
    public void AJavaTargetSpellsTheNameItsOwnWay()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Hibernate,
            UnitsWith(Query("findAll", "select c.CustomerName from Customer c")));

        var method = result.Sources.Single(s => s.ContentType == ConversionContentType.JavaQuery).Content;

        Assert.Contains("findAll(EntityManager em)", method);
    }

    /// <summary>
    /// A query the source named carries its name into the records too. Without it, three
    /// failed queries of one document would be three records distinguishable only by their
    /// order - the unit reference names the file, not the query inside it.
    /// </summary>
    [Fact]
    public void RecordsOfANamedQueryNameTheQuery()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Dapper,
            UnitsWith(
                Query("findAll", "select c.CustomerName from Customer c"),
                // A query parameter is refused at reading (decision 070), so this one leaves
                // a record and no artifact - and the record has to say which query it was.
                Query("byLimit", "select c.CustomerName from Customer c where c.CreditLimit > :limit")));

        var refused = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        Assert.Equal("byLimit", refused.Query);
        Assert.Equal("customer.hbm.xml", refused.Unit);

        // The neighbour is untouched: a fresh builder per query is what keeps one query's
        // refusal from reaching the next one.
        Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }

    /// <summary>
    /// An empty collection from the query parser is no error in itself: a mapping document
    /// that happens to carry no query is an ordinary input. Barrenness is a question about
    /// both passes together, and the entity pass answered it.
    /// </summary>
    [Fact]
    public void ADualUnitThatCarriesNoQueryIsNotBarren()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Dapper,
            UnitsWith());

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A unit neither pass claims is one event and gets one record. Both branches used to
    /// write their own, which a unit both of them look at would have collected twice.
    /// </summary>
    [Fact]
    public void AUnitNobodyClaimsIsReportedOnce()
    {
        List<ConversionSource> sources =
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.Dapper),
            new()
            {
                Name = "customer.hbm.xml",
                ContentType = ConversionContentType.XML,
                Content = "<hibernate-mapping><class name=\"Customer\" /></hibernate-mapping>",
            },
        ];

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, sources);

        var record = Assert.Single(result.Records, r => r.Unit == "customer.hbm.xml");

        Assert.Equal(ConversionRecordKind.Failure, record.Kind);
        Assert.Contains("no parser", record.Reason);
    }

    /// <summary>
    /// The other query form of an hbm.xml is native SQL, and reading SQL needs the T-SQL
    /// grammar that still sits inside the Dapper wrapper (S1). It stays a loss - but one
    /// that says what it is, instead of citing the flat-class boundary of decision 030 as it
    /// did while &lt;query&gt; was unread beside it.
    /// </summary>
    [Fact]
    public void ANativeQueryIsALossThatNamesItself()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Dapper,
            UnitsWith(
                Query("findAll", "select c.CustomerName from Customer c"),
                "  <sql-query name=\"findAllNative\">SELECT CustomerName FROM Sales.Customers</sql-query>\n"));

        // Records about the unit itself, as opposed to the mechanical losses Dapper reports
        // about every mapping fact it cannot record (decision 066 attributes only the former
        // to a unit).
        var loss = Assert.Single(
            result.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Unit == "customer.hbm.xml");

        Assert.Contains("native SQL", loss.Reason);
        Assert.Contains("findAllNative", loss.Reason);

        // The HQL form beside it is read, so it leaves no loss of its own any more.
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("<query name=\"findAll\">"));
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }

    /// <summary>
    /// All three forms that let a unit carry several queries require the name, so a nameless
    /// one is malformed input rather than a query falling back to the fixed method name.
    /// </summary>
    [Fact]
    public void AQueryWithoutANameIsRefused()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Dapper,
            UnitsWith("  <query>select c.CustomerName from Customer c</query>\n"));

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        Assert.Contains("name attribute", record.Reason);
        Assert.Null(record.Query);
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }
}
