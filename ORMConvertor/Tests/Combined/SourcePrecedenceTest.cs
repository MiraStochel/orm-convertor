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

    /// <summary>
    /// The fourth conflict scenario the acceptance criterion of F5 names by hand, beside
    /// the column name, the data type and the primary key. Its write path is the column
    /// name's, but nothing exercised it: the nullability branch of
    /// SetPropertyDatabaseMapping had no test at all, so a claimed criterion rested on an
    /// unverified line.
    /// </summary>
    [Fact]
    public void OccupiedNullabilityIsKeptAndTheLaterClaimIsAConflict()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddProperty("string", "Name", "public", hasGetter: true, hasSetter: true);
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["nullable"] = "true" });
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["nullable"] = "false" });

        var propertyMap = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "Name");
        Assert.True(propertyMap.IsNullable);

        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Conflict, record.Kind);
        Assert.Equal("Customer", record.Entity);
        Assert.Equal("Name", record.Property);
        Assert.Equal(MappingFactCategory.Nullability, record.Category);
    }

    [Fact]
    public void OccupiedKeyIsKeptWithItsDetailsAndTheLaterDifferentClaimIsAConflict()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("int", "OrderNumber", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Sequence, "OrderID");
        builder.SetKeyStrategyDetails("OrderID", sourceStrategyName: "seqhilo");

        builder.AddPrimaryKey(PrimaryKeyStrategy.Assigned, "OrderNumber");

        // The key is one compound fact (decision 036): the differing later claim is
        // discarded whole and the first key stays, strategy details included.
        var key = builder.EntityMap.PrimaryKey;
        Assert.NotNull(key);
        var part = Assert.Single(key.Parts);
        Assert.Equal("OrderID", part.PropertyMap.Property.Name);
        Assert.Equal(PrimaryKeyStrategy.Sequence, part.Strategy);
        Assert.Equal("seqhilo", part.SourceStrategyName);

        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Conflict, record.Kind);
        Assert.Equal(MappingFactCategory.PrimaryKey, record.Category);
        Assert.Contains("OrderNumber", record.Reason);
    }

    [Fact]
    public void RestatingTheSameKeyIsNoEvent()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");

        Assert.Empty(builder.Records);
        Assert.Equal(PrimaryKeyStrategy.Identity, Assert.Single(builder.EntityMap.PrimaryKey!.Parts).Strategy);
    }

    [Fact]
    public void SameKeyFillsTheStrategyTheFirstClaimLeftUnspecified()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "OrderID");
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");

        // Unspecified states nothing, so the later stated strategy fills the gap - the
        // same completion the catalog performs for an identity column.
        Assert.Empty(builder.Records);
        Assert.Equal(PrimaryKeyStrategy.Identity, Assert.Single(builder.EntityMap.PrimaryKey!.Parts).Strategy);
    }

    [Fact]
    public void DifferingStrategyOverTheSameKeyIsAConflictAndItsDetailsFallWithIt()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Sequence, "OrderID");
        builder.SetKeyStrategyDetails("OrderID", parameters: new Dictionary<GeneratorParameter, string>
        {
            [GeneratorParameter.SequenceName] = "order_seq",
        });

        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");
        builder.SetKeyStrategyDetails("OrderID", sourceStrategyName: "native");

        // The strategy claim was discarded, so the details trailing it are dropped with
        // it instead of landing on the kept key; the conflict record already stands.
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Sequence, part.Strategy);
        Assert.Equal("order_seq", part.StrategyParameters[GeneratorParameter.SequenceName]);
        Assert.Null(part.SourceStrategyName);

        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Conflict, record.Kind);
        Assert.Equal(MappingFactCategory.PrimaryKeyStrategy, record.Category);
        Assert.Equal("OrderID", record.Property);
    }

    [Fact]
    public void DetailOfADiscardedKeyClaimIsDroppedNotAnException()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("int", "OrderNumber", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");

        builder.AddPrimaryKey(PrimaryKeyStrategy.Sequence, "OrderNumber");
        builder.SetKeyStrategyDetails("OrderNumber", parameters: new Dictionary<GeneratorParameter, string>
        {
            [GeneratorParameter.SequenceName] = "order_seq",
        });

        // "OrderNumber" is no part of the kept key; outside a discarded claim that call
        // is a programming error and throws, here it falls with its claim.
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal("OrderID", part.PropertyMap.Property.Name);
        Assert.Empty(part.StrategyParameters);
        Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void OccupiedGeneratorParameterIsKeptAndTheLaterClaimIsAConflict()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Sequence, "OrderID");
        builder.SetKeyStrategyDetails("OrderID", parameters: new Dictionary<GeneratorParameter, string>
        {
            [GeneratorParameter.SequenceName] = "order_seq",
        });

        builder.AddPrimaryKey(PrimaryKeyStrategy.Sequence, "OrderID");
        builder.SetKeyStrategyDetails("OrderID", parameters: new Dictionary<GeneratorParameter, string>
        {
            [GeneratorParameter.SequenceName] = "other_seq",
            [GeneratorParameter.BlockSize] = "10",
        });

        // Entry by entry like the other key-value facts: the occupied sequence name is
        // kept and reported, the block size nobody stated yet is filled.
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal("order_seq", part.StrategyParameters[GeneratorParameter.SequenceName]);
        Assert.Equal("10", part.StrategyParameters[GeneratorParameter.BlockSize]);

        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Conflict, record.Kind);
        Assert.Equal(MappingFactCategory.PrimaryKeyStrategy, record.Category);
        Assert.Contains("other_seq", record.Reason);
    }

    [Fact]
    public void TwoMappingArtifactsClaimingDifferentKeysKeepTheFirstAndReportTheConflict()
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
                        public virtual int CustomerNumber { get; set; }
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
                                <generator class="native" />
                            </id>
                            <property name="CustomerNumber" column="CustomerNumber" />
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
                            <id name="CustomerNumber" column="CustomerNumber" type="Int32">
                                <generator class="sequence">
                                    <param name="sequence">customer_seq</param>
                                </generator>
                            </id>
                        </class>
                    </hibernate-mapping>
                    """,
            },
        };

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.NHibernate, sources);

        // The audit's reachable case (finding 1.2 of 2026-08-21): two mapping XMLs over
        // the same class claiming different keys. The first key wins deterministically,
        // the second claim - its trailing generator details included - is discarded with
        // a conflict record instead of silently replacing the key.
        var mapping = result.Sources.Single(s => s.ContentType == ConversionContentType.XML).Content;
        Assert.Contains("<id name=\"CustomerID\"", mapping);
        Assert.DoesNotContain("<id name=\"CustomerNumber\"", mapping);
        Assert.DoesNotContain("customer_seq", mapping);

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal("Customer", record.Entity);
        Assert.Equal(MappingFactCategory.PrimaryKey, record.Category);
        Assert.Contains("CustomerNumber", record.Reason);
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
