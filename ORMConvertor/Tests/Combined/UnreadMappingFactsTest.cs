using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The silent places of the readers, closed (decisions 004 and 048): facts a source states
/// that the model has no place for and that used to vanish without a record. One class for
/// all of them, because the rule is one rule - a fact the reading drops says so - and the
/// test of each place is the same test: the source states it, the records name it, and the
/// artifact still comes out. Where a statement is not a loss - the convention derives the
/// same thing, or the name is the key the model pairs with anyway - the silence is the
/// right answer and is held to as well.
/// </summary>
public class UnreadMappingFactsTest
{
    private static ConversionSource Source(ConversionContentType type, string content, string name) => new()
    {
        Name = name,
        ContentType = type,
        Content = content,
    };

    private static List<ConversionRecord> Losses(ConversionResult result, string phrase)
        => [.. result.Records.Where(r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains(phrase, StringComparison.Ordinal))];

    /* ---- C#: the structural reading ------------------------------------------------- */

    private const string AnnotatedCSharpClass = """
        using System;

        namespace Shop;

        [Serializable]
        public class Customer
        {
            private string cache;

            public static int Counter;

            public const int MaxNameLength = 100;

            [Obsolete("renamed")]
            public int CustomerId { get; set; }

            public string CustomerName { get; set; }
        }
        """;

    /// <summary>
    /// Dapper and NHibernate carry no mapping in the class, so the shared reading takes the
    /// structure alone - and used to take the annotations with it, in silence. Each
    /// annotation is now a record naming where it sat; the class is still translated.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.NHibernate)]
    public void AnAnnotationTheStructuralReadingDoesNotReadIsAReportedLoss(ORMEnum source)
    {
        var result = ConversionHandler.Convert(source, ORMEnum.EFCore,
            [Source(ConversionContentType.CSharp, AnnotatedCSharpClass, "Customer.cs")]);

        var onClass = Assert.Single(Losses(result, "[Serializable] on the class 'Customer'"));
        Assert.Equal("Customer", onClass.Entity);
        Assert.Null(onClass.Property);

        var onProperty = Assert.Single(Losses(result, "[Obsolete] on the property 'CustomerId'"));
        Assert.Equal("CustomerId", onProperty.Property);

        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity && s.Content.Contains("class Customer"));
    }

    /// <summary>
    /// The model carries properties only, so a field leaves the class in every reading; the
    /// record says so and adds what the framework would have made of it - a column with
    /// Dapper, a column under access="field" with NHibernate, only the member with EF Core.
    /// Constants and static fields are no members of an instance and get no record.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, "Dapper maps a field by its name")]
    [InlineData(ORMEnum.NHibernate, "access=\"field\"")]
    [InlineData(ORMEnum.EFCore, "EF Core maps no field by convention")]
    public void AFieldOfTheClassIsAReportedLoss(ORMEnum source, string note)
    {
        var result = ConversionHandler.Convert(source, ORMEnum.EFCore,
            [Source(ConversionContentType.CSharp, AnnotatedCSharpClass, "Customer.cs")]);

        var field = Assert.Single(Losses(result, "The field 'cache' of the class 'Customer'"));
        Assert.Equal("cache", field.Property);
        Assert.Contains(note, field.Reason);

        Assert.Empty(Losses(result, "The field 'Counter'"));
        Assert.Empty(Losses(result, "The field 'MaxNameLength'"));
    }

    /* ---- EF Core: the scalar form of [ForeignKey] ----------------------------------- */

    private static string OrderWithForeignKeyOn(string property) => $$"""
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Shop;

        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
        }

        public class Order
        {
            [Key]
            public int OrderId { get; set; }

            [ForeignKey("Customer")]
            public int {{property}} { get; set; }

            public Customer Customer { get; set; }
        }
        """;

    /// <summary>
    /// [ForeignKey("Navigation")] on the key property is the other legal form of the
    /// annotation, and the reading pairs a column with its navigation from the navigation's
    /// side only, so the claim used to fall out without a trace. Now it is a record - except
    /// where the property is named as the convention finds it anyway, since the annotation
    /// then states nothing the convention does not derive.
    /// </summary>
    [Theory]
    [InlineData("CustomerRef", true)]
    [InlineData("CustomerId", false)]
    public void TheScalarFormOfForeignKeyIsAReportedLossUnlessTheConventionDerivesIt(string property, bool reported)
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate,
            [Source(ConversionContentType.CSharp, OrderWithForeignKeyOn(property), "Order.cs")]);

        var losses = Losses(result, "[ForeignKey(\"Customer\")] on the scalar property");

        if (!reported)
        {
            Assert.Empty(losses);
            return;
        }

        var loss = Assert.Single(losses);
        Assert.Equal("Order", loss.Entity);
        Assert.Equal(property, loss.Property);
    }

    /* ---- Java: the structural reading ----------------------------------------------- */

    /// <summary>
    /// The Java counterpart of the C# case: a MyBatis domain class carries no mapping, so an
    /// annotation on it or on a field - a Lombok @Data, a validation constraint - is read by
    /// nobody and now says so.
    /// </summary>
    [Fact]
    public void AnAnnotationOnAMyBatisDomainClassIsAReportedLoss()
    {
        var result = ConversionHandler.Convert(ORMEnum.MyBatis, ORMEnum.EFCore,
        [
            Source(ConversionContentType.Java, """
                package shop;

                import lombok.Data;
                import jakarta.validation.constraints.NotBlank;

                @Data
                public class Customer {
                    private Integer customerId;

                    @NotBlank
                    private String customerName;
                }
                """, "Customer.java"),
        ]);

        var onClass = Assert.Single(Losses(result, "@Data on the class 'Customer'"));
        Assert.Null(onClass.Property);

        var onField = Assert.Single(Losses(result, "@NotBlank on the attribute 'customerName'"));
        Assert.Equal("customerName", onField.Property);
    }

    /* ---- JPA: the elements of @Table and @Column outside the subset ----------------- */

    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void TableAndColumnElementsOutsideTheSubsetAreReportedLosses(ORMEnum source)
    {
        var result = ConversionHandler.Convert(source, ORMEnum.EFCore,
        [
            Source(ConversionContentType.Java, """
                package shop;

                import jakarta.persistence.*;

                @Entity
                @Table(name = "Customers", catalog = "Shop", indexes = { @Index(columnList = "CustomerName") })
                public class Customer {
                    @Id
                    private Integer customerId;

                    @Column(name = "CustomerName", table = "CustomerDetails")
                    private String customerName;

                    @Column(name = "Note", insertable = false, updatable = false)
                    private String note;
                }
                """, "Customer.java"),
        ]);

        Assert.Single(Losses(result, "The class annotation Table(catalog)"));
        Assert.Single(Losses(result, "The class annotation Table(indexes)"));

        // A column of a secondary table is excluded area 2: the attribute leaves the model
        // with the one record (decision 177).
        var table = Assert.Single(Losses(result, "The property is mapped by @Column(table = \"CustomerDetails\")"));
        Assert.Equal("customerName", table.Property);
        Assert.DoesNotContain("customerName", Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity).Content);

        var insertable = Assert.Single(Losses(result, "Column(insertable = false)"));
        Assert.Equal("note", insertable.Property);
        Assert.Single(Losses(result, "Column(updatable = false)"));
    }

    /// <summary>
    /// insertable = false, updatable = false over the column a relation of the same class
    /// maps is the read-only idiom of a scalar beside its relation: the model derives it
    /// from the relation and every target writes it again in its own words (decision 012),
    /// so there it is no loss and gets no record.
    /// </summary>
    [Fact]
    public void TheReadOnlyIdiomOverARelationsColumnIsNotALoss()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
        [
            Source(ConversionContentType.Java, """
                package shop;

                import jakarta.persistence.*;

                @Entity
                public class Customer {
                    @Id
                    private Integer customerId;
                }

                @Entity
                public class Order {
                    @Id
                    private Integer orderId;

                    @Column(name = "CustomerId", insertable = false, updatable = false)
                    private Integer customerId;

                    @ManyToOne
                    @JoinColumn(name = "CustomerId")
                    private Customer customer;
                }
                """, "Shop.java"),
        ]);

        Assert.Empty(Losses(result, "insertable = false"));
        Assert.Empty(Losses(result, "updatable = false"));
    }

    [Fact]
    public void TheOrmXmlSpellingsOfTheSameElementsAreReportedLosses()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
        [
            Source(ConversionContentType.XML, """
                <?xml version="1.0" encoding="UTF-8"?>
                <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                    <entity class="shop.Customer">
                        <table name="Customers" catalog="Shop">
                            <index column-list="CustomerName"/>
                        </table>
                        <attributes>
                            <id name="customerId"/>
                            <basic name="customerName">
                                <column name="CustomerName" table="CustomerDetails" insertable="false" updatable="false"/>
                            </basic>
                            <basic name="note">
                                <column name="Note" insertable="false" updatable="false"/>
                            </basic>
                        </attributes>
                    </entity>
                </entity-mappings>
                """, "orm.xml"),
            Source(ConversionContentType.Java, """
                package shop;

                public class Customer {
                    private Integer customerId;
                    private String customerName;
                    private String note;
                }
                """, "Customer.java"),
        ]);

        Assert.Single(Losses(result, "The class annotation <table catalog>"));
        Assert.Single(Losses(result, "The class annotation <index>"));

        // The column of a secondary table takes the attribute out whole, the write flags beside
        // it with it; the same flags on a column of the entity's own table are reported (decision 177).
        var table = Assert.Single(result.Records, r => r.Property == "customerName");
        Assert.Equal(ConversionRecordKind.Loss, table.Kind);
        Assert.Contains("<column table=\"CustomerDetails\">", table.Reason);

        Assert.Equal("note", Assert.Single(Losses(result, "<column insertable=\"false\">")).Property);
        Assert.Equal("note", Assert.Single(Losses(result, "<column updatable=\"false\">")).Property);
    }

    /// <summary>
    /// The same two flags on a join column are the mirror image: beside a scalar mapping the
    /// same column they are the idiom and say nothing; on a join column of their own they
    /// are a loss, as are table and columnDefinition, which the model has no place for.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JoinColumnElementsOutsideTheSubsetAreReportedLossesUnlessBesideAScalar(bool besideScalar)
    {
        var scalar = besideScalar ? "@Column(name = \"CustomerId\") private Integer customerId;" : string.Empty;

        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
        [
            Source(ConversionContentType.Java, $$"""
                package shop;

                import jakarta.persistence.*;

                @Entity
                public class Customer {
                    @Id
                    private Integer customerId;
                }

                @Entity
                public class Order {
                    @Id
                    private Integer orderId;

                    {{scalar}}

                    @ManyToOne
                    @JoinColumn(name = "CustomerId", table = "OrderDetails", columnDefinition = "int",
                        insertable = false, updatable = false)
                    private Customer customer;
                }
                """, "Shop.java"),
        ]);

        var table = Assert.Single(Losses(result, "The annotation JoinColumn(table)"));
        Assert.Equal("customer", table.Property);
        Assert.Single(Losses(result, "The annotation JoinColumn(columnDefinition)"));

        var flags = Losses(result, "JoinColumn(insertable = false)").Concat(Losses(result, "JoinColumn(updatable = false)")).ToList();

        Assert.Equal(besideScalar ? 0 : 2, flags.Count);
    }

    /* ---- JPA: referencedColumnName ------------------------------------------------- */

    private static string OrderReferencing(string column) => $$"""
        package shop;

        import jakarta.persistence.*;

        @Entity
        public class Customer {
            @Id
            @Column(name = "CustomerId")
            private Integer id;

            @Column(name = "CustomerNumber")
            private String number;
        }

        @Entity
        public class Order {
            @Id
            private Integer orderId;

            @ManyToOne
            @JoinColumn(name = "CustomerId", referencedColumnName = "{{column}}")
            private Customer customer;
        }
        """;

    /// <summary>
    /// referencedColumnName was read and used by nobody. The model pairs a foreign key with
    /// the referenced entity's primary key (decision 012), so naming that key adds nothing
    /// and stays silent, while naming another column is a relation the model cannot keep
    /// and is a record - judged once the referenced entity's key is known, whichever unit
    /// declares it.
    /// </summary>
    [Theory]
    [InlineData("CustomerId", false)]
    [InlineData("customerid", false)]
    [InlineData("CustomerNumber", true)]
    public void AReferencedColumnOtherThanTheKeyIsAReportedLoss(string column, bool reported)
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
            [Source(ConversionContentType.Java, OrderReferencing(column), "Shop.java")]);

        var losses = Losses(result, "states that its foreign key references");

        if (!reported)
        {
            Assert.Empty(losses);
            return;
        }

        var loss = Assert.Single(losses);
        Assert.Equal("Order", loss.Entity);
        Assert.Equal("customer", loss.Property);
        Assert.Null(loss.Category);
        Assert.Contains("'CustomerNumber' of Customer", loss.Reason);
        Assert.Contains("whose primary key is 'CustomerId'", loss.Reason);
    }

    /// <summary>
    /// A referenced entity outside the conversion has no key to compare with, so the claim
    /// is dropped without a record, like every other fact the catalog may still settle.
    /// </summary>
    [Fact]
    public void AReferencedColumnOfAnEntityOutsideTheConversionIsNotJudged()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
        [
            Source(ConversionContentType.Java, """
                package shop;

                import jakarta.persistence.*;

                @Entity
                public class Order {
                    @Id
                    private Integer orderId;

                    @ManyToOne
                    @JoinColumn(name = "CustomerId", referencedColumnName = "CustomerNumber")
                    private Customer customer;
                }
                """, "Order.java"),
        ]);

        Assert.Empty(Losses(result, "states that its foreign key references"));
    }

    /* ---- MyBatis: the mapper elements outside the table of decision 084 ------------- */

    [Fact]
    public void CacheParameterMapAndTypeHandlerAreReportedLosses()
    {
        var result = ConversionHandler.Convert(ORMEnum.MyBatis, ORMEnum.EFCore,
        [
            Source(ConversionContentType.Java, """
                package shop;

                public class Customer {
                    private Integer customerId;
                    private String customerName;
                }
                """, "Customer.java"),
            Source(ConversionContentType.XML, """
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                        "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
                <mapper namespace="shop.CustomerMapper">
                    <cache/>
                    <cache-ref namespace="shop.OrderMapper"/>
                    <parameterMap id="customerParameters" type="Customer">
                        <parameter property="customerId"/>
                    </parameterMap>
                    <resultMap id="customerMap" type="Customer">
                        <id property="customerId" column="CustomerId"/>
                        <result property="customerName" column="CustomerName" typeHandler="org.apache.ibatis.type.StringTypeHandler"/>
                    </resultMap>
                </mapper>
                """, "CustomerMapper.xml"),
        ]);

        var cache = Assert.Single(Losses(result, "<cache> switches on the second-level cache"));
        Assert.Null(cache.Entity);

        Assert.Single(Losses(result, "<cache-ref namespace=\"shop.OrderMapper\">"));
        Assert.Single(Losses(result, "<parameterMap id=\"customerParameters\">"));

        var handler = Assert.Single(Losses(result, "The type handler 'org.apache.ibatis.type.StringTypeHandler'"));
        Assert.Equal("Customer", handler.Entity);
        Assert.Equal("customerName", handler.Property);
    }

    /* ---- The JPA target: a collection with no relation behind it -------------------- */

    /// <summary>
    /// A bare collection field is not nothing to a JPA provider: Hibernate 7 reads it as a
    /// basic attribute over an xml column, EclipseLink as a serialized image - a column the
    /// source never claimed, written with a plain @Column and no record. The member now stays
    /// on the class as @Transient with the record the NHibernate and MyBatis builders write
    /// for the same shape. The source is a JPA attribute of a collection type with no
    /// annotation at all, so no relation stands behind it; EF Core's primitive collection
    /// never gets this far, being excluded area 2 (decision 177).
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void ACollectionWithoutARelationIsTransientInTheJpaTarget(ORMEnum target)
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, target,
        [
            Source(ConversionContentType.Java, """
                package shop;

                import jakarta.persistence.*;
                import java.util.List;

                @Entity
                public class Customer {
                    @Id
                    private Integer customerId;

                    private List<String> Tags;
                }
                """, "Customer.java"),
        ]);

        var entity = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaEntity);
        var code = entity.Content.Replace("\r\n", "\n");

        Assert.Contains("@Transient\n    private List<String> Tags", code);
        Assert.DoesNotContain("@Column(name = \"Tags\"", code);

        var gap = Assert.Single(result.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Property == "Tags" && r.Reason.Contains("has no relation behind it"));

        Assert.Equal(target, gap.Framework);
        Assert.Contains("written @Transient", gap.Reason);
    }
}
