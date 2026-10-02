using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;
using Tests.Catalog;

namespace Tests.Combined;

/// <summary>
/// The join column Jakarta Persistence derives for an owning reference that names none -
/// attribute_referencedColumn - read as a statement of the source (decision 067). Until
/// 2026-10-02 the JPA reading left the fact empty, so the relation had no column pairs
/// without a catalog: NHibernate named the column after the property and the collection
/// key after the owner's key, two different wrong columns, and a join along either side of
/// the association path was refused (decision 101). Both pinned implementations derive the
/// name for a single-column key - Hibernate customer_CustomerID, EclipseLink the same with
/// the attribute part in upper case - and part ways over a composite one, where the
/// specification gives no default; that case keeps the empty fact.
/// </summary>
public class JpaDefaultJoinColumnTest
{
    private const string Customer = """
        package Shop;

        import jakarta.persistence.*;
        import java.util.List;

        @Entity
        @Table(name = "Customers")
        public class Customer {
            @Id
            @Column(name = "CustomerID")
            private Integer id;

            private String name;

            @OneToMany(mappedBy = "customer")
            private List<Order> orders;
        }
        """;

    private const string Order = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Orders")
        public class Order {
            @Id
            private Integer id;

            @ManyToOne
            private Customer customer;
        }
        """;

    private static ConversionResult Convert(ORMEnum source, ORMEnum target, params ConversionSource[] units)
        => ConversionHandler.Convert(source, target, [.. units]);

    private static ConversionSource Java(string content) => new() { Content = content, ContentType = ConversionContentType.Java };

    private static ConversionSource Jpql(string content) => new() { Content = content, ContentType = ConversionContentType.JpqlQuery };

    private static string Mapping(ConversionResult result, string entity)
        => Assert.Single(result.Sources, s =>
            s.ContentType == ConversionContentType.XML && s.Content.Contains($"<class name=\"{entity}\"")).Content;

    private static string Sql(ConversionResult result)
    {
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        return Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery).Content;
    }

    /// <summary>
    /// The reference and the collection that mirrors it name one column, the one the
    /// source's framework creates; neither falls back to a convention of the target, so
    /// no record says one was used.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void TheDefaultColumnIsWhatTheSourceStates(ORMEnum source)
    {
        var result = Convert(source, ORMEnum.NHibernate, Java(Customer), Java(Order));

        Assert.Contains("<many-to-one name=\"customer\" class=\"Customer\" column=\"customer_CustomerID\" />", Mapping(result, "Order"));
        Assert.Contains("<key column=\"customer_CustomerID\" />", Mapping(result, "Customer"));
        Assert.DoesNotContain(result.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Category == MappingFactCategory.ForeignKeyColumns);
    }

    /// <summary>Both sides of the path join over the column, and neither is refused any more.</summary>
    [Theory]
    [InlineData("select o from Order o join o.customer c", "INNER JOIN Customers c ON o.customer_CustomerID = c.CustomerID")]
    [InlineData("select c from Customer c join c.orders o", "INNER JOIN Orders o ON o.customer_CustomerID = c.CustomerID")]
    public void BothSidesOfThePathJoinOverTheDefaultColumn(string query, string join)
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.Dapper, Java(Customer), Java(Order), Jpql(query));

        Assert.Contains(join, Sql(result));
    }

    /// <summary>
    /// A @JoinColumn that names no column - here it says only that the column is NOT NULL -
    /// leaves the name to the same default; so does an owning one-to-one.
    /// </summary>
    [Fact]
    public void AJoinColumnWithoutANameAndAnOwningOneToOneTakeTheDefaultToo()
    {
        const string order = """
            package Shop;

            import jakarta.persistence.*;

            @Entity
            @Table(name = "Orders")
            public class Order {
                @Id
                private Integer id;

                @ManyToOne
                @JoinColumn(nullable = false)
                private Customer billTo;

                @OneToOne
                private Customer contact;
            }
            """;

        var mapping = Mapping(Convert(ORMEnum.Hibernate, ORMEnum.NHibernate, Java(Customer), Java(order)), "Order");

        Assert.Contains("column=\"billTo_CustomerID\"", mapping);
        Assert.Contains("column=\"contact_CustomerID\"", mapping);
    }

    /// <summary>
    /// Over a composite key the specification gives no default and the implementations
    /// disagree, so nothing is derived: the reference leaves its columns to the target
    /// with the record it always had, and the path join is refused for want of them.
    /// </summary>
    [Fact]
    public void ACompositeKeyGetsNoDefault()
    {
        const string header = """
            package Shop;

            import jakarta.persistence.*;
            import java.io.Serializable;

            @Entity
            @Table(name = "Headers")
            @IdClass(Header.Key.class)
            public class Header {
                @Id
                private Integer companyId;

                @Id
                private Integer orderNo;

                public static class Key implements Serializable {
                    private Integer companyId;
                    private Integer orderNo;
                }
            }
            """;

        const string line = """
            package Shop;

            import jakarta.persistence.*;

            @Entity
            @Table(name = "Lines")
            public class Line {
                @Id
                private Integer id;

                @ManyToOne
                private Header header;
            }
            """;

        var mapping = Convert(ORMEnum.Hibernate, ORMEnum.NHibernate, Java(header), Java(line));
        Assert.Contains("<many-to-one name=\"header\" class=\"Header\" />", Mapping(mapping, "Line"));
        Assert.Contains(mapping.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Category == MappingFactCategory.ForeignKeyColumns && r.Property == "header");

        var query = Convert(ORMEnum.Hibernate, ORMEnum.Dapper, Java(header), Java(line), Jpql("select l from Line l join l.header h"));
        Assert.DoesNotContain(query.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
        Assert.Contains(query.Records, r =>
            r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("no foreign key columns"));
    }

    /// <summary>The referenced key is unknown when its entity is not in the conversion, so there is nothing to derive from.</summary>
    [Fact]
    public void AReferenceOutsideTheConversionGetsNoDefault()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.NHibernate, Java(Order));

        Assert.Contains("<many-to-one name=\"customer\" class=\"Customer\" />", Mapping(result, "Order"));
    }

    /// <summary>
    /// A column some source states outranks the derived one, wherever it is stated: here
    /// orm.xml, read first (decision 068), declares the reference without a column and the
    /// annotation names it.
    /// </summary>
    [Fact]
    public void AStatedColumnOutranksTheDefault()
    {
        const string ormXml = """
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <entity class="Shop.Order">
                    <attributes>
                        <many-to-one name="customer" target-entity="Shop.Customer"/>
                    </attributes>
                </entity>
            </entity-mappings>
            """;

        var named = Order.Replace("@ManyToOne", "@ManyToOne\n    @JoinColumn(name = \"CustomerRef\")");

        var result = Convert(ORMEnum.Hibernate, ORMEnum.NHibernate,
            new ConversionSource { Content = ormXml, ContentType = ConversionContentType.XML }, Java(Customer), Java(named));

        Assert.Contains("column=\"CustomerRef\"", Mapping(result, "Order"));
        Assert.DoesNotContain("customer_CustomerID", Mapping(result, "Order"));
    }

    /// <summary>
    /// A referencedColumnName naming another column than the key is a relation the model
    /// would pair with the key anyway; the default is not derived over it.
    /// </summary>
    [Fact]
    public void AReferencedColumnOtherThanTheKeyGetsNoDefault()
    {
        var elsewhere = Order.Replace("@ManyToOne", "@ManyToOne\n    @JoinColumn(referencedColumnName = \"Code\")");

        var result = Convert(ORMEnum.Hibernate, ORMEnum.NHibernate, Java(Customer), Java(elsewhere));

        Assert.Contains("<many-to-one name=\"customer\" class=\"Customer\" />", Mapping(result, "Order"));
    }

    private static TableImage CustomersImage() => new()
    {
        Schema = "dbo",
        Name = "Customers",
        Columns =
        [
            new ColumnImage { Name = "CustomerID", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "name", Type = DatabaseType.VarChar, Length = 100, IsNullable = true, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["CustomerID"],
        ForeignKeys = [],
    };

    private static TableImage OrdersImage(string foreignKeyColumn) => new()
    {
        Schema = "dbo",
        Name = "Orders",
        Columns =
        [
            new ColumnImage { Name = "id", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = foreignKeyColumn, Type = DatabaseType.Integer, IsNullable = true, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["id"],
        ForeignKeys =
        [
            new ForeignKeyImage
            {
                Name = "FK_Orders_Customers",
                ReferencedSchema = "dbo",
                ReferencedTable = "Customers",
                Columns = [new ForeignKeyColumn(foreignKeyColumn, "CustomerID")],
            },
        ],
    };

    /// <summary>
    /// The derived column is the source's statement, so the catalog compares it instead of
    /// supplying its own (decisions 015 and 067): a schema that names the column otherwise
    /// is a conflict, and the source's column stays.
    /// </summary>
    [Fact]
    public void TheCatalogComparesTheDefaultRatherThanReplacingIt()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.NHibernate, [Java(Customer), Java(Order)],
            new FakeCatalogReader(CustomersImage(), OrdersImage("CustomerID")));

        Assert.Contains("column=\"customer_CustomerID\"", Mapping(result, "Order"));
        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.ForeignKeyColumns && r.Property == "customer");
        Assert.DoesNotContain(result.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.ForeignKeyColumns && r.Property == "customer");
    }

    /// <summary>A schema made by the source's framework agrees with it - in EclipseLink's upper case too.</summary>
    [Theory]
    [InlineData("customer_CustomerID")]
    [InlineData("CUSTOMER_CustomerID")]
    public void TheCatalogOfTheSourcesOwnSchemaAgrees(string column)
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.NHibernate, [Java(Customer), Java(Order)],
            new FakeCatalogReader(CustomersImage(), OrdersImage(column)));

        Assert.DoesNotContain(result.Records, r =>
            r.Category == MappingFactCategory.ForeignKeyColumns && r.Kind == ConversionRecordKind.Conflict);
        Assert.DoesNotContain(result.Records, r =>
            r.Category == MappingFactCategory.ForeignKeyColumns && r.Kind == ConversionRecordKind.Supplied && r.Property == "customer");
    }
}
