using AbstractWrappers.Diagnostics;
using HibernateWrappers;
using JakartaPersistence;
using Model;
using OrmConvertor;
using SampleData;

namespace Tests.Hibernate;

/// <summary>
/// The round trip that pins the Java reader to the Java writer (decisions 062 and 077):
/// what the builder emits, the parser reads back into the same representation, and the
/// builder emits again as the same text.
/// </summary>
public class HibernateToHibernateTest
{
    private static string Rebuild(string java)
    {
        var builder = new HibernateEntityBuilder();
        new HibernateEntityParser(builder, new JpaReadingContext()).Parse(java);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return artifacts.Single(a => a.ContentType == ConversionContentType.JavaEntity).Content;
    }

    [Fact]
    public void TheSampleEntityIsAFixedPoint()
    {
        var first = Rebuild(CustomerSampleHibernate.Entity);
        var second = Rebuild(first);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ACompositeKeyWithItsNestedKeyClassIsAFixedPoint()
    {
        const string java = """
            package Shop;

            import jakarta.persistence.Column;
            import jakarta.persistence.Entity;
            import jakarta.persistence.Id;
            import jakarta.persistence.IdClass;
            import jakarta.persistence.Table;
            import java.io.Serializable;
            import java.util.Objects;

            @Entity
            @Table(name = "OrderLines")
            @IdClass(OrderLine.OrderLineId.class)
            public class OrderLine {

                @Id
                @Column(name = "OrderId")
                private Integer OrderId;

                @Id
                @Column(name = "LineNo")
                private Integer LineNo;

                @Column(name = "Quantity", nullable = false)
                private int Quantity;

                public static class OrderLineId implements Serializable {
                    private Integer OrderId;
                    private Integer LineNo;

                    public OrderLineId() {
                    }

                    @Override
                    public boolean equals(Object obj) {
                        return obj instanceof OrderLineId other
                            && Objects.equals(OrderId, other.OrderId)
                            && Objects.equals(LineNo, other.LineNo);
                    }

                    @Override
                    public int hashCode() {
                        return Objects.hash(OrderId, LineNo);
                    }
                }

                public Integer getOrderId() {
                    return OrderId;
                }

                public void setOrderId(Integer value) {
                    this.OrderId = value;
                }
            }
            """;

        var first = Rebuild(java);
        var second = Rebuild(first);

        Assert.Equal(first, second);
        Assert.Contains("@IdClass(OrderLine.OrderLineId.class)", second);
        Assert.Contains("public static class OrderLineId implements Serializable {", second);
    }

    [Fact]
    public void RelationsSurviveTheRoundTrip()
    {
        const string java = """
            import jakarta.persistence.*;
            import java.util.List;
            import java.util.ArrayList;

            @Entity
            @Table(name = "Authors")
            public class Author {
                @Id @Column(name = "AuthorId") private Integer AuthorId;

                @OneToMany(mappedBy = "Author")
                private List<Book> Books = new ArrayList<>();
            }

            @Entity
            @Table(name = "Books")
            public class Book {
                @Id @Column(name = "BookId") private Integer BookId;

                @ManyToOne(optional = false)
                @JoinColumn(name = "AuthorId", referencedColumnName = "AuthorId", nullable = false)
                private Author Author;
            }
            """;

        var builder = new HibernateEntityBuilder();
        new HibernateEntityParser(builder, new JpaReadingContext()).Parse(java);
        var artifacts = builder.Build();

        // Each artifact is a Java file of its own, so each is read back as its own unit.
        var rebuilt = new HibernateEntityBuilder();
        var parser = new HibernateEntityParser(rebuilt, new JpaReadingContext());
        foreach (var artifact in artifacts)
        {
            parser.Parse(artifact.Content);
        }

        var combined = string.Join("\n\n", artifacts.Select(a => a.Content));
        var again = string.Join("\n\n", rebuilt.Build().Select(a => a.Content));

        Assert.Equal(combined, again);
        Assert.Contains("@OneToMany(mappedBy = \"Author\")", again);
        Assert.Contains("@ManyToOne(optional = false)", again);
        Assert.Contains("@JoinColumn(name = \"AuthorId\", referencedColumnName = \"AuthorId\", nullable = false)", again);
    }

    [Fact]
    public void TheOrchestrationTranslatesHibernateToHibernate()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.Hibernate,
        [
            new() { Content = CustomerSampleHibernate.Entity, ContentType = ConversionContentType.JavaEntity },
            new() { Content = CustomerSampleHibernate.JpqlQuery, ContentType = ConversionContentType.JpqlQuery },
        ]);

        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.JavaEntity);
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.JavaQuery);
        var jpql = result.Sources.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
        Assert.Equal(CustomerSampleHibernate.JpqlQuery.Trim(), jpql.Trim(), ignoreLineEndingDifferences: true);
        Assert.Equal("7.4.5.Final", result.SourceFrameworkVersion);
        Assert.Equal("7.4.5.Final", result.TargetFrameworkVersion);
    }
}
