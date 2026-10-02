using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;
using Tests.Catalog;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A foreign key column no scalar property maps - the usual shape in Jakarta Persistence,
/// where the join column belongs to the reference alone - in the three languages over
/// entities. HQL, JPQL and LINQ name properties, and the column used to be written as if it
/// were one (<c>o.customer_CustomerID</c>), which every one of the four targets refused.
/// The condition of a join along the association is now written as the reference compared
/// with the row (<c>p.customer = c</c>), any other place reaches the key through the
/// reference (<c>p.customer.CustomerID</c>) where the target reads that off the foreign key,
/// EF Core names the foreign key property its entity builder declares, and a target that
/// would reach the key with a join - EclipseLink, NHibernate over a composite key - writes
/// the query in native SQL (decision 113). What each target writes was measured on the
/// pinned releases, the Java ones at the fourth level (JavaTests, ReferenceKeyClaimsTest).
/// </summary>
public class ForeignKeyColumnWithoutPropertyTest
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
            private List<Purchase> purchases;
        }
        """;

    private const string Purchase = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Purchases")
        public class Purchase {
            @Id
            private Integer id;

            @ManyToOne
            private Customer customer;
        }
        """;

    private const string Composite = """
        package Shop;

        import jakarta.persistence.*;
        import java.io.Serializable;
        import java.util.ArrayList;
        import java.util.List;
        import java.util.Objects;

        @Entity
        @Table(name = "Headers")
        @IdClass(Header.HeaderKey.class)
        public class Header {
            @Id
            @Column(name = "CompanyId")
            private Integer CompanyId;

            @Id
            @Column(name = "OrderId")
            private Integer OrderId;

            @OneToMany(mappedBy = "Head")
            private List<Line> Lines = new ArrayList<>();

            public static class HeaderKey implements Serializable {
                private Integer CompanyId;
                private Integer OrderId;

                public HeaderKey() {
                }

                @Override
                public boolean equals(Object obj) {
                    return obj instanceof HeaderKey other
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
        @Table(name = "Lines")
        public class Line {
            @Id
            @Column(name = "LineId")
            private Integer LineId;

            @ManyToOne
            @JoinColumns({
                @JoinColumn(name = "HeadCompanyId", referencedColumnName = "CompanyId"),
                @JoinColumn(name = "HeadNo", referencedColumnName = "OrderId")
            })
            private Header Head;
        }
        """;

    private static ConversionSource Java(string content) => new() { Content = content, ContentType = ConversionContentType.Java };

    private static ConversionSource Jpql(string content) => new() { Content = content, ContentType = ConversionContentType.JpqlQuery };

    private static ConversionResult FromJpa(ORMEnum target, string query)
        => ConversionHandler.Convert(ORMEnum.Hibernate, target, [Java(Customer), Java(Purchase), Jpql(query)]);

    private static ConversionResult FromComposite(ORMEnum target, string query)
        => ConversionHandler.Convert(ORMEnum.Hibernate, target, [Java(Composite), Jpql(query)]);

    private static string Text(ConversionResult result, ConversionContentType type)
    {
        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
        return Assert.Single(result.Sources, s => s.ContentType == type).Content;
    }

    private static int Occurrences(string text, string fragment)
        => (text.Length - text.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;

    /* ---- a join along the association: the reference compared with the row ------------ */

    [Theory]
    [InlineData(ORMEnum.NHibernate, "select p from Purchase p join p.customer c", "inner join Customer c with p.customer = c")]
    [InlineData(ORMEnum.NHibernate, "select p from Purchase p left join p.customer c", "left join Customer c with p.customer = c")]
    [InlineData(ORMEnum.NHibernate, "select c from Customer c join c.purchases p", "inner join Purchase p with p.customer = c")]
    [InlineData(ORMEnum.Hibernate, "select p from Purchase p left join p.customer c", "left join Customer c on p.customer = c")]
    [InlineData(ORMEnum.Hibernate, "select c from Customer c left join c.purchases p", "left join Purchase p on p.customer = c")]
    [InlineData(ORMEnum.EclipseLink, "select p from Purchase p join p.customer c", "join Customer c on p.customer = c")]
    [InlineData(ORMEnum.EclipseLink, "select c from Customer c left join c.purchases p", "left join Purchase p on p.customer = c")]
    public void AJoinAlongTheAssociationComparesTheReferenceWithTheRow(ORMEnum target, string query, string join)
    {
        var language = target == ORMEnum.NHibernate ? ConversionContentType.HqlQuery : ConversionContentType.JpqlQuery;
        var text = Text(FromJpa(target, query), language);

        Assert.Contains(join, text);
        Assert.DoesNotContain("customer_CustomerID", text);
    }

    /// <summary>
    /// Level 3, and the SQL behind it: NHibernate 5.7.0 compiles the join and reads the
    /// reference off the foreign key column, so the referenced table is joined once - the
    /// join of the source, not one more. The direction from Purchase is the reproduction of
    /// the defect, which NHibernate refused with "could not resolve property".
    /// </summary>
    [Theory]
    [InlineData("select p from Purchase p join p.customer c", "inner join Customers")]
    [InlineData("select p from Purchase p left join p.customer c", "left outer join Customers")]
    [InlineData("select c from Customer c join c.purchases p", "inner join Purchases")]
    [InlineData("select c from Customer c left join c.purchases p", "left outer join Purchases")]
    public void NHibernateJoinsOverTheForeignKeyColumn(string query, string join)
    {
        var result = FromJpa(ORMEnum.NHibernate, query);
        var sql = NHibernateSql(result, Text(result, ConversionContentType.HqlQuery), "ForeignKeyColumn_NHibernate");

        Assert.Contains(join, sql);
        Assert.Contains("customer_CustomerID=", sql.Replace(" ", string.Empty));
        Assert.Equal(1, Occurrences(sql, "Customers "));
        Assert.Equal(1, Occurrences(sql, "Purchases "));
    }

    /// <summary>
    /// Level 3 for EF Core: the key selector is the foreign key property the entity builder
    /// declares for the column, which EF Core reads without a join; the navigation's key,
    /// <c>p.customer.id</c>, would have cost a join of Customers (measured).
    /// </summary>
    [Theory]
    [InlineData("select p from Purchase p join p.customer c", "INNER JOIN [Customers] AS [c] ON [p].[customer_CustomerID] = [c].[CustomerID]")]
    [InlineData("select p from Purchase p left join p.customer c", "LEFT JOIN [Customers] AS [c] ON [p].[customer_CustomerID] = [c].[CustomerID]")]
    [InlineData("select c from Customer c left join c.purchases p", "LEFT JOIN [Purchases] AS [p] ON [c].[CustomerID] = [p].[customer_CustomerID]")]
    public void EFCoreJoinsOverTheDeclaredForeignKeyProperty(string query, string join)
    {
        var result = FromJpa(ORMEnum.EFCore, query);
        var method = Text(result, ConversionContentType.CSharpQuery);
        Assert.Contains("p => p.customerid", method);

        var sql = EFCoreSql(result, method, "ForeignKeyColumn_EFCore");

        Assert.Contains(join, sql);
        Assert.Equal(1, Occurrences(sql, "JOIN"));
    }

    /// <summary>The defect as it was found: JPA entities without @JoinColumn, the path join into NHibernate.</summary>
    [Fact]
    public void TheReproductionCompilesInNHibernate()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.Hibernate,
            ORMEnum.NHibernate,
            [Java(Customer.Replace("Purchase", "Order").Replace("purchases", "orders")), Java(Purchase.Replace("Purchases", "Orders").Replace("class Purchase", "class Order")),
             Jpql("select o from Order o join o.customer c")]);

        var hql = Text(result, ConversionContentType.HqlQuery);
        Assert.Contains("with o.customer = c", hql);

        NHibernateSql(result, hql, "ForeignKeyColumn_Reproduction");
    }

    /// <summary>
    /// A composite key: every pair of the reference makes one comparison of the reference with
    /// the row, which NHibernate reads off both foreign key columns - a part of the key
    /// reached through the reference would have cost it a join of Headers (measured) - and
    /// EF Core joins over its two declared properties, cast where the foreign key is nullable
    /// and the key it points at is not, so that the two anonymous keys are one type.
    /// </summary>
    [Fact]
    public void ACompositeKeyIsComparedAsAWhole()
    {
        var nhibernate = FromComposite(ORMEnum.NHibernate, "select l from Line l left join l.Head h");
        var hql = Text(nhibernate, ConversionContentType.HqlQuery);
        Assert.Contains("left join Header h with l.Head = h", hql);

        var sql = NHibernateSql(nhibernate, hql, "ForeignKeyColumn_Composite_NHibernate");
        Assert.Equal(1, Occurrences(sql, "Headers "));

        Assert.Contains("join Header h on l.Head = h", Text(FromComposite(ORMEnum.EclipseLink, "select l from Line l join l.Head h"), ConversionContentType.JpqlQuery));

        var efCore = FromComposite(ORMEnum.EFCore, "select l from Line l join l.Head h");
        var translated = EFCoreSql(efCore, Text(efCore, ConversionContentType.CSharpQuery), "ForeignKeyColumn_Composite_EFCore");
        Assert.Contains("ON [l].[HeadCompanyId] = [h].[CompanyId] AND [l].[HeadNo] = [h].[OrderId]", translated);
        Assert.Equal(1, Occurrences(translated, "JOIN"));
    }

    /// <summary>
    /// The cast that makes the two anonymous keys one type needs the joined class's members as
    /// it declares them, also where the source states no table and the class is found only by
    /// the naming convention (decision 050): a Dapper source without a catalog joining over a
    /// nullable member compiled to two anonymous types C# could not unify (CS0411).
    /// </summary>
    [Fact]
    public void ACompositeKeyOfAJoinedClassFoundByNameIsCastToOneType()
    {
        const string entities = """
            namespace Shop;

            public class Order
            {
                public int OrderID { get; set; }
                public int CustomerID { get; set; }
                public int Qty { get; set; }
            }

            public class Customer
            {
                public int CustomerID { get; set; }
                public int? Rank { get; set; }
            }
            """;

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore,
        [
            new() { Content = entities, ContentType = ConversionContentType.CSharp },
            new()
            {
                Content = "SELECT o.OrderID, o.Qty FROM Orders o JOIN Customers c ON o.CustomerID = c.CustomerID AND o.Qty = c.Rank",
                ContentType = ConversionContentType.SqlQuery,
            },
        ]);

        var method = Text(result, ConversionContentType.CSharpQuery);
        Assert.Contains("(int?)", method);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "ForeignKeyColumn_CompositeByName_EFCore",
            method,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Shop;");
        Assert.NotEmpty(compiled);
    }

    /// <summary>
    /// A JPQL source that compares the reference with the row itself - <c>on p.customer = c</c>,
    /// <c>where p.customer = c</c> - says the same as the join along the association, and is
    /// read as the equality of the foreign key column with the key, derived from the relation
    /// (decision 101). Read as a column compared with the entity, it came out as
    /// <c>c.*</c> in every target - in SQL as <c>p.customer = *</c>.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate, "select p from Purchase p join Customer c on p.customer = c", "join Customer c on p.customer = c")]
    [InlineData(ORMEnum.NHibernate, "select p from Purchase p join Customer c on p.customer = c", "inner join Customer c with p.customer = c")]
    [InlineData(ORMEnum.Dapper, "select p from Purchase p join Customer c on p.customer = c", "ON p.customer_CustomerID = c.CustomerID")]
    [InlineData(ORMEnum.Dapper, "select p from Purchase p join p.customer c where p.customer <> c", "WHERE NOT (p.customer_CustomerID = c.CustomerID)")]
    public void AReferenceComparedWithTheRowIsTheEqualityOfItsKey(ORMEnum target, string query, string expected)
    {
        var result = FromJpa(target, query);
        var written = Assert.Single(result.Sources, s => s.ContentType is ConversionContentType.JpqlQuery or ConversionContentType.HqlQuery or ConversionContentType.SqlQuery).Content;

        Assert.Contains(expected, written);
        Assert.DoesNotContain("*", written.Replace("SELECT *", string.Empty, StringComparison.Ordinal));
    }

    /// <summary>An entity compared with anything else - a parameter here - has no operand in the representation, and the condition is refused, not written as a column.</summary>
    [Fact]
    public void AnEntityComparedWithAValueIsNotRead()
    {
        var result = FromJpa(ORMEnum.Dapper, "select p from Purchase p where p.customer = :customer");

        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("whole entity", StringComparison.Ordinal));
    }

    /// <summary>
    /// The target of a relation may be written qualified - hbm.xml keeps
    /// <c>class="Shop.Customer, Shop"</c> - and names the class all the same: the equality of
    /// the foreign key column with the key it references is still the reference compared
    /// with the row.
    /// </summary>
    [Fact]
    public void AQualifiedTargetOfTheReferenceIsTheSameClass()
    {
        var customerId = new Model.AbstractRepresentation.Property { Name = "CustomerID", Type = Model.AbstractRepresentation.LangType.Scalar(ScalarType.Int) };
        var customerKey = new Model.AbstractRepresentation.PropertyMap { Property = customerId, ColumnName = "CustomerID" };
        var customer = new Model.AbstractRepresentation.EntityMap
        {
            Entity = new Model.AbstractRepresentation.Entity { Name = "Customer", Properties = [customerId] },
            Table = "Customers",
            PropertyMaps = [customerKey],
        };

        var purchaseId = new Model.AbstractRepresentation.Property { Name = "id", Type = Model.AbstractRepresentation.LangType.Scalar(ScalarType.Int) };
        var foreignKey = new Model.AbstractRepresentation.PropertyMap
        {
            Property = new Model.AbstractRepresentation.Property { Name = "customer_CustomerID", Type = Model.AbstractRepresentation.LangType.Scalar(ScalarType.Int) },
            ColumnName = "customer_CustomerID",
        };
        var purchase = new Model.AbstractRepresentation.EntityMap
        {
            Entity = new Model.AbstractRepresentation.Entity { Name = "Purchase", Properties = [purchaseId] },
            Table = "Purchases",
            PropertyMaps = [new Model.AbstractRepresentation.PropertyMap { Property = purchaseId, ColumnName = "id" }],
            Relations =
            [
                new Model.AbstractRepresentation.Relation
                {
                    Cardinality = Cardinality.ManyToOne,
                    Role = RelationRole.Owning,
                    SourceEntity = "Purchase",
                    TargetEntity = "Shop.Customer, Shop",
                    SourceNavigationProperty = "customer",
                    ColumnPairs = [new Model.AbstractRepresentation.ColumnPair { Source = foreignKey, Target = customerKey }],
                },
            ],
        };

        var condition = new Model.QueryInstructions.Conditions.ComparisonCondition(
            Model.QueryInstructions.Conditions.QueryOperand.Column("p", "customer_CustomerID"),
            Model.QueryInstructions.Conditions.ComparisonOperator.Equal,
            Model.QueryInstructions.Conditions.QueryOperand.Column("c", "CustomerID"));

        var comparison = Assert.Single(AbstractWrappers.ColumnMember.ReferenceComparisons(
            condition,
            new Dictionary<string, Model.AbstractRepresentation.EntityMap>(StringComparer.OrdinalIgnoreCase) { ["p"] = purchase, ["c"] = customer }));
        Assert.Equal(("p", "customer", "c"), (comparison.Alias, comparison.Navigation, comparison.Target));
    }

    /* ---- the column outside a join along the association ----------------------------- */

    // A Dapper source states no relation columns; the catalog supplies them, and the column
    // stands in the source's SQL as a column of its own - where a query over entities has no
    // property for it.
    private const string DapperEntities = """
        namespace Shop;

        public class Customer
        {
            public int CustomerID { get; set; }
            public string name { get; set; }
        }

        public class Purchase
        {
            public int id { get; set; }
            public Customer customer { get; set; }
        }

        public class Header
        {
            public int CompanyId { get; set; }
            public int OrderId { get; set; }
        }

        public class Line
        {
            public int LineId { get; set; }
            public Header Head { get; set; }
        }
        """;

    private static ColumnImage Integer(string name, bool nullable = false)
        => new() { Name = name, Type = DatabaseType.Integer, IsNullable = nullable, IsIdentity = false };

    private static FakeCatalogReader Catalog() => new(
        new TableImage
        {
            Schema = "dbo", Name = "Customers", PrimaryKeyColumns = ["CustomerID"], ForeignKeys = [],
            Columns = [Integer("CustomerID"), new ColumnImage { Name = "name", Type = DatabaseType.VarChar, Length = 50, IsNullable = true, IsIdentity = false }],
        },
        new TableImage
        {
            Schema = "dbo", Name = "Purchases", PrimaryKeyColumns = ["id"],
            Columns = [Integer("id"), Integer("customer_CustomerID", nullable: true)],
            ForeignKeys = [new ForeignKeyImage { Name = "FK_Purchases_Customers", ReferencedSchema = "dbo", ReferencedTable = "Customers", Columns = [new ForeignKeyColumn("customer_CustomerID", "CustomerID")] }],
        },
        new TableImage
        {
            Schema = "dbo", Name = "Headers", PrimaryKeyColumns = ["CompanyId", "OrderId"], ForeignKeys = [],
            Columns = [Integer("CompanyId"), Integer("OrderId")],
        },
        new TableImage
        {
            Schema = "dbo", Name = "Lines", PrimaryKeyColumns = ["LineId"],
            Columns = [Integer("LineId"), Integer("HeadCompanyId", nullable: true), Integer("HeadNo", nullable: true)],
            ForeignKeys = [new ForeignKeyImage { Name = "FK_Lines_Headers", ReferencedSchema = "dbo", ReferencedTable = "Headers", Columns = [new ForeignKeyColumn("HeadCompanyId", "CompanyId"), new ForeignKeyColumn("HeadNo", "OrderId")] }],
        });

    private static ConversionResult FromDapper(ORMEnum target, string sql)
        => ConversionHandler.Convert(ORMEnum.Dapper, target,
            [new() { Content = DapperEntities, ContentType = ConversionContentType.CSharp }, new() { Content = sql, ContentType = ConversionContentType.SqlQuery }],
            Catalog());

    /// <summary>
    /// NHibernate reads a single-column key reached through the reference off the foreign
    /// key column itself, a filter for a NULL included, so the query needs no join (measured
    /// in the SQL of its plan).
    /// </summary>
    [Fact]
    public void NHibernateReachesASingleColumnKeyThroughTheReference()
    {
        var result = FromDapper(ORMEnum.NHibernate, "SELECT p.id FROM Purchases p WHERE p.customer_CustomerID IS NULL");
        var hql = Text(result, ConversionContentType.HqlQuery);
        Assert.Contains("where p.customer.CustomerID is null", hql);

        var sql = NHibernateSql(result, hql, "ForeignKeyColumn_Filter_NHibernate");
        Assert.Contains("customer_CustomerID is null", sql);
        Assert.DoesNotContain("join", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Hibernate 7.4.5 reads the path off the foreign key column (measured at the fourth level).</summary>
    [Fact]
    public void HibernateReachesTheKeyThroughTheReference()
        => Assert.Contains("where p.customer.CustomerID is null",
            Text(FromDapper(ORMEnum.Hibernate, "SELECT p.id FROM Purchases p WHERE p.customer_CustomerID IS NULL"), ConversionContentType.JpqlQuery));

    /// <summary>EF Core filters on the foreign key property the entity declares, without a join.</summary>
    [Fact]
    public void EFCoreFiltersOnTheDeclaredForeignKeyProperty()
    {
        var result = FromDapper(ORMEnum.EFCore, "SELECT p.id FROM Purchases p WHERE p.customer_CustomerID IS NULL");
        var method = Text(result, ConversionContentType.CSharpQuery);
        Assert.Contains("p.customerCustomerID == null", method);

        var sql = EFCoreSql(result, method, "ForeignKeyColumn_Filter_EFCore");
        Assert.Contains("WHERE [p].[customer_CustomerID] IS NULL", sql);
        Assert.DoesNotContain("JOIN", sql);
    }

    /// <summary>
    /// Where the path would cost a join - in EclipseLink always, in NHibernate for a part of a
    /// composite key - the rows whose foreign key is NULL would drop out of a filter or a
    /// projection, a different query (decision 053); the target writes it in native SQL
    /// instead (decision 113) and says why.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EclipseLink, "SELECT p.id FROM Purchases p WHERE p.customer_CustomerID IS NULL")]
    [InlineData(ORMEnum.EclipseLink, "SELECT p.id, p.customer_CustomerID FROM Purchases p")]
    [InlineData(ORMEnum.NHibernate, "SELECT l.LineId FROM Lines l WHERE l.HeadCompanyId IS NULL")]
    [InlineData(ORMEnum.NHibernate, "SELECT l.LineId, l.HeadNo FROM Lines l")]
    public void WhereThePathCostsAJoinTheQueryGoesOutInNativeSql(ORMEnum target, string sql)
    {
        var result = FromDapper(target, sql);

        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Join && r.Reason.Contains("inner join of the referenced table"));
        Assert.DoesNotContain(result.Sources, s => s.ContentType is ConversionContentType.HqlQuery or ConversionContentType.JpqlQuery);
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }

    /// <summary>A join of the source's SQL along the foreign key is the reference compared with the row as well, in EclipseLink too.</summary>
    [Fact]
    public void AJoinOfTheSourceAlongTheForeignKeyStaysJpqlInEclipseLink()
        => Assert.Contains("left join Customer c on p.customer = c",
            Text(FromDapper(ORMEnum.EclipseLink, "SELECT p.id, c.name FROM Purchases p LEFT JOIN Customers c ON p.customer_CustomerID = c.CustomerID"),
                ConversionContentType.JpqlQuery));

    /* ---- the verification levels ------------------------------------------------------ */

    private static string NHibernateSql(ConversionResult result, string hql, string assembly)
    {
        var compiled = GeneratedEntityCompiler.CompileOrFail(
            assembly,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        return NHibernateQueryAcceptance.Sql(
            compiled,
            result.Sources.Where(s => s.ContentType == ConversionContentType.XML).Select(s => s.Content),
            hql);
    }

    private static string EFCoreSql(ConversionResult result, string method, string assembly)
    {
        var compiled = GeneratedQueryCompiler.CompileOrFail(
            assembly,
            method,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Shop;");

        return EFCoreQueryAcceptance.Translate(compiled);
    }
}
