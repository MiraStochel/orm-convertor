using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using SampleData;

namespace Tests.Combined;

/// <summary>
/// The first translations across the ecosystems (F10, decision 077): hbm.xml into
/// annotations through the pivot, and a Java entity into the two .NET annotation forms.
/// The matrix of every direction is <see cref="QueryMatrixTest"/> and its siblings; this
/// class asserts the shape where the two ecosystems spell a fact differently.
/// </summary>
public class CrossEcosystemTest
{
    private static ConversionResult Convert(ORMEnum source, ORMEnum target, params ConversionSource[] units)
        => ConversionHandler.Convert(source, target, [.. units]);

    private static string Entity(ConversionResult result, ConversionContentType language)
        => result.Sources.Single(s => s.ContentType == language).Content;

    /// <summary>The hbm.xml → annotations road of decision 013: the NHibernate sample as a Hibernate entity.</summary>
    [Fact]
    public void NHibernateMappingBecomesJpaAnnotations()
    {
        var result = Convert(ORMEnum.NHibernate, ORMEnum.Hibernate,
            new() { Content = CustomerSampleNHibernate.Entity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = CustomerSampleNHibernate.XmlMapping, ContentType = ConversionContentType.XML });

        var java = Entity(result, ConversionContentType.JavaEntity);

        Assert.Contains("package NHibernateEntities;", java);
        Assert.Contains("@Table(name = \"Customers\", schema = \"Sales\")", java);
        Assert.Contains("@GeneratedValue(strategy = GenerationType.IDENTITY)", java);
        Assert.Contains("@Column(name = \"CustomerID\")", java);
        Assert.Contains("private Integer CustomerID;", java);
        Assert.Contains("@Column(name = \"CustomerName\", length = 200, nullable = false)", java);
        Assert.Contains("@Nationalized", java); // type="String" is nvarchar in NHibernate
        Assert.Contains("@Column(name = \"AccountOpenedDate\", precision = 7, nullable = false)", java);
        Assert.Contains("private LocalDateTime AccountOpenedDate;", java);
        Assert.Contains("private BigDecimal CreditLimit;", java);
        Assert.DoesNotContain("virtual", java);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>A sequence generator with its parameters crosses over as @SequenceGenerator (decision 020).</summary>
    [Fact]
    public void ANHibernateSequenceGeneratorBecomesASequenceGenerator()
    {
        var result = Convert(ORMEnum.NHibernate, ORMEnum.Hibernate,
            new()
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = """
                    public class Invoice
                    {
                        public virtual long InvoiceId { get; set; }
                        public virtual string Number { get; set; }
                    }
                    """,
            },
            new()
            {
                ContentType = ConversionContentType.XML,
                Content = """
                    <?xml version="1.0" encoding="utf-8" ?>
                    <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                        <class name="Invoice" table="Invoices">
                            <id name="InvoiceId" column="InvoiceId" type="Int64">
                                <generator class="sequence">
                                    <param name="sequence">Sales.invoice_seq</param>
                                </generator>
                            </id>
                            <property name="Number" not-null="true" length="20" />
                        </class>
                    </hibernate-mapping>
                    """,
            });

        var java = Entity(result, ConversionContentType.JavaEntity);

        Assert.Contains("@GeneratedValue(strategy = GenerationType.SEQUENCE, generator = \"Invoice_InvoiceId_gen\")", java);
        Assert.Contains("@SequenceGenerator(name = \"Invoice_InvoiceId_gen\", sequenceName = \"invoice_seq\", schema = \"Sales\", allocationSize = 50)", java);
        Assert.Contains("private Long InvoiceId;", java);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("allocationSize = 50"));
    }

    /// <summary>A composite-id with a key class crosses over as @IdClass named after the source's class (decision 031).</summary>
    [Fact]
    public void ANHibernateCompositeIdBecomesAnIdClass()
    {
        var result = Convert(ORMEnum.NHibernate, ORMEnum.Hibernate,
            new()
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = """
                    public class OrderLine
                    {
                        public virtual int OrderId { get; set; }
                        public virtual int LineNo { get; set; }
                        public virtual int Quantity { get; set; }
                    }

                    [Serializable]
                    public class OrderLineKey
                    {
                        public virtual int OrderId { get; set; }
                        public virtual int LineNo { get; set; }
                    }
                    """,
            },
            new()
            {
                ContentType = ConversionContentType.XML,
                Content = """
                    <?xml version="1.0" encoding="utf-8" ?>
                    <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                        <class name="OrderLine" table="OrderLines">
                            <composite-id class="OrderLineKey">
                                <key-property name="OrderId" column="OrderId" type="Int32" />
                                <key-property name="LineNo" column="LineNo" type="Int32" />
                            </composite-id>
                            <property name="Quantity" not-null="true" type="Int32" />
                        </class>
                    </hibernate-mapping>
                    """,
            });

        var java = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaEntity).Content;

        Assert.Contains("@IdClass(OrderLine.OrderLineKey.class)", java);
        Assert.Contains("public static class OrderLineKey implements Serializable {", java);
        Assert.Contains("private int Quantity;", java);
    }

    /// <summary>The Java entity as the two .NET annotation forms: types, nullability and the identifier translate by the language axis.</summary>
    [Fact]
    public void AHibernateEntityBecomesAnEfCoreEntity()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
            new ConversionSource { Content = CustomerSampleHibernate.Entity, ContentType = ConversionContentType.JavaEntity });

        var csharp = Entity(result, ConversionContentType.CSharpEntity);

        Assert.Contains("namespace HibernateEntities;", csharp);
        Assert.Contains("[Table(\"Customers\", Schema = \"Sales\")]", csharp);
        Assert.Contains("[Key]", csharp);
        Assert.Contains("public required int CustomerID { get; set; }", csharp);
        Assert.Contains("[MaxLength(200)]", csharp);
        Assert.Contains("public required string CustomerName { get; set; }", csharp);
        Assert.Contains("[Precision(7)]", csharp);
        Assert.Contains("public required DateTime AccountOpenedDate { get; set; }", csharp);
        Assert.Contains("[Precision(18, 2)]", csharp);
        Assert.Contains("public decimal? CreditLimit { get; set; }", csharp);

        // An initialized Java collection is never null; its initializer is Java's spelling
        // and does not travel, so the C# side is the non-nullable property without one.
        Assert.Contains("public required List<CustomerTransaction> Transactions { get; set; }", csharp);
        Assert.Empty(result.Records.Where(r => r.Kind == ConversionRecordKind.Failure));
    }

    [Fact]
    public void AHibernateEntityBecomesAnNHibernateMapping()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.NHibernate,
            new ConversionSource { Content = CustomerSampleHibernate.Entity, ContentType = ConversionContentType.JavaEntity });

        var csharp = Entity(result, ConversionContentType.CSharpEntity);
        var xml = Entity(result, ConversionContentType.XML);

        Assert.Contains("public virtual int CustomerID { get; set; }", csharp);
        Assert.Contains("public virtual string CustomerName { get; set; }", csharp);
        Assert.Contains("<class name=\"Customer\" table=\"Customers\" schema=\"Sales\">", xml);
        Assert.Contains("<generator class=\"identity\" />", xml);
        Assert.Contains("<property name=\"CustomerName\" column=\"CustomerName\" not-null=\"true\" length=\"200\"", xml);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>The JPQL sample as SQL and as LINQ: names go through the mapping both ways.</summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, ConversionContentType.SqlQuery, "FROM Sales.Customers AS c")]
    [InlineData(ORMEnum.EFCore, ConversionContentType.CSharpQuery, "ctx.Set<Customer>()")]
    [InlineData(ORMEnum.NHibernate, ConversionContentType.HqlQuery, "from Customer c")]
    public void AJpqlQueryBecomesEachDotNetLanguage(ORMEnum target, ConversionContentType language, string hallmark)
    {
        var result = Convert(ORMEnum.Hibernate, target,
            new() { Content = CustomerSampleHibernate.Entity, ContentType = ConversionContentType.JavaEntity },
            new() { Content = CustomerSampleHibernate.JpqlQuery, ContentType = ConversionContentType.JpqlQuery });

        var query = result.Sources.Single(s => s.ContentType == language).Content;

        Assert.Contains(hallmark, query);
        Assert.Contains("2000", query);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>A Java type with no counterpart stays as written and is reported (decisions 014 and 075), whichever target.</summary>
    [Fact]
    public void AnUnknownJavaTypeIsRepeatedAndReported()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
            new ConversionSource
            {
                ContentType = ConversionContentType.JavaEntity,
                Content = """
                    import jakarta.persistence.*;
                    import java.time.Instant;

                    @Entity
                    public class Event {
                        @Id private Integer id;
                        private Instant at;
                    }
                    """,
            });

        var csharp = Entity(result, ConversionContentType.CSharpEntity);
        Assert.True(csharp.Contains("public Instant? at { get; set; }"), csharp);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Property == "at" && r.Reason.Contains("Instant"));
    }
}
