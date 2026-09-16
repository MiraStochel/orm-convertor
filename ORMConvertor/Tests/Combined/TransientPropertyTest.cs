using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using DatabaseCatalog;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Catalog;

namespace Tests.Combined;

/// <summary>
/// A property the source states is not persisted (decision 072): a carried mapping fact,
/// not a missing column. EF Core spells it [NotMapped], NHibernate leaves the property out
/// of the mapping while the class keeps it, Dapper has nowhere to put it, and the catalog
/// has nothing to say about it.
/// </summary>
public class TransientPropertyTest
{
    private const string EFCoreCustomer = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }

            public string Name { get; set; }

            [NotMapped]
            public string FullName { get; set; }

            [NotMapped]
            public List<string> Tags { get; set; } = [];
        }
        """;

    private const string NHibernateCustomer = """
        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string Name { get; set; }
            public virtual string FullName { get; set; }
        }
        """;

    private const string NHibernateMapping = """
        <?xml version="1.0" encoding="utf-8"?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32">
              <generator class="identity" />
            </id>
            <property name="Name" column="Name" type="String" />
          </class>
        </hibernate-mapping>
        """;

    [Fact]
    public void NotMappedIsReadAsATransientPropertyWithoutALossRecord()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(EFCoreCustomer);

        var map = builder.EntityMaps.Single();
        Assert.True(map.PropertyMaps.Single(pm => pm.Property.Name == "FullName").IsTransient);
        Assert.True(map.PropertyMaps.Single(pm => pm.Property.Name == "Tags").IsTransient);
        Assert.False(map.PropertyMaps.Single(pm => pm.Property.Name == "Name").IsTransient);

        // The property stays a member of the class with its language facts.
        Assert.Equal(ScalarType.String, map.PropertyMaps.Single(pm => pm.Property.Name == "FullName").Property.Type!.ScalarType);

        // The annotation used to fall into the unread-annotation branch; a read fact must
        // not be reported as dropped.
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
    }

    [Fact]
    public void EFCoreRoundTripKeepsNotMapped()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(EFCoreCustomer);

        var code = builder.Build().Single().Content;

        Assert.Contains("[NotMapped]", code);
        Assert.Contains("FullName", code);

        var reparsed = new EFCoreEntityBuilder();
        new EFCoreEntityParser(reparsed).Parse(code);
        Assert.True(reparsed.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "FullName").IsTransient);
    }

    [Fact]
    public void NHibernateKeepsThePropertyOnTheClassAndLeavesItOutOfTheMapping()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(EFCoreCustomer);

        var outputs = builder.Build();
        var code = outputs.Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;
        var mapping = outputs.Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("FullName", code);
        Assert.Contains("Tags", code);
        Assert.Contains("name=\"Name\"", mapping);
        Assert.DoesNotContain("FullName", mapping);
        Assert.DoesNotContain("Tags", mapping);

        // Leaving the element out is the statement, so nothing is lost or incomplete - not
        // even for the collection, which is not a collection without a relation.
        Assert.DoesNotContain(builder.Records, r => r.Property is "FullName" or "Tags");
    }

    [Fact]
    public void AClassPropertyTheMappingDoesNotNameIsTransient()
    {
        var builder = new EFCoreEntityBuilder();
        new NHibernateEntityParser(builder).Parse(NHibernateCustomer);
        new NHibernateXMLMappingParser(builder).Parse(NHibernateMapping);

        var map = builder.EntityMaps.Single();
        Assert.True(map.PropertyMaps.Single(pm => pm.Property.Name == "FullName").IsTransient);
        Assert.False(map.PropertyMaps.Single(pm => pm.Property.Name == "Name").IsTransient);
        Assert.False(map.PropertyMaps.Single(pm => pm.Property.Name == "CustomerId").IsTransient);

        var code = builder.Build().Single().Content;
        Assert.Contains("[NotMapped]", code);
        Assert.Contains("FullName", code);
    }

    [Fact]
    public void NHibernateRoundTripLeavesTheUnmappedPropertyOutOfTheMapping()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(NHibernateCustomer);
        new NHibernateXMLMappingParser(builder).Parse(NHibernateMapping);

        var outputs = builder.Build();
        var code = outputs.Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;
        var mapping = outputs.Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("FullName", code);
        Assert.DoesNotContain("FullName", mapping);
    }

    /// <summary>
    /// Source precedence (decision 017) in both orders: the first claim about whether the
    /// property has a column is kept and the later one is a conflict record. A second mapping
    /// document over one class is not valid NHibernate input; the rule only has to be
    /// deterministic and loud about it.
    /// </summary>
    [Fact]
    public void ALaterDocumentCannotOverturnWhetherThePropertyIsPersisted()
    {
        const string mappingWithFullName = """
            <?xml version="1.0" encoding="utf-8"?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
              <class name="Customer" table="Customers" schema="Sales">
                <id name="CustomerId" column="CustomerId" type="Int32">
                  <generator class="identity" />
                </id>
                <property name="Name" column="Name" type="String" />
                <property name="FullName" column="FullName" type="String" />
              </class>
            </hibernate-mapping>
            """;

        // Transient first, mapped second: the column claim is the conflict.
        var transientFirst = new EFCoreEntityBuilder();
        new NHibernateEntityParser(transientFirst).Parse(NHibernateCustomer);
        new NHibernateXMLMappingParser(transientFirst).Parse(NHibernateMapping);
        new NHibernateXMLMappingParser(transientFirst).Parse(mappingWithFullName);

        var fullName = transientFirst.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "FullName");
        Assert.True(fullName.IsTransient);
        Assert.Null(fullName.ColumnName);
        Assert.Contains(transientFirst.Records, r =>
            r.Kind == ConversionRecordKind.Conflict
            && r.Category == MappingFactCategory.TransientProperty
            && r.Property == "FullName");

        // Mapped first, transient second: the omission is the conflict.
        var mappedFirst = new EFCoreEntityBuilder();
        new NHibernateEntityParser(mappedFirst).Parse(NHibernateCustomer);
        new NHibernateXMLMappingParser(mappedFirst).Parse(mappingWithFullName);
        new NHibernateXMLMappingParser(mappedFirst).Parse(NHibernateMapping);

        fullName = mappedFirst.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "FullName");
        Assert.False(fullName.IsTransient);
        Assert.Equal("FullName", fullName.ColumnName);
        Assert.Contains(mappedFirst.Records, r =>
            r.Kind == ConversionRecordKind.Conflict
            && r.Category == MappingFactCategory.TransientProperty
            && r.Property == "FullName");
    }

    /// <summary>Without a mapping document the class states nothing about columns (F6).</summary>
    [Fact]
    public void WithoutAMappingDocumentNothingIsTransient()
    {
        var builder = new EFCoreEntityBuilder();
        new NHibernateEntityParser(builder).Parse(NHibernateCustomer);

        Assert.All(builder.EntityMaps.Single().PropertyMaps, pm => Assert.False(pm.IsTransient));
    }

    /// <summary>
    /// The mechanical loss record of decision 004: the descriptor says Dapper cannot record
    /// the fact, and the intersection with the model produces the record - a statement about
    /// Dapper, not about the tool.
    /// </summary>
    [Fact]
    public void DapperReportsTheFactItCannotRecord()
    {
        var builder = new DapperEntityBuilder();
        new EFCoreEntityParser(builder).Parse(EFCoreCustomer);

        var code = builder.Build().Single().Content;
        Assert.Contains("FullName", code);

        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss
            && r.Category == MappingFactCategory.TransientProperty
            && r.Property == "FullName");
    }

    [Fact]
    public void ANotMappedIdIsNotTheConventionKey()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations.Schema;

            public class Customer
            {
                [NotMapped]
                public int Id { get; set; }

                public string Name { get; set; }
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);

        Assert.Null(builder.EntityMap.PrimaryKey);
        Assert.Contains("[Keyless]", builder.Build().Single().Content);
    }

    [Fact]
    public void ANotMappedNavigationFoundsNoRelation()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            public class Order
            {
                [Key]
                public int OrderId { get; set; }

                public int CustomerId { get; set; }

                [NotMapped]
                public Customer Customer { get; set; }

                [NotMapped]
                public List<OrderLine> Lines { get; set; } = [];
            }

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }
            }

            public class OrderLine
            {
                [Key]
                public int OrderLineId { get; set; }

                public int OrderId { get; set; }
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);
        builder.Build();

        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        Assert.Empty(order.Relations);
        Assert.True(order.PropertyMaps.Single(pm => pm.Property.Name == "Customer").IsTransient);
        Assert.True(order.PropertyMaps.Single(pm => pm.Property.Name == "Lines").IsTransient);
    }

    /// <summary>
    /// Both claims enter the model and the completeness gate refuses the contradiction, the
    /// way decision 063 refuses [Keyless] beside a class-level [PrimaryKey].
    /// </summary>
    [Fact]
    public void AKeyBesideNotMappedRefusesTheEntity()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            public class Customer
            {
                [Key]
                [NotMapped]
                public int CustomerId { get; set; }

                public string Name { get; set; }
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);

        Assert.Empty(builder.Build());

        var failure = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(MappingFactCategory.TransientProperty, failure.Category);
        Assert.Equal("CustomerId", failure.Property);
    }

    /// <summary>
    /// A column of the same name is no statement about a property the source says is not
    /// persisted: the catalog supplies nothing, reports nothing about it, and does not pair
    /// its column to the property when translating a constraint (decision 072).
    /// </summary>
    [Fact]
    public void TheCatalogSkipsATransientProperty()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(EFCoreCustomer);

        var image = new TableImage
        {
            Schema = "Sales",
            Name = "Customers",
            Columns =
            [
                new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
                new ColumnImage { Name = "Name", Type = DatabaseType.VarChar, IsUnicode = true, Length = 100, IsNullable = false, IsIdentity = false },
                new ColumnImage { Name = "FullName", Type = DatabaseType.VarChar, IsUnicode = true, Length = 200, IsNullable = false, IsIdentity = false },
            ],
            PrimaryKeyColumns = ["CustomerId"],
            ForeignKeys = [],
            UniqueConstraints = [new UniqueConstraintImage { Name = "UQ_FullName", Columns = ["FullName"] }],
        };

        CatalogCompletion.Complete(builder, new FakeCatalogReader(image));

        var em = builder.EntityMaps.Single();
        var fullName = em.PropertyMaps.Single(pm => pm.Property.Name == "FullName");
        Assert.True(fullName.IsTransient);
        Assert.Null(fullName.Type);
        Assert.Null(fullName.Length);
        Assert.Null(fullName.IsNullable);
        Assert.DoesNotContain(builder.Records, r => r.Property == "FullName");

        // The neighbouring column still completes, and the constraint over the transient
        // property's namesake column finds no property to hang on.
        Assert.Equal(100, em.PropertyMaps.Single(pm => pm.Property.Name == "Name").Length);
        Assert.Empty(em.UniqueConstraints);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Category == MappingFactCategory.UniqueConstraint);
    }

    /// <summary>
    /// A scalar named after the foreign key column of an association is the flat form of that
    /// column, paired by the resolution phase and written by EF Core as the key property; the
    /// column is in the table, so the mapping does not state the property is unpersisted.
    /// </summary>
    [Fact]
    public void AForeignKeyColumnScalarIsNotTransient()
    {
        const string source = """
            public class Order
            {
                public virtual int OrderId { get; set; }
                public virtual int CustomerId { get; set; }
                public virtual Customer Customer { get; set; }
            }

            public class Customer
            {
                public virtual int CustomerId { get; set; }
            }
            """;

        const string mapping = """
            <?xml version="1.0" encoding="utf-8"?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
              <class name="Order" table="Orders">
                <id name="OrderId" column="OrderId" type="Int32">
                  <generator class="identity" />
                </id>
                <many-to-one name="Customer" class="Customer" column="CustomerId" />
              </class>
              <class name="Customer" table="Customers">
                <id name="CustomerId" column="CustomerId" type="Int32">
                  <generator class="identity" />
                </id>
              </class>
            </hibernate-mapping>
            """;

        var builder = new EFCoreEntityBuilder();
        new NHibernateEntityParser(builder).Parse(source);
        new NHibernateXMLMappingParser(builder).Parse(mapping);

        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        Assert.False(order.PropertyMaps.Single(pm => pm.Property.Name == "CustomerId").IsTransient);
        Assert.False(order.PropertyMaps.Single(pm => pm.Property.Name == "Customer").IsTransient);

        var code = builder.Build().Single(o => o.Content.Contains("class Order")).Content;
        Assert.Contains("[ForeignKey(\"CustomerId\")]", code);
        Assert.DoesNotContain("[NotMapped]", code);
    }

    /// <summary>
    /// An element the parser does not read still maps the property - in a shape the model
    /// does not carry, reported as a loss (decision 030) - so the property is not transient.
    /// </summary>
    [Fact]
    public void APropertyMappedByAnUnreadElementIsNotTransient()
    {
        const string source = """
            public class Customer
            {
                public virtual int CustomerId { get; set; }
                public virtual Address Address { get; set; }
            }
            """;

        const string mapping = """
            <?xml version="1.0" encoding="utf-8"?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
              <class name="Customer" table="Customers">
                <id name="CustomerId" column="CustomerId" type="Int32">
                  <generator class="identity" />
                </id>
                <component name="Address" class="Address">
                  <property name="Street" column="Street" type="String" />
                </component>
              </class>
            </hibernate-mapping>
            """;

        var builder = new EFCoreEntityBuilder();
        new NHibernateEntityParser(builder).Parse(source);
        new NHibernateXMLMappingParser(builder).Parse(mapping);

        var customer = builder.EntityMaps.Single();
        Assert.False(customer.PropertyMaps.Single(pm => pm.Property.Name == "Address").IsTransient);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "Address");
    }

    [Fact]
    public void AUniqueConstraintOverATransientPropertyIsDroppedByBothTargets()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            [Index(nameof(FullName), IsUnique = true)]
            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [NotMapped]
                public string FullName { get; set; }
            }
            """;

        var efCore = new EFCoreEntityBuilder();
        new EFCoreEntityParser(efCore).Parse(source);
        var code = efCore.Build().Single().Content;

        Assert.DoesNotContain("[Index(", code);
        Assert.Contains(efCore.Records, r =>
            r.Kind == ConversionRecordKind.Loss
            && r.Category == MappingFactCategory.UniqueConstraint
            && r.Property == "FullName");

        var nhibernate = new NHibernateEntityBuilder();
        new EFCoreEntityParser(nhibernate).Parse(source);
        var mapping = nhibernate.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.DoesNotContain("unique", mapping);
        Assert.Contains(nhibernate.Records, r =>
            r.Kind == ConversionRecordKind.Loss
            && r.Category == MappingFactCategory.UniqueConstraint
            && r.Property == "FullName");
    }
}
