using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers.Convertors;
using OrmConvertor;
using Tests.Combined;

namespace Tests.NHibernate;

/// <summary>
/// What a named query of an hbm.xml states beside its text. The reading used to take the
/// element's own text and say nothing of the rest; now every part of it is answered. A
/// &lt;query-param&gt; is the stated scalar of its parameter (decision 083), in either form of
/// the query, the attributes are execution options the representation does not carry and
/// each is a loss, and a child the schema does not admit refuses the query, because its
/// text would otherwise leave the query without a word.
/// </summary>
public class NHibernateNamedQueryDeclarationTest
{
    private static ConversionResult Convert(params string[] queryElements)
    {
        var units = CrossFrameworkInputs.MappingUnits(ORMEnum.NHibernate);
        var mapping = units.Single(u => u.ContentType == ConversionContentType.XML);

        return ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Dapper,
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
            ]);
    }

    private static List<ConversionRecord> Records(ConversionResult result, string query) =>
        [.. result.Records.Where(r => r.Query == query)];

    private static string Method(ConversionResult result) =>
        result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

    /* ---- the type names ------------------------------------------------------------- */

    /// <summary>
    /// The value side of an NHibernate type name: the registered names and their aliases, the
    /// names that read the newer CLR types, a CLR type by its full name, and the names that
    /// type no scalar of the vocabulary.
    /// </summary>
    [Theory]
    [InlineData("Int32", ScalarType.Int)]
    [InlineData("int", ScalarType.Int)]
    [InlineData("Int64", ScalarType.Long)]
    [InlineData("Currency", ScalarType.Decimal)]
    [InlineData("AnsiString", ScalarType.String)]
    [InlineData("Date", ScalarType.DateTime)]
    [InlineData("DateOnlyAsDate", ScalarType.Date)]
    [InlineData("TimeAsTimeSpan", ScalarType.Duration)]
    [InlineData("TimeOnlyAsTicks", ScalarType.TimeOfDay)]
    [InlineData("YesNo", ScalarType.Bool)]
    [InlineData("System.Int64", ScalarType.Long)]
    [InlineData("System.Int64, mscorlib", ScalarType.Long)]
    [InlineData("System.DateOnly", ScalarType.Date)]
    [InlineData("XmlDoc", null)]
    [InlineData("UInt32", null)]
    [InlineData("System.Object", null)]
    [InlineData("Shop.MoneyType, Shop", null)]
    public void ATypeNameStatesTheScalarOfItsValue(string type, ScalarType? scalar)
        => Assert.Equal(scalar, ScalarTypeConvertor.FromNHibernate(type));

    /* ---- <query-param> -------------------------------------------------------------- */

    /// <summary>
    /// The declared scalar is the one the generated method takes, and where the comparison
    /// implies another, the two are not reconciled in silence: the stated one wins with a
    /// conflict, as decision 083 rules for the stated scalar of MyBatis.
    /// </summary>
    [Fact]
    public void ADeclaredTypeIsTheStatedScalarOfItsParameter()
    {
        var result = Convert("""
            <query name="byId">select c.CustomerName from Customer c where c.CustomerId = :id<query-param name="id" type="Int64"/></query>
            """);

        Assert.Contains("(IDbConnection connection, long id)", Method(result), StringComparison.Ordinal);
        Assert.Contains(
            Records(result, "byId"),
            r => r.Kind == ConversionRecordKind.Conflict && r.Feature == QueryFeature.QueryParameter);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A parameter compared with another parameter has no scalar to derive, and the builder
    /// template refuses it (decision 083) - unless the source declared one, which is what the
    /// declaration is for.
    /// </summary>
    [Fact]
    public void ADeclaredTypeTypesAParameterNothingElseCould()
    {
        var result = Convert("""
            <query name="window">
              select c.CustomerName from Customer c where :first = :second
              <query-param name="first" type="Int32"/>
              <query-param name="second" type="Int32"/>
            </query>
            """);

        Assert.Contains("(IDbConnection connection, int first, int second)", Method(result), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void ATypeOutsideTheVocabularyIsALossAndTheScalarIsDerived()
    {
        var result = Convert("""
            <query name="byId">select c.CustomerName from Customer c where c.CustomerId = :id<query-param name="id" type="XmlDoc"/></query>
            """);

        Assert.Contains("(IDbConnection connection, int id)", Method(result), StringComparison.Ordinal);
        Assert.Contains(
            Records(result, "byId"),
            r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("'XmlDoc'", StringComparison.Ordinal));
    }

    [Fact]
    public void OneParameterDeclaredWithTwoScalarsRefusesTheQuery()
    {
        var result = Convert("""
            <query name="byId">
              select c.CustomerName from Customer c where c.CustomerId = :id
              <query-param name="id" type="Int32"/>
              <query-param name="id" type="Int64"/>
            </query>
            """);

        Assert.Contains(
            Records(result, "byId"),
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.QueryParameter);
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }

    /// <summary>
    /// The native form declares its parameters the same way and is read the same way; the
    /// declaration used to be reported as a result mapping it is not. A CLR type named in
    /// full is a type name NHibernate resolves, and so is read. &lt;synchronize&gt; is no
    /// result mapping either and says so.
    /// </summary>
    [Fact]
    public void TheNativeFormReadsItsDeclarationsToo()
    {
        var result = Convert("""
            <sql-query name="byId">
              SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerId = :id
              <synchronize table="Customers"/>
              <query-param name="id" type="System.Int64"/>
            </sql-query>
            """);

        var records = Records(result, "byId");

        Assert.Contains("(IDbConnection connection, long id)", Method(result), StringComparison.Ordinal);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Conflict && r.Feature == QueryFeature.QueryParameter);
        Assert.DoesNotContain(records, r => r.Reason.Contains("query-param", StringComparison.Ordinal));
        Assert.Contains(records, r =>
            r.Kind == ConversionRecordKind.Loss
            && r.Reason.Contains("<synchronize>", StringComparison.Ordinal)
            && !r.Reason.Contains("result is mapped", StringComparison.Ordinal));
    }

    /* ---- the attributes ------------------------------------------------------------- */

    [Fact]
    public void TheOptionsOfANamedQueryAreEachALoss()
    {
        var result = Convert("""
            <query name="rich" cacheable="true" timeout="5" flush-mode="manual">select c.CustomerName from Customer c</query>
            """);

        var losses = Records(result, "rich").Where(r => r.Kind == ConversionRecordKind.Loss).ToList();

        Assert.Contains(losses, r => r.Reason.Contains("cacheable=\"true\"", StringComparison.Ordinal));
        Assert.Contains(losses, r => r.Reason.Contains("timeout=\"5\"", StringComparison.Ordinal));
        Assert.Contains(losses, r => r.Reason.Contains("flush-mode=\"manual\"", StringComparison.Ordinal));
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }

    /// <summary>A query is not cacheable unless it says so, so saying it is not states nothing.</summary>
    [Fact]
    public void AnOptionSpelledAsItsDefaultStatesNothing()
    {
        var result = Convert("""
            <query name="rich" cacheable="false">select c.CustomerName from Customer c</query>
            """);

        Assert.Empty(Records(result, "rich"));
    }

    /// <summary>
    /// callable marks a native query as a procedure call. What the text states is read as
    /// ever: a query is translated and the mark is the loss, a call is refused by the shared
    /// reading as a statement that is not a query.
    /// </summary>
    [Fact]
    public void ACallableNativeQueryLosesTheMarkAndACallInItIsRefused()
    {
        var result = Convert(
            """<sql-query name="all" callable="true">SELECT c.CustomerName FROM Sales.Customers AS c</sql-query>""",
            """<sql-query name="byProcedure" callable="true">exec dbo.FindCustomers :id</sql-query>""");

        Assert.Contains(
            Records(result, "all"),
            r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("callable=\"true\"", StringComparison.Ordinal));
        Assert.DoesNotContain(Records(result, "all"), r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains(
            Records(result, "byProcedure"),
            r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("EXECUTE", StringComparison.Ordinal));
    }

    /* ---- the children --------------------------------------------------------------- */

    /// <summary>
    /// The text of a child is never read as the query's, so an element the schema does not
    /// admit could take a piece of the query with it - here the whole filter. That would be
    /// a query with other rows than the source wrote, and it is refused naming the element.
    /// </summary>
    [Fact]
    public void AChildTheSchemaDoesNotAdmitRefusesTheQuery()
    {
        var result = Convert("""
            <query name="rich">select c.CustomerName from Customer c <filter>where c.CreditLimit > 2000</filter></query>
            """);

        Assert.Contains(
            Records(result, "rich"),
            r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("<filter>", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }
}
