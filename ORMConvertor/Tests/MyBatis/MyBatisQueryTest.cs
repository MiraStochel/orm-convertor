using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.MyBatis;

/// <summary>
/// The query branch of decision 084, through the real orchestration because two of the three
/// units are a mapping and a query at once and the passes have to meet: the signatures of the
/// mapper interface are read on the entity pass and are what types the parameters of a
/// statement read on the query one.
///
/// The line the whole branch is drawn along is decision 082's: the wrapper hands the shared
/// reader plain T-SQL or refuses the statement. Everything below is one of the two.
/// </summary>
public class MyBatisQueryTest
{
    private const string DomainClass = """
        package Shop;

        import java.math.BigDecimal;

        public class Customer {
            private Integer CustomerId;
            private String CustomerName;
            private BigDecimal CreditLimit;
        }
        """;

    private static string Interface(string methods) => $$"""
        package Shop;

        import java.math.BigDecimal;
        import java.util.Collection;
        import java.util.List;
        import org.apache.ibatis.annotations.Param;
        import org.apache.ibatis.annotations.Select;

        public interface CustomerMapper {
        {{methods}}
        }
        """;

    private static string Mapper(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
        <mapper namespace="Shop.CustomerMapper">
        {body}
        </mapper>
        """;

    /// <summary>
    /// A mapper that carries the mapping and no statement, for a case whose statement is in
    /// the annotated form: a document declaring nothing but a &lt;sql&gt; fragment would be a
    /// unit nothing came of, which is a finding of its own (decision 066).
    /// </summary>
    private static readonly string ResultMapOnly = Mapper("""
          <resultMap id="customer" type="Customer">
            <result column="CustomerName" property="CustomerName"/>
          </resultMap>
        """);

    /// <summary>A conversion out of MyBatis, with whichever of the three units the case needs.</summary>
    private static ConversionResult Convert(ORMEnum target, string? mapperInterface, string mapper)
    {
        List<ConversionSource> units =
        [
            new() { Content = DomainClass, ContentType = ConversionContentType.JavaEntity },
            new() { Content = mapper, ContentType = ConversionContentType.XML },
        ];

        if (mapperInterface is not null)
        {
            units.Insert(1, new ConversionSource { Content = mapperInterface, ContentType = ConversionContentType.JavaQuery });
        }

        return ConversionHandler.Convert(ORMEnum.MyBatis, target, units);
    }

    private static string Sql(ConversionResult result)
    {
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        return result.Sources.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content.Replace("\r\n", "\n");
    }

    /* ---- the statement's text -------------------------------------------------------- */

    [Fact]
    public void AStatementIsReadThroughTheSharedTSqlReader()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName
            FROM Sales.Customers AS c
            ORDER BY c.CustomerName ASC
          </select>
        """));

        Assert.Equal(
            "SELECT c.CustomerName\nFROM Sales.Customers AS c\nORDER BY c.CustomerName ASC",
            Sql(result));
    }

    /// <summary>The statement's id is the query's name, so the generated method is named after it.</summary>
    [Fact]
    public void TheStatementIdNamesTheQueryAndItsMethod()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findRichCustomers" resultType="Customer">
            SELECT c.CustomerName FROM Sales.Customers AS c
          </select>
        """));

        Assert.Contains("FindRichCustomers(", result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
        Assert.Contains(result.Records, r => r.Query == "findRichCustomers");
    }

    [Fact]
    public void AStatementWithoutAnIdIsRefused()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select resultType="Customer">
            SELECT c.CustomerName FROM Sales.Customers AS c
          </select>
        """));

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("id attribute"));
    }

    /// <summary>
    /// The result mapping is the one thing a statement states that the query representation
    /// has no slot for - the same record and the same reason as the &lt;return&gt; of an
    /// NHibernate native query (decision 082).
    /// </summary>
    [Fact]
    public void TheResultMappingIsALossAndTheResultTypeIsDerivedFromTheTable()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultMap="customer">
            SELECT c.CustomerName FROM Sales.Customers AS c
          </select>
        """));

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("resultMap=\"customer\""));
        Assert.Contains("List<Customer>", result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
    }

    /* ---- step 1: the static tags ----------------------------------------------------- */

    [Fact]
    public void AnIncludeInlinesTheFragmentOfTheSameDocument()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <sql id="columns">c.CustomerName, c.CreditLimit</sql>
          <select id="findAll" resultType="Customer">
            SELECT <include refid="columns"/>
            FROM Sales.Customers AS c
          </select>
        """));

        Assert.Equal("SELECT c.CustomerName, c.CreditLimit\nFROM Sales.Customers AS c", Sql(result));
    }

    /// <summary>
    /// The unit of conversion is one artifact, so a fragment is looked for nowhere else -
    /// otherwise the result would depend on what the user happened to attach.
    /// </summary>
    [Fact]
    public void AnIncludeOfAFragmentTheDocumentDoesNotDeclareIsRefused()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT <include refid="elsewhere"/> FROM Sales.Customers AS c
          </select>
        """));

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("elsewhere"));
    }

    /// <summary>
    /// &lt;where&gt; is an operation over the condition, not a piece of text: it writes the
    /// keyword and strips the leading connective, which is exactly what the condition tree
    /// does in the representation.
    /// </summary>
    [Fact]
    public void WhereWritesItsKeywordAndStripsTheLeadingConnective()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName
            FROM Sales.Customers AS c
            <where>
              AND c.CreditLimit &gt; 2000
            </where>
          </select>
        """));

        Assert.Equal("SELECT c.CustomerName\nFROM Sales.Customers AS c\nWHERE c.CreditLimit > 2000", Sql(result));
    }

    [Fact]
    public void ATrimAppliesItsOwnPrefixAndOverrides()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName
            FROM Sales.Customers AS c
            <trim prefix="WHERE" prefixOverrides="OR ">
              OR c.CreditLimit &gt; 2000
            </trim>
          </select>
        """));

        Assert.Equal("SELECT c.CustomerName\nFROM Sales.Customers AS c\nWHERE c.CreditLimit > 2000", Sql(result));
    }

    /* ---- step 2: the tags OGNL decides ----------------------------------------------- */

    /// <summary>
    /// The heart of decision 084: one &lt;select&gt; with an &lt;if&gt; is a family of
    /// statements, and two members of that family differ by a whole WHERE clause - a
    /// different set of rows, which rule 053 forbids emitting in silence. The refusal names
    /// the tag, so the user knows what to change.
    /// </summary>
    [Theory]
    [InlineData("<if test=\"x != null\">AND c.CreditLimit &gt; 2000</if>", "if")]
    [InlineData("<choose><when test=\"x\">AND c.CreditLimit &gt; 1</when></choose>", "choose")]
    [InlineData("<bind name=\"p\" value=\"'%'\"/>", "bind")]
    public void ATagOgnlDecidesRefusesTheStatementByName(string tag, string name)
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper($"""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName
            FROM Sales.Customers AS c
            WHERE 1 = 1 {tag}
          </select>
        """));

        var refusal = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains($"<{name}>"));
        Assert.Contains("family of statements", refusal.Reason);
        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
    }

    /* ---- step 3: foreach ------------------------------------------------------------- */

    /// <summary>
    /// The one exception, and it is a consequence of the rule rather than an exception to
    /// it: a canonical &lt;foreach&gt; over IN needs no OGNL - its collection is the name of
    /// a parameter - and the representation has a place for it, the collection parameter
    /// decision 074 sent there by name.
    /// </summary>
    [Fact]
    public void ACanonicalForEachIsOneCollectionParameter()
    {
        var result = Convert(ORMEnum.Dapper,
            Interface("    List<Customer> findIn(@Param(\"ids\") Collection<Integer> ids);"),
            Mapper("""
              <select id="findIn" resultType="Customer">
                SELECT c.CustomerName
                FROM Sales.Customers AS c
                WHERE c.CustomerId IN
                <foreach item="item" collection="ids" open="(" separator="," close=")">#{item}</foreach>
              </select>
            """));

        Assert.Equal(
            "SELECT c.CustomerName\nFROM Sales.Customers AS c\nWHERE c.CustomerId IN @ids",
            Sql(result));

        Assert.Contains(
            "IEnumerable<int> ids",
            result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
    }

    [Theory]
    [InlineData("<foreach item=\"item\" collection=\"ids\" open=\"[\" separator=\",\" close=\"]\">#{item}</foreach>", "parentheses")]
    [InlineData("<foreach item=\"item\" collection=\"ids\" open=\"(\" separator=\";\" close=\")\">#{item}</foreach>", "comma")]
    [InlineData("<foreach item=\"item\" collection=\"ids\" open=\"(\" separator=\",\" close=\")\">#{item} + 1</foreach>", "item alone")]
    public void AForEachOutsideTheCanonicalFormIsRefusedSayingHowItDiffers(string tag, string difference)
    {
        var result = Convert(ORMEnum.Dapper,
            Interface("    List<Customer> findIn(@Param(\"ids\") Collection<Integer> ids);"),
            Mapper($"""
              <select id="findIn" resultType="Customer">
                SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerId IN {tag}
              </select>
            """));

        var refusal = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("<foreach>"));
        Assert.Contains(difference, refusal.Reason);
        Assert.Equal(QueryFeature.QueryParameter, refusal.Feature);
    }

    /* ---- step 4: the placeholders ---------------------------------------------------- */

    /// <summary>
    /// #{name} becomes @name, which the shared reading has read as a named parameter since
    /// decision 083 - a substitution that is exact because both sides mean a bound value.
    /// </summary>
    [Fact]
    public void APlaceholderBecomesABoundParameterTypedFromTheMethodSignature()
    {
        var result = Convert(ORMEnum.Dapper,
            Interface("    List<Customer> findRich(@Param(\"creditLimit\") BigDecimal creditLimit);"),
            Mapper("""
              <select id="findRich" resultType="Customer">
                SELECT c.CustomerName
                FROM Sales.Customers AS c
                WHERE c.CreditLimit &gt; #{creditLimit}
              </select>
            """));

        Assert.Equal(
            "SELECT c.CustomerName\nFROM Sales.Customers AS c\nWHERE c.CreditLimit > @creditLimit",
            Sql(result));

        Assert.Contains(
            "decimal creditLimit",
            result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
    }

    /// <summary>
    /// Without the interface there is nowhere to take the type from: a MyBatis mapper states
    /// nothing about its parameters and the statement names a table no entity is mapped to,
    /// so the comparison says nothing either. The refusal is the template's, and it is the
    /// reason the three units belong together (decision 084).
    /// </summary>
    [Fact]
    public void WithoutTheInterfaceTheParametersScalarDoesNotFollowFromAnything()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findRich" resultType="Customer">
            SELECT c.CustomerName
            FROM Sales.Customers AS c
            WHERE c.CreditLimit &gt; #{creditLimit}
          </select>
        """));

        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Failure
            && r.Feature == QueryFeature.QueryParameter
            && r.Reason.Contains("creditLimit"));
    }

    /// <summary>javaType inside the placeholder is the most local claim and outranks the signature.</summary>
    [Fact]
    public void AJavaTypeInsideThePlaceholderStatesTheScalar()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findRich" resultType="Customer">
            SELECT c.CustomerName
            FROM Sales.Customers AS c
            WHERE c.CreditLimit &gt; #{creditLimit,javaType=BigDecimal,jdbcType=DECIMAL}
          </select>
        """));

        Assert.Contains(
            "decimal creditLimit",
            result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);

        // jdbcType is how MyBatis binds the value rather than what the value is.
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("jdbcType"));
    }

    /// <summary>
    /// A stated scalar outranks a derived one and the difference is a Conflict, not a silent
    /// choice - the answer decision 015 gives whenever two sources of one fact disagree.
    /// </summary>
    [Fact]
    public void AStatedScalarOutranksTheDerivedOneAndTheDifferenceIsAConflict()
    {
        var result = Convert(ORMEnum.Dapper,
            Interface("    List<Customer> findRich(@Param(\"creditLimit\") String creditLimit);"),
            Mapper("""
              <select id="findRich" resultType="Customer">
                SELECT c.CustomerName
                FROM Customer AS c
                WHERE c.CreditLimit &gt; #{creditLimit}
              </select>
            """));

        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Conflict
            && r.Feature == QueryFeature.QueryParameter
            && r.Reason.Contains("creditLimit"));

        Assert.Contains(
            "string creditLimit",
            result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
    }

    /// <summary>${…} substitutes text, not a value, and can change the tables the query names.</summary>
    [Fact]
    public void ATextSubstitutionIsRefused()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName FROM ${table} AS c
          </select>
        """));

        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Failure
            && r.Feature == QueryFeature.QueryParameter
            && r.Reason.Contains("${"));
    }

    [Fact]
    public void APropertyPathInsideAPlaceholderIsRefused()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerId = #{customer.id}
          </select>
        """));

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("#{customer.id}"));
    }

    /// <summary>
    /// The trap that is visible only from inside the substitution: after step four an @name
    /// that stood in the text from the start is indistinguishable from a placeholder, and
    /// would be translated as a parameter MyBatis never binds. The check runs before the
    /// substitution, because afterwards it cannot be made at all.
    /// </summary>
    [Fact]
    public void AnIdentifierAlreadyIntroducedByAnAtSignRefusesTheStatement()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerId = @legacy
          </select>
        """));

        var refusal = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("@legacy"));
        Assert.Equal(QueryFeature.QueryParameter, refusal.Feature);
    }

    /// <summary>A literal that happens to hold an @ is a value, not a name, and is left alone.</summary>
    [Fact]
    public void AnAtSignInsideAStringLiteralIsNotAParameter()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerName LIKE '%@example%'
          </select>
        """));

        Assert.Contains("'%@example%'", Sql(result));
    }

    /* ---- the two forms of one statement ---------------------------------------------- */

    /// <summary>
    /// The finding the tutorial produced as an exception rather than as documentation: the
    /// same &lt;namespace, id&gt; in an annotation and in XML is an input MyBatis refuses to
    /// build a factory from, so there is no precedence to apply - a Failure, not a Conflict
    /// (decision 068). It is recognized across units, because the entity pass reads both
    /// before the query pass runs.
    /// </summary>
    [Fact]
    public void TheSameStatementInBothFormsIsAFailureNamingBoth()
    {
        var result = Convert(ORMEnum.Dapper,
            Interface("""
                    @Select("SELECT c.CustomerName FROM Sales.Customers AS c")
                    List<Customer> findAll();
                """),
            Mapper("""
              <select id="findAll" resultType="Customer">
                SELECT c.CustomerName FROM Sales.Customers AS c
              </select>
            """));

        var refusals = result.Records
            .Where(r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("Shop.CustomerMapper.findAll"))
            .ToList();

        Assert.NotEmpty(refusals);
        Assert.All(refusals, r => Assert.Contains("annotation", r.Reason));
        Assert.All(refusals, r => Assert.Contains("XML mapper", r.Reason));
        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
    }

    /// <summary>The annotated form on its own is read like any other statement (F8).</summary>
    [Fact]
    public void AnAnnotatedStatementIsReadOnItsOwn()
    {
        var result = Convert(ORMEnum.Dapper,
            Interface("""
                    @Select("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit > #{creditLimit}")
                    List<Customer> findRich(@Param("creditLimit") BigDecimal creditLimit);
                """),
            ResultMapOnly);

        Assert.Equal(
            "SELECT c.CustomerName\nFROM Sales.Customers AS c\nWHERE c.CreditLimit > @creditLimit",
            Sql(result));
    }

    /// <summary>
    /// SQL assembled by Java code is a program and not an artifact, and running it to find
    /// out what it says is on the far side of the boundary decision 040 draws.
    /// </summary>
    [Fact]
    public void ASelectProviderIsRefusedByName()
    {
        var result = Convert(ORMEnum.Dapper,
            Interface("""
                    @SelectProvider(type = CustomerSql.class, method = "findAll")
                    List<Customer> findAll();
                """),
            ResultMapOnly);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("@SelectProvider"));
    }

    /// <summary>
    /// A statement that changes rows is outside what the tool translates for any framework -
    /// the same refusal a Dapper unit carrying an INSERT meets in the shared reader - and is
    /// named rather than dropped in silence.
    /// </summary>
    [Fact]
    public void AWritingStatementIsNamedAndRefused()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <insert id="insertCustomer">
            INSERT INTO Sales.Customers (CustomerName) VALUES (#{customerName})
          </insert>
        """));

        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("<insert>") && r.Query == "insertCustomer");
    }

    /* ---- more than one table --------------------------------------------------------- */

    /// <summary>
    /// The hand-written join requirement F8 names, on the query side where it belongs: it is
    /// translated into the join of the target and not into a foreign key of the mapping.
    /// </summary>
    [Fact]
    public void AHandWrittenJoinIsTranslatedAsAJoin()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findWithOrders" resultType="Customer">
            SELECT c.CustomerName, o.Reference
            FROM Sales.Customers AS c
            LEFT JOIN Sales.Orders AS o ON o.CustomerId = c.CustomerId
            WHERE c.CreditLimit &gt; 2000
          </select>
        """));

        var sql = Sql(result);
        Assert.Contains("LEFT JOIN Sales.Orders o ON o.CustomerId = c.CustomerId", sql);

        // The join is a query fact; nothing about it reaches the mapping of the entity.
        Assert.All(ConversionHandler
                .Convert(ORMEnum.MyBatis, ORMEnum.EFCore,
                [
                    new() { Content = DomainClass, ContentType = ConversionContentType.JavaEntity },
                    new() { Content = Mapper("""
                      <select id="findWithOrders" resultType="Customer">
                        SELECT c.CustomerName FROM Sales.Customers AS c
                        LEFT JOIN Sales.Orders AS o ON o.CustomerId = c.CustomerId
                      </select>
                    """), ContentType = ConversionContentType.XML },
                ])
                .Records.Where(r => r.Category == MappingFactCategory.ForeignKeyColumns),
            r => Assert.NotEqual(ConversionRecordKind.Supplied, r.Kind));
    }

    [Fact]
    public void ASubqueryInAConditionIsCarried()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findWithOrders" resultType="Customer">
            SELECT c.CustomerName
            FROM Sales.Customers AS c
            WHERE EXISTS (SELECT o.OrderId FROM Sales.Orders AS o WHERE o.CustomerId = c.CustomerId)
          </select>
        """));

        Assert.Contains("EXISTS (SELECT", Sql(result));
    }

    /// <summary>Several statements in one document yield several queries, each under its own name.</summary>
    [Fact]
    public void EveryStatementOfTheDocumentBecomesItsOwnQuery()
    {
        var result = Convert(ORMEnum.Dapper, null, Mapper("""
          <select id="findAll" resultType="Customer">
            SELECT c.CustomerName FROM Sales.Customers AS c
          </select>
          <select id="findRich" resultType="Customer">
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit &gt; 2000
          </select>
        """));

        Assert.Equal(2, result.Sources.Count(s => s.ContentType == ConversionContentType.SqlQuery));
        Assert.Contains("FindAll(", result.Sources.First(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
        Assert.Contains("FindRich(", result.Sources.Last(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
    }
}
