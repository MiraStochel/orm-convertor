using System.Text.RegularExpressions;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using EFCoreWrappers;
using HibernateWrappers;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernate.Mapping;
using NHibernateWrappers;
using OrmConvertor;
using Tests.Catalog;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A unique constraint over a foreign key column the class maps by its navigation alone
/// (decision 130): the navigation is the member and stands for the whole foreign key. The
/// three sources that state one - JPA, NHibernate's unique-key and the catalog - read it,
/// and the three annotation targets write it each their own way: EF Core over the foreign
/// key properties, JPA over the join columns, NHibernate as unique-key on the many-to-one.
/// The shapes are asserted here, the frameworks' own verdict at the second and third levels
/// of decision 016 below.
/// </summary>
public class UniqueOverForeignKeyTest
{
    private const string Customer = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Customers")
        public class Customer {
            @Id
            @Column(name = "CustomerID")
            private Integer id;
        }
        """;

    /// <summary>The most common multi-column constraint: unique within the parent - and so mixed.</summary>
    private const string Purchase = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Purchases", uniqueConstraints = @UniqueConstraint(name = "UQ_Purchases_Customer_Number", columnNames = { "CustomerId", "Number" }))
        public class Purchase {
            @Id
            private Integer id;

            @Column(name = "Number")
            private String number;

            @ManyToOne
            @JoinColumn(name = "CustomerId")
            private Customer customer;
        }
        """;

    /// <summary>A @ManyToOne whose join column is unique: still N:1, the database guards the rest.</summary>
    private const string Address = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Addresses")
        public class Address {
            @Id
            private Integer id;

            @ManyToOne
            @JoinColumn(name = "CustomerId", unique = true)
            private Customer customer;
        }
        """;

    private static ConversionSource Java(string content) => new() { Content = content, ContentType = ConversionContentType.Java };

    private static ConversionResult FromHibernate(ORMEnum target)
        => ConversionHandler.Convert(ORMEnum.Hibernate, target, [Java(Customer), Java(Purchase), Java(Address)]);

    /// <summary>The entity's mapping document where the target writes one, else its class.</summary>
    private static string Artifact(ConversionResult result, string entity)
    {
        Assert.Empty(result.Records.Where(r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback).Select(r => r.Reason));
        return (result.Sources.SingleOrDefault(s => s.Content.Contains($"<class name=\"{entity}\"", StringComparison.Ordinal))
            ?? Assert.Single(result.Sources, s => s.Content.Contains($"class {entity}", StringComparison.Ordinal))).Content;
    }

    private static string ForeignKeyProperty(string code)
        => Regex.Match(code, "\\[ForeignKey\\(\"(?<name>[^\"]+)\"\\)\\]").Groups["name"].Value;

    private static IEnumerable<ConversionRecord> UniqueRecords(ConversionResult result, ConversionRecordKind kind)
        => result.Records.Where(r => r.Kind == kind && r.Category == MappingFactCategory.UniqueConstraint);

    [Fact]
    public void JpaReadsTheJoinColumnAsTheNavigation()
    {
        var builder = new EFCoreEntityBuilder();
        var context = new JpaReadingContext();

        foreach (var unit in new[] { Customer, Purchase, Address })
        {
            new HibernateEntityParser(builder, context).Parse(unit);
        }

        var purchase = builder.EntityMaps.Single(em => em.Entity.Name == "Purchase");
        var mixed = Assert.Single(purchase.UniqueConstraints);
        Assert.Equal(["customer", "number"], mixed.PropertyNames);
        Assert.Equal("UQ_Purchases_Customer_Number", mixed.Name);

        var address = builder.EntityMaps.Single(em => em.Entity.Name == "Address");
        Assert.Equal(["customer"], Assert.Single(address.UniqueConstraints).PropertyNames);
        Assert.Equal(Cardinality.ManyToOne, address.Relations.Single().Cardinality);

        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.UniqueConstraint);
    }

    [Fact]
    public void EFCoreIndexesTheForeignKeyPropertyItDeclares()
    {
        var result = FromHibernate(ORMEnum.EFCore);

        var purchase = Artifact(result, "Purchase");
        var key = ForeignKeyProperty(purchase);
        Assert.Contains($"[Index(nameof({key}), nameof(number), IsUnique = true, Name = \"UQ_Purchases_Customer_Number\")]", purchase, StringComparison.Ordinal);

        var address = Artifact(result, "Address");
        Assert.Contains($"[Index(nameof({ForeignKeyProperty(address)}), IsUnique = true)]", address, StringComparison.Ordinal);

        Assert.Empty(UniqueRecords(result, ConversionRecordKind.Incompleteness));
        Assert.Empty(UniqueRecords(result, ConversionRecordKind.Loss));
    }

    [Fact]
    public void JpaWritesTheJoinColumnsIntoTheConstraint()
    {
        var result = FromHibernate(ORMEnum.EclipseLink);

        Assert.Contains("@UniqueConstraint(name = \"UQ_Purchases_Customer_Number\", columnNames = { \"CustomerId\", \"Number\" })",
            Artifact(result, "Purchase"), StringComparison.Ordinal);
        Assert.Contains("@UniqueConstraint(columnNames = { \"CustomerId\" })", Artifact(result, "Address"), StringComparison.Ordinal);
    }

    /// <summary>
    /// unique="true" on a many-to-one is the owning side of a 1:1 (decision 012), so a
    /// constraint over the foreign key goes to unique-key even when it is the only member,
    /// with a derived token where it has no name - and the association stays N:1.
    /// </summary>
    [Fact]
    public void NHibernateWritesUniqueKeyOnTheManyToOneNeverUnique()
    {
        var result = FromHibernate(ORMEnum.NHibernate);

        var purchase = Artifact(result, "Purchase");
        Assert.Matches("<many-to-one name=\"customer\"[^>]*unique-key=\"UQ_Purchases_Customer_Number\"", purchase);
        Assert.Matches("<property name=\"number\"[^>]*unique-key=\"UQ_Purchases_Customer_Number\"", purchase);

        var address = Artifact(result, "Address");
        Assert.Matches("<many-to-one name=\"customer\"[^>]*unique-key=\"UQ_Address_customer\"", address);
        Assert.DoesNotContain("unique=\"true\"", address, StringComparison.Ordinal);

        Assert.Single(UniqueRecords(result, ConversionRecordKind.Convention), r => r.Entity == "Address");
        Assert.Empty(UniqueRecords(result, ConversionRecordKind.Loss));
    }

    [Fact]
    public void DapperReportsTheConstraintAsALoss()
    {
        var result = FromHibernate(ORMEnum.Dapper);

        Assert.Contains(UniqueRecords(result, ConversionRecordKind.Loss), r => r.Entity == "Purchase");
        Assert.Contains(UniqueRecords(result, ConversionRecordKind.Loss), r => r.Entity == "Address" && r.Property == "customer");
    }

    /// <summary>
    /// What NHibernate wrote is what its own parser reads: unique-key on the many-to-one and
    /// on the property are one group in the order of the document, and the many-to-one
    /// keeps its N:1.
    /// </summary>
    [Fact]
    public void NHibernateReadsUniqueKeyOnTheManyToOneAsTheNavigation()
    {
        const string mapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
              <class name="Purchase" table="Purchases">
                <id name="Id"><generator class="assigned" /></id>
                <many-to-one name="Customer" class="Customer" column="CustomerId" unique-key="UQ_Purchases_Customer_Number" />
                <property name="Number" unique-key="UQ_Purchases_Customer_Number" />
              </class>
            </hibernate-mapping>
            """;

        var builder = new EFCoreEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse(mapping);

        var map = builder.EntityMaps.Single();
        var constraint = Assert.Single(map.UniqueConstraints);
        Assert.Equal(["Customer", "Number"], constraint.PropertyNames);
        Assert.Equal("UQ_Purchases_Customer_Number", constraint.Name);
        Assert.Equal(Cardinality.ManyToOne, map.Relations.Single().Cardinality);
    }

    [Fact]
    public void AUniqueKeyOnSomeColumnsOfACompositeForeignKeyIsAReportedIncompleteness()
    {
        const string mapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
              <class name="Line" table="Lines">
                <id name="Id"><generator class="assigned" /></id>
                <many-to-one name="Head" class="Header">
                  <column name="HeadCompanyId" unique-key="UQ_Lines_Company" />
                  <column name="HeadNo" />
                </many-to-one>
              </class>
            </hibernate-mapping>
            """;

        var builder = new EFCoreEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse(mapping);

        Assert.Empty(builder.EntityMaps.Single().UniqueConstraints);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness
            && r.Category == MappingFactCategory.UniqueConstraint
            && r.Property == "Head");
    }

    /// <summary>
    /// A navigation stands for all the columns of its foreign key, so a constraint over part
    /// of a composite one cannot be named, and a join column of a one-to-many lies in the
    /// child's table - both are reported rather than read wrong (decision 130).
    /// </summary>
    [Fact]
    public void JpaReportsWhatTheNavigationCannotName()
    {
        const string header = """
            package Shop;

            import jakarta.persistence.*;
            import java.util.List;

            @Entity
            public class Header {
                @Id private Integer id;

                @ManyToOne
                @JoinColumns({
                    @JoinColumn(name = "OwnerCompanyId", referencedColumnName = "CompanyId", unique = true),
                    @JoinColumn(name = "OwnerNo", referencedColumnName = "No")
                })
                private Owner owner;

                @OneToMany
                @JoinColumn(name = "HeaderId", unique = true)
                private List<Line> lines;
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new HibernateEntityParser(builder, new JpaReadingContext()).Parse(header);

        Assert.Empty(builder.EntityMaps.Single().UniqueConstraints);

        var records = builder.Records
            .Where(r => r.Kind == ConversionRecordKind.Incompleteness && r.Category == MappingFactCategory.UniqueConstraint)
            .ToList();
        Assert.Contains(records, r => r.Property == "owner" && r.Reason.Contains("composite foreign key", StringComparison.Ordinal));
        Assert.Contains(records, r => r.Property == "lines" && r.Reason.Contains("another table", StringComparison.Ordinal));
    }

    /// <summary>
    /// The catalog states the constraint by column; the column is the foreign key of a
    /// navigation the class maps it by alone, so the navigation is supplied as the member -
    /// after the foreign keys, which is when the navigation's columns are known.
    /// </summary>
    [Fact]
    public void TheCatalogSuppliesTheNavigationForAForeignKeyColumn()
    {
        const string entities = """
            public class Customer
            {
                public int CustomerID { get; set; }
            }

            public class Address
            {
                public int AddressID { get; set; }
                public Customer Customer { get; set; }
            }
            """;

        static ColumnImage Integer(string name) => new() { Name = name, Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false };

        var catalog = new FakeCatalogReader(
            new TableImage
            {
                Schema = "dbo", Name = "Customer", PrimaryKeyColumns = ["CustomerID"], ForeignKeys = [],
                Columns = [Integer("CustomerID")],
            },
            new TableImage
            {
                Schema = "dbo", Name = "Address", PrimaryKeyColumns = ["AddressID"],
                Columns = [Integer("AddressID"), Integer("CustomerRef")],
                ForeignKeys = [new ForeignKeyImage { Name = "FK_Address_Customer", ReferencedSchema = "dbo", ReferencedTable = "Customer", Columns = [new ForeignKeyColumn("CustomerRef", "CustomerID")] }],
                UniqueConstraints = [new UniqueConstraintImage { Name = "UQ_Address_CustomerRef", Columns = ["CustomerRef"] }],
            });

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.NHibernate,
            [new() { Content = entities, ContentType = ConversionContentType.CSharp }], catalog);

        Assert.Matches("<many-to-one name=\"Customer\"[^>]*column=\"CustomerRef\"[^>]*unique-key=\"UQ_Address_CustomerRef\"", Artifact(result, "Address"));
        Assert.Contains(UniqueRecords(result, ConversionRecordKind.Supplied), r => r.Entity == "Address");
    }

    /// <summary>
    /// Second level of decision 016: EF Core builds a model in which the constraint is a
    /// unique index over the foreign key property - the one the builder declared, which is
    /// also the property of the foreign key EF Core itself configured.
    /// </summary>
    [Fact]
    public void EFCoreBuildsAUniqueIndexOverTheForeignKey()
    {
        var result = FromHibernate(ORMEnum.EFCore);

        var model = EFCoreAcceptance.BuildModel(
            GeneratedEntityCompiler.CompileOrFail(
                "UniqueOverForeignKeyEFCore",
                result.Sources.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
                GeneratedEntityCompiler.EFCoreConsumerReferences));

        var purchase = model.GetEntityTypes().Single(t => t.ClrType.Name == "Purchase");
        var foreignKey = purchase.GetForeignKeys().Single();
        var index = Assert.Single(purchase.GetIndexes(), i => i.IsUnique && i.Properties.Count == 2);
        Assert.Equal(foreignKey.Properties[0], index.Properties[0]);
        Assert.Equal("number", index.Properties[1].Name);

        var address = model.GetEntityTypes().Single(t => t.ClrType.Name == "Address");
        Assert.Contains(address.GetIndexes(), i => i.IsUnique && i.Properties.SequenceEqual(address.GetForeignKeys().Single().Properties));
    }

    /// <summary>
    /// Third level: NHibernate binds the mapping, puts the foreign key column into the
    /// table's unique key beside the scalar's, and keeps the association N:1 - unique-key
    /// does not make it logically one-to-one, which is what decision 130 rests on.
    /// </summary>
    [Fact]
    public void NHibernateBindsTheForeignKeyColumnIntoTheUniqueKeyAndKeepsTheManyToOne()
    {
        var result = FromHibernate(ORMEnum.NHibernate);

        NHibernateAcceptance.UseConfiguration(
            GeneratedEntityCompiler.CompileOrFail(
                "UniqueOverForeignKeyNHibernate",
                result.Sources.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
                GeneratedEntityCompiler.NHibernateConsumerReferences),
            result.Sources.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content),
            configuration =>
            {
                var purchase = configuration.ClassMappings.Single(c => c.MappedClass.Name == "Purchase");
                var key = Assert.Single(purchase.Table.UniqueKeyIterator, k => k.Name == "UQ_Purchases_Customer_Number");
                Assert.Equal(["CustomerId", "Number"], key.ColumnIterator.Select(c => c.Name).Order(StringComparer.Ordinal));

                var address = configuration.ClassMappings.Single(c => c.MappedClass.Name == "Address");
                Assert.Contains(address.Table.UniqueKeyIterator, k => k.ColumnIterator.Select(c => c.Name).SequenceEqual(["CustomerId"]));

                var reference = Assert.IsType<ManyToOne>(address.GetProperty("customer").Value);
                Assert.False(reference.IsLogicalOneToOne);
            });
    }
}
