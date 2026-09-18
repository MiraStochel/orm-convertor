using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Combined;

namespace Tests.EclipseLink;

/// <summary>
/// The translations between the two implementations of one specification that criterion F9
/// asks for. They are the interesting direction precisely because the source and the target
/// read the same annotations: everything that differs is a default behind that shared text,
/// which is what decision 076 warned about and what decision 080 settles - AUTO, the
/// national column, the vendor annotation and the lazy reference.
/// </summary>
public class HibernateToEclipseLinkTest
{
    private static ConversionResult Convert(ORMEnum source, ORMEnum target, string entity, params ConversionSource[] extra)
        => ConversionHandler.Convert(source, target,
        [
            new() { Content = entity, ContentType = ConversionContentType.JavaEntity },
            .. extra,
        ]);

    private static string Entity(ConversionResult result)
    {
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        return result.Sources.Single(s => s.ContentType == ConversionContentType.JavaEntity).Content;
    }

    private const string CustomerWithAuto = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Customers", schema = "Sales")
        public class Customer {
            @Id
            @GeneratedValue
            @Column(name = "CustomerId")
            private Integer CustomerId;

            @Column(name = "CustomerName", length = 200, nullable = false)
            private String CustomerName;
        }
        """;

    /// <summary>
    /// The translation the two implementations make dangerous: one line of the source means
    /// a sequence on one side and a counter table on the other, so copying it over would
    /// have swapped the generator without a word. The artifact names the mechanism instead.
    /// </summary>
    [Fact]
    public void AutoFromHibernateBecomesTheCounterTableOfEclipseLink()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.EclipseLink, CustomerWithAuto);
        var code = Entity(result);

        Assert.Contains("@GeneratedValue(strategy = GenerationType.TABLE", code);
        Assert.Contains("table = \"SEQUENCE\"", code);
        Assert.Contains("pkColumnValue = \"SEQ_GEN\"", code);
        Assert.DoesNotContain("@GeneratedValue\n", code.Replace("\r\n", "\n"));
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Convention
            && r.Category == MappingFactCategory.PrimaryKeyStrategy
            && r.Reason.Contains("EclipseLink resolves it to TABLE"));
    }

    /// <summary>And back: the same silence is a sequence over there, written out just as explicitly.</summary>
    [Fact]
    public void AutoFromEclipseLinkBecomesTheSequenceOfHibernate()
    {
        var result = Convert(ORMEnum.EclipseLink, ORMEnum.Hibernate, CustomerWithAuto);
        var code = Entity(result);

        Assert.Contains("@GeneratedValue(strategy = GenerationType.SEQUENCE", code);
        Assert.Contains("@SequenceGenerator(name = \"Customer_CustomerId_gen\", sequenceName = \"Customer_SEQ\", allocationSize = 50)", code);
        Assert.DoesNotContain("@TableGenerator", code);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Convention
            && r.Reason.Contains("Hibernate resolves it to SEQUENCE"));
    }

    /// <summary>
    /// The unicode facet is one fact in two spellings (decision 019): an annotation on one
    /// side, a literal type on the other, because EclipseLink has no annotation for it.
    /// </summary>
    [Fact]
    public void NationalizedBecomesALiteralTypeAndBackAnAnnotation()
    {
        var toEclipseLinkResult = Convert(ORMEnum.Hibernate, ORMEnum.EclipseLink, """
            package Shop;

            import jakarta.persistence.*;
            import org.hibernate.annotations.Nationalized;

            @Entity
            @Table(name = "Customers")
            public class Customer {
                @Id
                @Column(name = "CustomerId")
                private Integer CustomerId;

                @Nationalized
                @Column(name = "CustomerName", length = 200)
                private String CustomerName;
            }
            """);

        var toEclipseLink = Entity(toEclipseLinkResult);

        // The source states the facet and no type at all, so the family comes from the
        // language type and the artifact says so - the case the first CI run caught.
        Assert.Contains("columnDefinition = \"nvarchar(200)\"", toEclipseLink);
        Assert.DoesNotContain("@Nationalized", toEclipseLink);
        Assert.DoesNotContain("org.hibernate", toEclipseLink);
        Assert.Contains(toEclipseLinkResult.Records, r => r.Kind == ConversionRecordKind.Convention
            && r.Category == MappingFactCategory.DatabaseType
            && r.Property == "CustomerName");

        var toHibernate = Entity(Convert(ORMEnum.EclipseLink, ORMEnum.Hibernate, """
            package Shop;

            import jakarta.persistence.*;

            @Entity
            @Table(name = "Customers")
            public class Customer {
                @Id
                @Column(name = "CustomerId")
                private Integer CustomerId;

                @Column(name = "CustomerName", length = 200, columnDefinition = "nvarchar(200)")
                private String CustomerName;
            }
            """));

        Assert.Contains("import org.hibernate.annotations.Nationalized;", toHibernate);
        Assert.Contains("@Nationalized", toHibernate);
    }

    /// <summary>
    /// An annotation of the other implementation is a loss with its name, the same answer
    /// the tool gives between NHibernate and EF Core - the vendor half of the third hook.
    /// </summary>
    [Fact]
    public void AVendorAnnotationOfEitherImplementationIsDroppedWithItsName()
    {
        var toEclipseLink = Convert(ORMEnum.Hibernate, ORMEnum.EclipseLink, """
            package Shop;

            import jakarta.persistence.*;
            import org.hibernate.annotations.Formula;

            @Entity
            @Table(name = "Customers")
            public class Customer {
                @Id
                @Column(name = "CustomerId")
                private Integer CustomerId;

                @Formula("CreditLimit * 2")
                private Integer DoubleLimit;
            }
            """);

        Assert.Contains(toEclipseLink.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("Formula"));

        var toHibernate = Convert(ORMEnum.EclipseLink, ORMEnum.Hibernate, """
            package Shop;

            import jakarta.persistence.*;
            import org.eclipse.persistence.annotations.AdditionalCriteria;

            @Entity
            @Table(name = "Customers")
            @AdditionalCriteria("this.CreditLimit > 0")
            public class Customer {
                @Id
                @Column(name = "CustomerId")
                private Integer CustomerId;
            }
            """);

        Assert.Contains(toHibernate.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("AdditionalCriteria"));
    }

    /// <summary>
    /// The fact no artifact shows (decision 080): a lazy reference read from an EclipseLink
    /// source is reported, and the artifact written for either target states no strategy at
    /// all, because the representation carries none.
    /// </summary>
    [Fact]
    public void ALazyReferenceIsReportedAndNoFetchStrategyTravels()
    {
        var result = ConversionHandler.Convert(ORMEnum.EclipseLink, ORMEnum.Hibernate,
        [
            new()
            {
                Content = """
                    package Shop;

                    import jakarta.persistence.*;

                    @Entity
                    @Table(name = "Customers")
                    public class Customer {
                        @Id
                        @Column(name = "CustomerId")
                        private Integer CustomerId;
                    }
                    """,
                ContentType = ConversionContentType.JavaEntity,
            },
            new()
            {
                Content = """
                    package Shop;

                    import jakarta.persistence.*;

                    @Entity
                    @Table(name = "CustomerOrders")
                    public class CustomerOrder {
                        @Id
                        @Column(name = "OrderId")
                        private Integer OrderId;

                        @ManyToOne(fetch = FetchType.LAZY)
                        @JoinColumn(name = "CustomerId", referencedColumnName = "CustomerId")
                        private Customer Customer;
                    }
                    """,
                ContentType = ConversionContentType.JavaEntity,
            },
        ]);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("weaves its bytecode"));

        var order = result.Sources
            .Single(s => s.ContentType == ConversionContentType.JavaEntity && s.Content.Contains("class CustomerOrder"))
            .Content;

        Assert.Contains("@ManyToOne", order);
        Assert.DoesNotContain("FetchType", order);
        Assert.Contains("@JoinColumn(name = \"CustomerId\", referencedColumnName = \"CustomerId\")", order);
    }

    /// <summary>
    /// The specification's own part travels unchanged, key class and all: between these two
    /// targets the artifact differs only where the profile differs (decisions 006 and 076).
    /// </summary>
    [Fact]
    public void ACompositeKeyCrossesUnchanged()
    {
        var code = Entity(Convert(ORMEnum.Hibernate, ORMEnum.EclipseLink, """
            package Shop;

            import jakarta.persistence.*;
            import java.io.Serializable;

            @Entity
            @IdClass(OrderLine.OrderLineId.class)
            @Table(name = "OrderLines")
            public class OrderLine {
                @Id
                @Column(name = "OrderId")
                private Integer OrderId;

                @Id
                @Column(name = "LineNumber")
                private Integer LineNumber;

                public static class OrderLineId implements Serializable {
                    private Integer OrderId;
                    private Integer LineNumber;
                }
            }
            """));

        Assert.Contains("@IdClass(OrderLine.OrderLineId.class)", code);
        Assert.Contains("public static class OrderLineId implements Serializable {", code);
        Assert.Contains("private Integer OrderId;", code);
        Assert.Contains("private Integer LineNumber;", code);
    }

    /// <summary>
    /// The query side of the same pair: the window HQL states inside the text is a fact of
    /// the query for both, and EclipseLink puts it where the specification does - on the
    /// query object, because EQL has no clause for it.
    /// </summary>
    [Fact]
    public void ThePaginationOfHqlBecomesTheWindowOfTheQueryObject()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EclipseLink,
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.Hibernate),
            new()
            {
                Content = "select c\nfrom Customer c\norder by c.CustomerName asc\nlimit 5 offset 10",
                ContentType = ConversionContentType.JpqlQuery,
            },
        ]);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Artifact?.IsQuery() == true);

        var method = result.Sources.Single(s => s.ContentType == ConversionContentType.JavaQuery).Content;
        var jpql = result.Sources.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;

        Assert.Contains("setFirstResult(10)", method);
        Assert.Contains("setMaxResults(5)", method);
        Assert.DoesNotContain("limit", jpql);
    }
}
