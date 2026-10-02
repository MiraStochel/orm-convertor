using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The key of an inverse collection in an NHibernate mapping (decision 012). A source that
/// states the collection as the inverse side without columns of its own - JPA's
/// @OneToMany(mappedBy), an EF Core collection paired by convention or [InverseProperty] -
/// leaves them with the owning many-to-one on the far side, and the builder takes them
/// from there, in the order of its pairs. The foreign key columns are named unlike the
/// owner's key on purpose: that is what tells the counterpart's columns apart from the
/// fallback, which writes the owner's key columns, every part of them.
/// </summary>
public class InverseCollectionKeyTest
{
    private const string EFCoreOrders = """
        namespace Shop;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        [Table("Orders")]
        [PrimaryKey(nameof(CompanyId), nameof(OrderId))]
        public class Order
        {
            public required int CompanyId { get; set; }

            public required int OrderId { get; set; }

            public List<OrderLine> Lines { get; set; } = new();
        }

        [Table("OrderLines")]
        public class OrderLine
        {
            [Key]
            public required int OrderLineId { get; set; }

            public required int OrderCompanyId { get; set; }

            public required int OrderNo { get; set; }

            [ForeignKey("OrderCompanyId,OrderNo")]
            public Order Order { get; set; } = null!;
        }
        """;

    private const string JpaOrders = """
        package Shop;

        import jakarta.persistence.*;
        import java.io.Serializable;
        import java.util.ArrayList;
        import java.util.List;
        import java.util.Objects;

        @Entity
        @Table(name = "Orders")
        @IdClass(Order.OrderKey.class)
        public class Order {
            @Id
            @Column(name = "CompanyId")
            private Integer CompanyId;

            @Id
            @Column(name = "OrderId")
            private Integer OrderId;

            @OneToMany(mappedBy = "Order")
            private List<OrderLine> Lines = new ArrayList<>();

            public static class OrderKey implements Serializable {
                private Integer CompanyId;
                private Integer OrderId;

                public OrderKey() {
                }

                @Override
                public boolean equals(Object obj) {
                    return obj instanceof OrderKey other
                        && Objects.equals(CompanyId, other.CompanyId)
                        && Objects.equals(OrderId, other.OrderId);
                }

                @Override
                public int hashCode() {
                    return Objects.hash(CompanyId, OrderId);
                }
            }
        }

        @Entity
        @Table(name = "OrderLines")
        public class OrderLine {
            @Id
            @Column(name = "OrderLineId")
            private Integer OrderLineId;

            @ManyToOne(optional = false)
            @JoinColumns({
                @JoinColumn(name = "OrderCompanyId", referencedColumnName = "CompanyId"),
                @JoinColumn(name = "OrderNo", referencedColumnName = "OrderId")
            })
            private Order Order;
        }
        """;

    /// <summary>The two sources that state the collection as the inverse side without columns.</summary>
    public static TheoryData<ORMEnum> Sources() => new(ORMEnum.EFCore, ORMEnum.Hibernate);

    public static ConversionResult Convert(ORMEnum source)
        => ConversionHandler.Convert(source, ORMEnum.NHibernate,
        [
            source == ORMEnum.EFCore
                ? new ConversionSource { Content = EFCoreOrders, ContentType = ConversionContentType.CSharp }
                : new ConversionSource { Content = JpaOrders, ContentType = ConversionContentType.Java },
        ]);

    private static string Mapping(ConversionResult result, string entity)
        => Assert.Single(result.Sources, s =>
            s.ContentType == ConversionContentType.XML && s.Content.Contains($"<class name=\"{entity}\"")).Content;

    private static string Normalized(string text) => text.Replace("\r\n", "\n");

    /// <summary>
    /// The key of the bag is the many-to-one's columns, in the same order: the far side
    /// states the foreign key, so the collection restates it instead of the owner's key.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void TheKeyOfTheBagIsTheForeignKeyOfTheOwningSide(ORMEnum source)
    {
        var result = Convert(source);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        var order = Normalized(Mapping(result, "Order"));
        var line = Normalized(Mapping(result, "OrderLine"));

        Assert.Contains(Normalized("""
                    <bag name="Lines" inverse="true">
                        <key>
                            <column name="OrderCompanyId" />
                            <column name="OrderNo" />
                        </key>
            """), order);
        Assert.Contains(Normalized("""
                    <many-to-one name="Order" class="Order">
                        <column name="OrderCompanyId" />
                        <column name="OrderNo" />
                    </many-to-one>
            """), line);
    }

    /// <summary>
    /// The columns are a fact of the source, stated on the owning side, so nothing reports
    /// the fallback convention of decision 012.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void TheCounterpartsColumnsAreNoConvention(ORMEnum source)
    {
        var result = Convert(source);

        Assert.DoesNotContain(result.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("owner's key column"));
    }

    /// <summary>
    /// Where nobody states the columns - the child is outside the conversion - the fallback
    /// still writes the owner's key, and over a composite key that is every part of it in the
    /// key's order; a single column would be refused by NHibernate as a foreign key of the
    /// wrong width.
    /// </summary>
    [Fact]
    public void TheFallbackOverACompositeKeyWritesEveryPart()
    {
        const string orderAlone = """
            namespace Shop;

            using System.ComponentModel.DataAnnotations.Schema;
            using Microsoft.EntityFrameworkCore;

            [Table("Orders")]
            [PrimaryKey(nameof(CompanyId), nameof(OrderId))]
            public class Order
            {
                public required int CompanyId { get; set; }

                public required int OrderId { get; set; }

                public List<OrderLine> Lines { get; set; } = new();
            }
            """;

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate,
            [new ConversionSource { Content = orderAlone, ContentType = ConversionContentType.CSharp }]);

        var order = Normalized(Mapping(result, "Order"));

        Assert.Contains(Normalized("""
                        <key>
                            <column name="CompanyId" />
                            <column name="OrderId" />
                        </key>
            """), order);

        var convention = Assert.Single(result.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("owner's key column"));
        Assert.Equal("Lines", convention.Property);
        Assert.Contains("'CompanyId', 'OrderId'", convention.Reason);
    }
}
