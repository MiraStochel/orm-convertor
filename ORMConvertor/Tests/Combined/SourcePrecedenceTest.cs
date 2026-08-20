using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Source precedence within the input (decision 017): a fact stated by an earlier-read
/// source is never overwritten by a later one - the lower level only fills gaps - and a
/// disagreement is a Conflict record, the same event as a disagreement with the catalog
/// (decision 015). The reading order itself is a stated fact of the framework: the entity
/// class parses before the auxiliary mapping artifacts, see ParserFactory.
/// </summary>
public class SourcePrecedenceTest
{
    [Fact]
    public void OccupiedColumnFactIsKeptAndTheLaterClaimIsAConflict()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddProperty("string", "Name", "public", hasGetter: true, hasSetter: true);
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["column"] = "CustomerName" });
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["column"] = "ClientName" });

        Assert.Equal("CustomerName", builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "Name").ColumnName);
        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Conflict, record.Kind);
        Assert.Equal("Customer", record.Entity);
        Assert.Equal("Name", record.Property);
        Assert.Equal(MappingFactCategory.ColumnName, record.Category);
        Assert.Contains("ClientName", record.Reason);
    }

    [Fact]
    public void RestatingTheSameFactIsNoConflict()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddTable("Customers");
        builder.AddProperty("string", "Name", "public", hasGetter: true, hasSetter: true);
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["column"] = "CustomerName" });
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["column"] = "CustomerName" });

        // Two artifacts agreeing is the common case and no event at all.
        Assert.Empty(builder.Records);
    }

    [Fact]
    public void OccupiedTableFactIsKeptAndTheLaterClaimIsAConflict()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddTable("Clients");

        Assert.Equal("Customers", builder.EntityMap.Table);
        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Conflict, record.Kind);
        Assert.Equal(MappingFactCategory.TableName, record.Category);
        Assert.Contains("Clients", record.Reason);
    }

    [Fact]
    public void OccupiedTypeFamilyIsKeptAndTheLaterClaimIsAConflict()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddProperty("string", "Name", "public", hasGetter: true, hasSetter: true);
        builder.SetPropertyDatabaseType("Name", DatabaseType.VarChar, isUnicode: true);
        builder.SetPropertyDatabaseType("Name", DatabaseType.Text, isUnicode: true);

        var propertyMap = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "Name");
        Assert.Equal(DatabaseType.VarChar, propertyMap.Type);
        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Conflict, record.Kind);
        Assert.Equal(MappingFactCategory.DatabaseType, record.Category);
    }

    [Fact]
    public void TwoMappingArtifactsDisagreeingKeepTheFirstValueAndReportTheSecond()
    {
        var sources = new List<ConversionSource>
        {
            new()
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = """
                    public class Customer
                    {
                        public virtual int CustomerID { get; set; }
                        public virtual string Name { get; set; }
                    }
                    """,
            },
            new()
            {
                ContentType = ConversionContentType.XML,
                Content = """
                    <?xml version="1.0" encoding="utf-8" ?>
                    <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                        <class name="Customer" table="Customers">
                            <id name="CustomerID" column="CustomerID" type="Int32">
                                <generator class="identity" />
                            </id>
                            <property name="Name" column="CustomerName" />
                        </class>
                    </hibernate-mapping>
                    """,
            },
            new()
            {
                ContentType = ConversionContentType.XML,
                Content = """
                    <?xml version="1.0" encoding="utf-8" ?>
                    <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                        <class name="Customer" table="Customers">
                            <id name="CustomerID" column="CustomerID" type="Int32">
                                <generator class="identity" />
                            </id>
                            <property name="Name" column="ClientName" />
                        </class>
                    </hibernate-mapping>
                    """,
            },
        };

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.NHibernate, sources);

        // Two artifacts of the same level: the value written first wins deterministically
        // and the disagreement is said out loud instead of the later write winning silently.
        var mapping = result.Sources.Single(s => s.ContentType == ConversionContentType.XML).Content;
        Assert.Contains("CustomerName", mapping);
        Assert.DoesNotContain("ClientName", mapping);

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal("Customer", record.Entity);
        Assert.Equal("Name", record.Property);
        Assert.Equal(MappingFactCategory.ColumnName, record.Category);
        Assert.Contains("ClientName", record.Reason);
    }
}
