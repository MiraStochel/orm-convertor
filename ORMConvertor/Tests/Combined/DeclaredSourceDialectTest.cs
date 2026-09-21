using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The guard of decision 088: a source may declare the dialect its literal SQL is written
/// in, and a declaration of a system this version does not read stops that reading - a query
/// is not emitted, a literal column type is not read.
///
/// What is worth testing here is not the condition, which is one line, but the four things
/// around it that the rule itself does not show. That the refusal lands on the query half of
/// a unit that is a mapping and a query at once and leaves the mapping half alive
/// (decision 081), and that the bookkeeping of decision 066 stays whole across it. That the
/// literal column type is dropped rather than carried on the escape path, which is what
/// keeps a foreign type name out of an artifact written for SQL Server 2022. That the
/// boundary is narrow: LINQ, HQL and JPQL are languages of frameworks and are read whatever
/// the source declares. And that the field is an increment - the same input with no
/// declaration, and with the dialect this version does read, comes out byte for byte as it
/// did before the field existed.
/// </summary>
public class DeclaredSourceDialectTest
{
    /* ---- the query half ---------------------------------------------------- */

    /// <summary>
    /// Dapper is the plainest case: its whole query surface is the database system's SQL and
    /// its entity surface has none, so a declaredly foreign project translates its entities
    /// and loses its queries entirely.
    /// </summary>
    [Fact]
    public void ADeclaredlyForeignDapperQueryYieldsNoArtifact()
    {
        var units = CrossFrameworkInputs.Units(ORMEnum.Dapper);

        var declared = Convert(ORMEnum.Dapper, ORMEnum.EFCore, units, SourceSqlDialect.AnotherSystem);
        var undeclared = Convert(ORMEnum.Dapper, ORMEnum.EFCore, units, dialect: null);

        Assert.Contains(undeclared.Sources, s => s.ContentType == ConversionContentType.CSharpQuery);
        Assert.DoesNotContain(declared.Sources, s => s.ContentType == ConversionContentType.CSharpQuery);

        // The entity half is untouched: the Dapper entity parser reads plain C# classes and
        // there is no SQL in them.
        Assert.Contains(declared.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);

        var refusal = Assert.Single(Refusals(declared));
        Assert.Contains("another database system", refusal.Reason);

        // Refusing a query is no property of the query, so the record carries no category
        // and no query feature (decisions 048 and 088).
        Assert.Null(refusal.Category);
        Assert.Null(refusal.Feature);
    }

    /// <summary>
    /// The guard reaches the third reader of the same language through the same channel, and
    /// the MyBatis mapper is the case decision 088 called the sharpest: a whole query surface
    /// written in the system's SQL, over a document that is a mapping at the same time.
    /// </summary>
    [Fact]
    public void ADeclaredlyForeignMyBatisMapperKeepsItsMappingAndLosesItsStatements()
    {
        var units = CrossFrameworkInputs.Units(ORMEnum.MyBatis);

        var declared = Convert(ORMEnum.MyBatis, ORMEnum.EFCore, units, SourceSqlDialect.AnotherSystem);

        Assert.DoesNotContain(declared.Sources, s => s.ContentType == ConversionContentType.CSharpQuery);
        Assert.Single(Refusals(declared));

        // The <resultMap> half of the same document was read: the column names it states
        // reached the generated entity, which the domain class alone could not have said.
        var entity = Assert.Single(declared.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);
        Assert.Contains("CustomerName", entity.Content);

        // The bookkeeping of decisions 066 and 081 stays whole. The unit was claimed and it
        // did yield on the entity pass, so it must appear neither among the units nobody
        // read nor among those that produced nothing.
        Assert.DoesNotContain(declared.Records, r => r.Reason.Contains("so it was not read"));
        Assert.DoesNotContain(declared.Records, r => r.Reason.Contains("neither a mapping fact nor a query came of it"));
    }

    /// <summary>
    /// One document, two query languages, one of them a dialect and the other not: the
    /// hbm.xml refuses its &lt;sql-query&gt; and translates its &lt;query&gt;. It is the
    /// sharpest statement of the boundary, because both halves travel the same unit through
    /// the same parser.
    /// </summary>
    [Fact]
    public void AnHbmRefusesItsNativeQueryAndTranslatesItsHql()
    {
        var mapping = CrossFrameworkInputs.MappingUnits(ORMEnum.NHibernate)
            .Single(u => u.ContentType == ConversionContentType.XML);

        List<ConversionSource> units =
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.NHibernate).Where(u => u.ContentType != ConversionContentType.XML),
            new()
            {
                Name = "customer.hbm.xml",
                ContentType = ConversionContentType.XML,
                Content = mapping.Content.Replace(
                    "</hibernate-mapping>",
                    """
                      <query name="byLimit">select c.CustomerName from Customer c where c.CreditLimit > 2000</query>
                      <sql-query name="findRich">SELECT c.CustomerName FROM Sales.Customers AS c</sql-query>
                    </hibernate-mapping>
                    """),
            },
        ];

        var declared = Convert(ORMEnum.NHibernate, ORMEnum.Dapper, units, SourceSqlDialect.AnotherSystem);

        var refusal = Assert.Single(Refusals(declared));
        Assert.Equal("findRich", refusal.Query);

        // HQL is a language of the framework and means the same over every database system,
        // so it is read whatever the source declares.
        var queries = declared.Sources.Where(s => s.ContentType == ConversionContentType.SqlQuery).ToList();
        Assert.Single(queries);
        Assert.Contains("ByLimit", string.Join(string.Empty, declared.Sources.Select(s => s.Content)));

        // And the mapping half of the same unit is alive: the table and schema the hbm.xml
        // states are in the generated entity.
        Assert.Contains(declared.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);
    }

    /// <summary>
    /// The other side of the same boundary: LINQ is .NET, not a database system's SQL - the
    /// provider makes the SQL out of it later - so an EF Core query is read under a foreign
    /// declaration exactly as without one. Widening the guard to it would narrow the matrix
    /// of F10 for no reason at all.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void AFrameworkQueryLanguageIsReadWhateverTheSourceDeclares(ORMEnum source)
    {
        var units = CrossFrameworkInputs.Units(source);

        var declared = Convert(source, ORMEnum.Dapper, units, SourceSqlDialect.AnotherSystem);
        var undeclared = Convert(source, ORMEnum.Dapper, units, dialect: null);

        Assert.Empty(Refusals(declared));
        Assert.Equal(Artifacts(undeclared), Artifacts(declared));
    }

    /* ---- the literal column type ------------------------------------------- */

    /// <summary>
    /// The four sources that spell a database system's own type: the EF Core [Column]
    /// TypeName, the columnDefinition of both JPA implementations, and the sql-type of an
    /// hbm.xml column. Under a foreign declaration the claim is dropped with a loss record
    /// and the literal spelling does not travel either - kept on the escape path it would be
    /// copied into an artifact the descriptor says is written for SQL Server 2022
    /// (decisions 052 and 086).
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, ConversionContentType.CSharpEntity)]
    [InlineData(ORMEnum.NHibernate, ConversionContentType.XML)]
    [InlineData(ORMEnum.Hibernate, ConversionContentType.JavaEntity)]
    [InlineData(ORMEnum.EclipseLink, ConversionContentType.JavaEntity)]
    public void ADeclaredlyForeignColumnTypeIsDroppedWithARecord(ORMEnum source, ConversionContentType unitType)
    {
        var units = UnitsWithForeignColumnType(source, unitType);

        var declared = Convert(source, ORMEnum.NHibernate, units, SourceSqlDialect.AnotherSystem);
        var undeclared = Convert(source, ORMEnum.NHibernate, units, dialect: null);

        // Without the declaration the foreign spelling reaches the target artifact, which is
        // the very thing decision 088 calls a silent claim: a type name SQL Server does not
        // know, in a mapping written for SQL Server.
        Assert.Contains("VARCHAR2(50)", Artifacts(undeclared));
        Assert.DoesNotContain("VARCHAR2(50)", Artifacts(declared));

        var loss = Assert.Single(
            declared.Records,
            r => r.Kind == ConversionRecordKind.Loss
                && r.Category == MappingFactCategory.DatabaseType
                && r.Reason.Contains("another database system"));

        Assert.Equal("CustomerName", loss.Property);

        // The entity is translated whole and is poorer by one claim, not different: the rest
        // of the mapping is there and the property with it.
        var entity = Assert.Single(declared.Sources, s => s.ContentType is ConversionContentType.XML);
        Assert.Contains("CustomerName", entity.Content);
        Assert.Contains("CreditLimit", entity.Content);
    }

    /* ---- the field is an increment ----------------------------------------- */

    /// <summary>
    /// The test that holds the whole decision together: nothing in the sample inputs declares
    /// a dialect, so declaring the one this version does read - and declaring nothing at all -
    /// must give byte for byte what the tool gave before the field existed. Both artifacts and
    /// records, in every source framework, because a record that moved would be a changed
    /// output as much as an artifact that did.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Frameworks), MemberType = typeof(CrossFrameworkInputs))]
    public void DeclaringTheDialectThisVersionReadsChangesNothing(ORMEnum source)
    {
        var units = CrossFrameworkInputs.Units(source);
        var target = source == ORMEnum.Dapper ? ORMEnum.EFCore : ORMEnum.Dapper;

        var undeclared = Convert(source, target, units, dialect: null);
        var declared = Convert(source, target, units, SourceSqlDialect.SqlServer2022);

        Assert.Equal(Artifacts(undeclared), Artifacts(declared));
        Assert.Equal(Reasons(undeclared), Reasons(declared));
    }

    /* ---- the run record ---------------------------------------------------- */

    /// <summary>
    /// What the guard changes even for a source that declares nothing: the run record tells
    /// "the source said T-SQL" from "nobody said anything" (S6). The two used to be the same
    /// answer, and for a measurement that difference is exactly the provenance a green cell
    /// was missing (decision 088).
    /// </summary>
    [Fact]
    public void TheRunRecordTellsAStatedDialectFromAnUnstatedOne()
    {
        var units = CrossFrameworkInputs.Units(ORMEnum.Dapper);

        Assert.Null(Convert(ORMEnum.Dapper, ORMEnum.EFCore, units, dialect: null).DeclaredSourceDialect);

        Assert.Equal(
            SourceSqlDialect.SqlServer2022,
            Convert(ORMEnum.Dapper, ORMEnum.EFCore, units, SourceSqlDialect.SqlServer2022).DeclaredSourceDialect);

        // Beside the dialect the target declares, which comes from the descriptor: the record
        // now names a database system on both sides of the conversion (decision 086).
        var result = Convert(ORMEnum.Dapper, ORMEnum.EFCore, units, SourceSqlDialect.AnotherSystem);

        Assert.Equal(SourceSqlDialect.AnotherSystem, result.DeclaredSourceDialect);
        Assert.Equal(DatabaseDialect.SqlServer2022, result.TargetDatabaseDialect);
    }

    /* ---- helpers ----------------------------------------------------------- */

    private static ConversionResult Convert(
        ORMEnum source, ORMEnum target, List<ConversionSource> units, SourceSqlDialect? dialect)
        => ConversionHandler.Convert(source, target, units, catalogReader: null, dialect);

    /// <summary>
    /// The records the guard writes on the query side: a failure whose reason names the
    /// declaration. Matched on the reason rather than on the kind alone, so a refusal made
    /// for some other cause cannot be mistaken for this one.
    /// </summary>
    private static List<ConversionRecord> Refusals(ConversionResult result)
        => [.. result.Records.Where(r =>
            r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("another database system"))];

    private static string Artifacts(ConversionResult result)
        => string.Join("\n---\n", result.Sources.Select(s => $"{s.ContentType} {s.Name}\n{s.Content}"));

    private static string Reasons(ConversionResult result)
        => string.Join("\n", result.Records.Select(r => $"{r.Kind} {r.Category} {r.Unit} {r.Query} {r.Reason}"));

    /// <summary>
    /// The same entity as the matrix's, with one property carrying a type name of a system
    /// this version does not read. VARCHAR2(50) is the visible class of foreign input - T-SQL
    /// has no such name - which is what lets the test see the difference in the output; the
    /// invisible class, a name legal in both dialects with another meaning, is the reason the
    /// declaration exists at all and shows in no artifact by definition.
    /// </summary>
    private static List<ConversionSource> UnitsWithForeignColumnType(ORMEnum source, ConversionContentType unitType)
    {
        var units = CrossFrameworkInputs.MappingUnits(source);
        var unit = units.Single(u => u.ContentType == unitType);

        var spelt = unitType switch
        {
            ConversionContentType.CSharpEntity => unit.Content.Replace(
                "    public string CustomerName { get; set; }",
                "    [Column(\"CustomerName\", TypeName = \"VARCHAR2(50)\")]\n    public string CustomerName { get; set; }"),

            ConversionContentType.JavaEntity => unit.Content.Replace(
                "@Column(name = \"CustomerName\")",
                "@Column(name = \"CustomerName\", columnDefinition = \"VARCHAR2(50)\")"),

            ConversionContentType.XML => unit.Content.Replace(
                "<property name=\"CustomerName\" column=\"CustomerName\" type=\"String\" />",
                "<property name=\"CustomerName\" type=\"String\"><column name=\"CustomerName\" sql-type=\"VARCHAR2(50)\" /></property>"),

            _ => throw new ArgumentOutOfRangeException(nameof(unitType), unitType, null),
        };

        Assert.NotEqual(unit.Content, spelt);

        return
        [
            .. units.Where(u => !ReferenceEquals(u, unit)),
            new() { Name = unit.Name, ContentType = unit.ContentType, Content = spelt },
        ];
    }
}
