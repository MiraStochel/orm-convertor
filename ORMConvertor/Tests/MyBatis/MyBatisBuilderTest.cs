using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using MyBatisWrappers;
using OrmConvertor;

namespace Tests.MyBatis;

/// <summary>
/// The writing half of decision 084: the pair of artifacts a MyBatis entity becomes, and the
/// pair a query becomes. Read against <see cref="MyBatisMappingParserTest"/> it is the
/// conversion table of the decision in both directions - and the asymmetry it states out
/// loud is visible here: writing the key into &lt;id&gt; is exact, reading it back is not.
/// </summary>
public class MyBatisBuilderTest
{
    private static (string Java, string Mapper) Build(Action<MyBatisEntityBuilder> populate)
    {
        var builder = new MyBatisEntityBuilder();
        populate(builder);

        var artifacts = builder.Build();

        return (
            artifacts.Single(a => a.ContentType == ConversionContentType.JavaEntity).Content.Replace("\r\n", "\n"),
            artifacts.Single(a => a.ContentType == ConversionContentType.XML).Content.Replace("\r\n", "\n"));
    }

    /// <summary>
    /// Line endings of an expected artifact. The repository checks out CRLF, so a raw string
    /// literal in this file carries them and a generated artifact does not; S2 is about the
    /// text of the artifact, not about the newline of the checkout.
    /// </summary>
    private static string Expected(string text) => text.Replace("\r\n", "\n");

    private static void Customer(MyBatisEntityBuilder builder)
    {
        builder.AddNamespace("Shop");
        builder.AddClassHeader("public", "Customer");
        builder.AddProperty("int", "CustomerId", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("string", "CustomerName", "public", hasGetter: true, hasSetter: true, isNullable: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "CustomerId");
        builder.SetPropertyDatabaseMapping("CustomerName", new Dictionary<string, string> { ["columnname"] = "FullName" });
        builder.SetPropertyDatabaseType("CustomerName", DatabaseType.VarChar, isUnicode: true, length: 200);
    }

    /* ---- the mapper document --------------------------------------------------------- */

    [Fact]
    public void TheMapperCarriesThePrologTheDoctypeAndOneClosedResultMap()
    {
        var (_, mapper) = Build(Customer);

        Assert.Equal(Expected("""
            <?xml version="1.0" encoding="utf-8" ?>
            <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                    "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
            <mapper namespace="Shop.CustomerMapper">
                <resultMap id="Customer" type="Shop.Customer" autoMapping="false">
                    <id column="CustomerId" property="CustomerId" />
                    <result column="FullName" property="CustomerName" jdbcType="NVARCHAR" />
                </resultMap>
            </mapper>
            """), mapper);
    }

    /// <summary>
    /// autoMapping="false" is what makes the artifact independent of a configuration the
    /// tool does not see: with automatic mapping on, a column nobody named would still fill
    /// a property of the same name, and mapUnderscoreToCamelCase would decide which
    /// (decision 084).
    /// </summary>
    [Fact]
    public void EveryResultMapIsClosed()
    {
        var (_, mapper) = Build(Customer);

        Assert.Contains("autoMapping=\"false\"", mapper);
    }

    /// <summary>
    /// The key is written in the order of its parts, which is exact - and is the one half of
    /// the round trip that holds: reading it back is not, because MyBatis marks the identity
    /// of a result row with the same element.
    /// </summary>
    [Fact]
    public void ACompositeKeyIsWrittenAsIdElementsInTheKeysOrder()
    {
        var (_, mapper) = Build(builder =>
        {
            builder.AddClassHeader("public", "OrderLine");
            builder.AddProperty("int", "OrderId", "public", hasGetter: true, hasSetter: true);
            builder.AddProperty("int", "LineNumber", "public", hasGetter: true, hasSetter: true);
            builder.AddPrimaryKey([("OrderId", 1, PrimaryKeyStrategy.Assigned), ("LineNumber", 2, PrimaryKeyStrategy.Assigned)]);
        });

        var id = mapper.Split('\n').Where(line => line.Contains("<id ")).ToList();

        Assert.Equal(
        [
            "        <id column=\"OrderId\" property=\"OrderId\" />",
            "        <id column=\"LineNumber\" property=\"LineNumber\" />",
        ],
            id);
    }

    /// <summary>
    /// The property a source states is not persisted is written by being left out of a
    /// closed mapping, which is a statement only because automatic mapping is off. The member
    /// stays on the class: only the mapping says there is no column behind it.
    /// </summary>
    [Fact]
    public void ATransientPropertyIsLeftOutOfTheMappingAndStaysOnTheClass()
    {
        var (java, mapper) = Build(builder =>
        {
            Customer(builder);
            builder.AddProperty("string", "Note", "public", hasGetter: true, hasSetter: true, isNullable: true);
            builder.MarkTransient("Note");
        });

        Assert.DoesNotContain("property=\"Note\"", mapper);
        Assert.Contains("private String Note;", java);
    }

    /// <summary>
    /// What a mapper has nowhere to put is a loss the descriptor produces mechanically
    /// (decision 010), so the list cannot be forgotten: the table and its schema, the length,
    /// the precision, the nullability, the key mechanism and the version column.
    /// </summary>
    [Fact]
    public void TheFactsAMapperCannotHoldAreReportedAsLosses()
    {
        var builder = new MyBatisEntityBuilder();
        Customer(builder);
        builder.AddTable("Customers");
        builder.AddSchema("Sales");
        builder.SetPropertyDatabaseMapping("CustomerName", new Dictionary<string, string> { ["isnullable"] = "false" });
        builder.Build();

        var lost = builder.Records
            .Where(r => r.Kind == ConversionRecordKind.Loss)
            .Select(r => r.Category)
            .ToList();

        Assert.Contains(MappingFactCategory.TableName, lost);
        Assert.Contains(MappingFactCategory.SchemaName, lost);
        Assert.Contains(MappingFactCategory.Length, lost);
        Assert.Contains(MappingFactCategory.Nullability, lost);
        Assert.Contains(MappingFactCategory.PrimaryKeyStrategy, lost);
    }

    /// <summary>
    /// The literal type the source spelled has no counterpart: jdbcType names a JDBC family,
    /// not a type of one database system, so there is no columnDefinition to put it in.
    /// </summary>
    [Fact]
    public void ALiteralSourceTypeIsReportedAndNotWritten()
    {
        var builder = new MyBatisEntityBuilder();
        Customer(builder);
        builder.SetPropertyDatabaseType("CustomerName", null, sourceSqlType: "nvarchar(200)");
        var mapper = builder.Build().Single(a => a.ContentType == ConversionContentType.XML).Content;

        Assert.DoesNotContain("nvarchar(200)", mapper);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss
            && r.Category == MappingFactCategory.DatabaseType
            && r.Reason.Contains("columnDefinition"));
    }

    /// <summary>
    /// A relation has no place in an entity mapper at all: MyBatis states one only inside the
    /// result shape of a statement, and this document carries none. The member stays on the
    /// class and the loss says why.
    /// </summary>
    [Fact]
    public void ARelationKeepsItsMemberAndIsReportedAsALoss()
    {
        var builder = new MyBatisEntityBuilder();
        Customer(builder);
        builder.AddProperty("List<Order>", "Orders", "public", hasGetter: true, hasSetter: true, defaultValue: "[]");
        builder.AddForeignKey(Cardinality.OneToMany, "Orders", "Order", RelationRole.Inverse);

        var artifacts = builder.Build();
        var java = artifacts.Single(a => a.ContentType == ConversionContentType.JavaEntity).Content;
        var mapper = artifacts.Single(a => a.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("private List<Order> Orders = new ArrayList<>();", java);
        Assert.DoesNotContain("<collection", mapper);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss
            && r.Category == MappingFactCategory.ForeignKeyColumns
            && r.Property == "Orders");
    }

    /* ---- the domain class ------------------------------------------------------------ */

    /// <summary>
    /// The clearest case of the rule that the demands of a target are constraints of the
    /// framework and not facts of the domain: MyBatis has none, so the class imports nothing
    /// from it, extends nothing and implements nothing.
    /// </summary>
    [Fact]
    public void TheDomainClassImportsNothingFromTheFramework()
    {
        var (java, _) = Build(Customer);

        Assert.Equal(Expected("""
            package Shop;

            public class Customer {

                private Integer CustomerId;

                private String CustomerName;

                public Integer getCustomerId() {
                    return CustomerId;
                }

                public void setCustomerId(Integer value) {
                    this.CustomerId = value;
                }

                public String getCustomerName() {
                    return CustomerName;
                }

                public void setCustomerName(String value) {
                    this.CustomerName = value;
                }
            }
            """), java.TrimEnd('\n'));
    }

    /// <summary>
    /// The one member MyBatis needs is the one nobody writes: its ObjectFactory reaches for
    /// the no-arg constructor, which holds only as long as the artifact declares no
    /// constructor at all (decision 009). The descriptor states it as a forbidden marker, and
    /// this is the test the Java suite is to confirm from the other side.
    /// </summary>
    [Fact]
    public void TheClassDeclaresNoConstructorSoTheImplicitOneHolds()
    {
        var (java, _) = Build(Customer);

        Assert.DoesNotContain("public Customer(", java);
        Assert.Equal(
            "no-arg constructor",
            Assert.Single(MyBatisDescriptor.Instance.EnforcedMembers).Name);
    }

    /* ---- the query pair --------------------------------------------------------------- */

    private static ConversionResult QueryToMyBatis(string sql) =>
        ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.MyBatis,
        [
            new() { Content = """
                namespace Shop;

                public class Customer
                {
                    public int CustomerId { get; set; }
                    public string CustomerName { get; set; }
                    public decimal CreditLimit { get; set; }
                }
                """, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = sql, ContentType = ConversionContentType.SqlQuery },
        ]);

    /// <summary>
    /// The conversion emits a mapper for the entity and a mapper for the query; this is the
    /// second one, which is the only one carrying a statement.
    /// </summary>
    private static string QueryMapper(ConversionResult result)
        => result.Sources
            .Single(s => s.ContentType == ConversionContentType.XML && s.Content.Contains("<select", StringComparison.Ordinal))
            .Content.Replace("\r\n", "\n");

    /// <summary>
    /// The two artifacts of a query, and the one thing they must not do: overlap. Writing the
    /// SQL into the method as well would make the generated project exactly the input the
    /// reading side refuses, because MyBatis would not build a factory from it (decision 068).
    /// </summary>
    [Fact]
    public void AQueryBecomesAMethodDeclarationAndAMapperThatDoNotOverlap()
    {
        var result = QueryToMyBatis("SELECT c.CustomerName FROM Customer AS c WHERE c.CreditLimit > @limit");

        var method = result.Sources.Single(s => s.ContentType == ConversionContentType.JavaQuery).Content;

        Assert.Equal("List<Customer> query(@Param(\"limit\") BigDecimal limit);", method);
        Assert.DoesNotContain("@Select", method);

        Assert.Equal(Expected("""
            <?xml version="1.0" encoding="utf-8" ?>
            <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                    "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
            <mapper namespace="Shop.QueryMapper">
                <select id="query" resultType="Shop.Customer">
                    SELECT c.CustomerName
                    FROM Customer AS c
                    WHERE c.CreditLimit &gt; #{limit}
                </select>
            </mapper>
            """), QueryMapper(result));
    }

    /// <summary>
    /// The markup the builder writes is its own; everything that came out of the query text
    /// is escaped, so a comparison written with a less-than sign cannot close the element.
    /// </summary>
    [Fact]
    public void TheQueryTextIsEscaped()
    {
        var result = QueryToMyBatis("SELECT c.CustomerName FROM Customer AS c WHERE c.CreditLimit < 2000");

        Assert.Contains("c.CreditLimit &lt; 2000", QueryMapper(result));
    }

    /// <summary>
    /// The one dynamic tag the builder ever writes, and it writes it in exactly the form the
    /// parser reads back: MyBatis does not expand a list behind #{}, so a collection
    /// parameter has no other spelling. The round trip therefore closes on the single tag the
    /// model carries (decision 084).
    /// </summary>
    [Fact]
    public void ACollectionParameterRoundTripsThroughACanonicalForEach()
    {
        var result = ConversionHandler.Convert(ORMEnum.MyBatis, ORMEnum.MyBatis,
        [
            new() { Content = """
                package Shop;

                public class Customer {
                    private Integer CustomerId;
                    private String CustomerName;
                }
                """, ContentType = ConversionContentType.JavaEntity },
            new() { Content = """
                package Shop;

                import java.util.Collection;
                import java.util.List;
                import org.apache.ibatis.annotations.Param;

                public interface CustomerMapper {
                    List<Customer> findIn(@Param("ids") Collection<Integer> ids);
                }
                """, ContentType = ConversionContentType.JavaQuery },
            new() { Content = """
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                        "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
                <mapper namespace="Shop.CustomerMapper">
                  <resultMap id="customer" type="Customer">
                    <result column="CustomerName" property="CustomerName"/>
                  </resultMap>
                  <select id="findIn" resultMap="customer">
                    SELECT c.CustomerName
                    FROM Customer AS c
                    WHERE c.CustomerId IN
                    <foreach item="item" collection="ids" open="(" separator="," close=")">#{item}</foreach>
                  </select>
                </mapper>
                """, ContentType = ConversionContentType.XML },
        ]);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        Assert.Contains(
            "IN <foreach item=\"item\" collection=\"ids\" open=\"(\" separator=\",\" close=\")\">#{item}</foreach>",
            QueryMapper(result));

        Assert.Contains(
            "Collection<Integer> ids",
            result.Sources.Single(s => s.ContentType == ConversionContentType.JavaQuery).Content);
    }
}
