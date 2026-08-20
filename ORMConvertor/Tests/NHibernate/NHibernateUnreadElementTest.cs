using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// The flat-class boundary of the XML mapping parser (decision 030): the version element
/// is read, because its model counterpart exists; a second column of one property is
/// dropped with a record, because the model maps a property to a single column; and every
/// element outside the flat class leaves a loss record instead of the output being
/// silently poorer than the input.
/// </summary>
public class NHibernateUnreadElementTest
{
    private static string Mapping(string body) => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Product" table="Products">
        {body}
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder ParseMapping(string mapping)
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse(mapping);
        return builder;
    }

    [Fact]
    public void VersionElementIsRead()
    {
        var builder = ParseMapping(Mapping("""
                    <id name="Id" type="Int32">
                        <generator class="identity" />
                    </id>
                    <version name="RowVersion" column="RV" type="binary" />
        """));

        var map = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "RowVersion");
        Assert.True(map.IsVersion);
        Assert.Equal("RV", map.ColumnName);
        Assert.Equal(DatabaseType.VarBinary, map.Type);

        // A read fact must not be reported as dropped.
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
    }

    [Fact]
    public void VersionSurvivesTheNHibernateRoundTrip()
    {
        const string entity = """
            public class Product
            {
                public virtual int Id { get; set; }

                public virtual byte[] RowVersion { get; set; }
            }
            """;

        var first = new NHibernateEntityBuilder();
        new NHibernateEntityParser(first).Parse(entity);
        new NHibernateXMLMappingParser(first).Parse(Mapping("""
                    <id name="Id" type="Int32">
                        <generator class="identity" />
                    </id>
                    <version name="RowVersion" column="RV" type="binary" />
        """));

        var xml = first.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        // The binary family makes the value the database's to produce, which the builder
        // states itself; the type name comes back registered (decision 019).
        Assert.Contains("<version name=\"RowVersion\"", xml);
        Assert.Contains("generated=\"always\"", xml);
        Assert.Contains("type=\"binary\"", xml);

        var second = new NHibernateEntityBuilder();
        new NHibernateEntityParser(second).Parse(entity);
        new NHibernateXMLMappingParser(second).Parse(xml);

        var map = second.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "RowVersion");
        Assert.True(map.IsVersion);
        Assert.Equal("RV", map.ColumnName);
    }

    [Fact]
    public void SecondColumnOfOnePropertyIsDroppedWithARecord()
    {
        var builder = ParseMapping(Mapping("""
                    <property name="Amount" type="Decimal">
                        <column name="AmountValue" />
                        <column name="AmountCurrency" />
                    </property>
        """));

        // The property itself is read as its first column.
        var map = Assert.Single(builder.EntityMap.PropertyMaps);
        Assert.Equal("AmountValue", map.ColumnName);

        var loss = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal("Amount", loss.Property);
        Assert.Equal(MappingFactCategory.ColumnName, loss.Category);
        Assert.Contains("AmountCurrency", loss.Reason);
    }

    [Fact]
    public void TimestampElementIsReadAsTheVersionFlag()
    {
        var builder = ParseMapping(Mapping("""
                    <id name="Id" type="Int32">
                        <generator class="identity" />
                    </id>
                    <timestamp name="Modified" column="ModifiedAt" />
        """));

        // <timestamp> is NHibernate's spelling of <version type="timestamp">, so the
        // element name itself claims the Timestamp family.
        var map = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "Modified");
        Assert.True(map.IsVersion);
        Assert.Equal("ModifiedAt", map.ColumnName);
        Assert.Equal(DatabaseType.Timestamp, map.Type);

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
    }

    [Fact]
    public void UnreadElementsOfTheClassAreReported()
    {
        var builder = ParseMapping(Mapping("""
                    <id name="Id" type="Int32">
                        <generator class="native" />
                    </id>
                    <natural-id>
                        <property name="Code" />
                    </natural-id>
                    <component name="Address">
                        <property name="Street" />
                    </component>
                    <join table="ProductExtras">
                        <key column="Id" />
                        <property name="Extra" />
                    </join>
        """));

        // Nothing inside an unread element becomes a property of the entity.
        Assert.Single(builder.EntityMap.PropertyMaps);

        var losses = builder.Records.Where(r => r.Kind == ConversionRecordKind.Loss).ToList();
        Assert.Equal(3, losses.Count);
        Assert.All(losses, r => Assert.Equal("Product", r.Entity));

        Assert.Contains(losses, r => r.Reason.Contains("<natural-id>"));
        Assert.Contains(losses, r => r.Reason.Contains("<join>"));

        // The component's name attribute names a property of the entity, so the record
        // can point at it.
        Assert.Contains(losses, r => r.Property == "Address" && r.Reason.Contains("component"));
    }

    [Fact]
    public void RootLevelElementsOtherThanClassAreReported()
    {
        var builder = ParseMapping("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Product" table="Products">
                    <id name="Id" type="Int32">
                        <generator class="native" />
                    </id>
                </class>
                <subclass name="SpecialProduct" extends="Product">
                    <property name="Extra" />
                </subclass>
            </hibernate-mapping>
            """);

        var loss = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);

        // The name attribute of a subclass names a class, not a property, so it stays in
        // the reason text.
        Assert.Null(loss.Property);
        Assert.Contains("subclass", loss.Reason);
        Assert.Contains("SpecialProduct", loss.Reason);
    }
}
