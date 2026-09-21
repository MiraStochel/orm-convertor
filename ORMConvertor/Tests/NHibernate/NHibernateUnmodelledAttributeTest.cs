using AbstractWrappers.Diagnostics;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// The attribute half of the flat-reading boundary (decisions 048 and 004). The element
/// half has had records since decision 030 (<see cref="NHibernateUnreadElementTest"/>),
/// but the attributes of &lt;class&gt; and &lt;property&gt; the model has no place for used to
/// vanish without a word - among them formula and where, which do not merely make the
/// output poorer but make it mean something else.
/// </summary>
public class NHibernateUnmodelledAttributeTest
{
    private static string Mapping(string classAttributes, string body) => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Product" table="Products"{classAttributes}>
                <id name="Id" type="Int32">
                    <generator class="identity" />
                </id>
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

    private static List<ConversionRecord> Losses(NHibernateEntityBuilder builder)
        => [.. builder.Records.Where(r => r.Kind == ConversionRecordKind.Loss)];

    [Theory]
    [InlineData("formula", "SubTotal * Rate")]
    [InlineData("access", "field.camelcase")]
    [InlineData("insert", "false")]
    [InlineData("update", "false")]
    [InlineData("lazy", "true")]
    [InlineData("generated", "always")]
    [InlineData("optimistic-lock", "false")]
    public void UnmodelledPropertyAttributeLeavesARecord(string attribute, string value)
    {
        var builder = ParseMapping(Mapping(string.Empty, $"""
                    <property name="Tax" type="Decimal" {attribute}="{value}" />
        """));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Product", loss.Entity);
        Assert.Equal("Tax", loss.Property);
        Assert.Contains($"{attribute}=\"{value}\"", loss.Reason);
        Assert.Contains("<property>", loss.Reason);

        // Decision 048: the categories are a closed vocabulary of facts the model holds and
        // none of these is one of them, so the record names none.
        Assert.Null(loss.Category);
    }

    [Theory]
    [InlineData("discriminator-value", "P")]
    [InlineData("where", "IsDeleted = 0")]
    [InlineData("mutable", "false")]
    [InlineData("optimistic-lock", "dirty")]
    [InlineData("dynamic-insert", "true")]
    [InlineData("dynamic-update", "true")]
    [InlineData("batch-size", "25")]
    [InlineData("lazy", "false")]
    public void UnmodelledClassAttributeLeavesARecord(string attribute, string value)
    {
        var builder = ParseMapping(Mapping($" {attribute}=\"{value}\"", string.Empty));

        var loss = Assert.Single(Losses(builder));

        Assert.Equal("Product", loss.Entity);
        Assert.Contains($"{attribute}=\"{value}\"", loss.Reason);
        Assert.Contains("<class>", loss.Reason);

        // The attribute concerns the entity as a whole, so the record points at no property.
        Assert.Null(loss.Property);
        Assert.Null(loss.Category);
    }

    [Fact]
    public void FormulaSaysThatTheOutputInventsAColumn()
    {
        var builder = ParseMapping(Mapping(string.Empty, """
                    <property name="Total" type="Decimal" formula="Quantity * UnitPrice" />
        """));

        // The property is read - what is lost is the expression behind it, not the member.
        var map = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "Total");
        Assert.Null(map.ColumnName);

        var loss = Assert.Single(Losses(builder));
        Assert.Contains("Quantity * UnitPrice", loss.Reason);
        Assert.Contains("names it after the property", loss.Reason);
    }

    [Fact]
    public void WhereSaysThatTheTargetReadsTheWholeTable()
    {
        var builder = ParseMapping(Mapping(" where=\"IsDeleted = 0\"", string.Empty));

        var loss = Assert.Single(Losses(builder));
        Assert.Contains("IsDeleted = 0", loss.Reason);
        Assert.Contains("reads the whole table", loss.Reason);
    }

    [Fact]
    public void EveryStatedAttributeOfOneElementLeavesItsOwnRecord()
    {
        var builder = ParseMapping(Mapping(" mutable=\"false\" dynamic-update=\"true\"", """
                    <property name="Tax" type="Decimal" insert="false" update="false" />
        """));

        var losses = Losses(builder);
        Assert.Equal(4, losses.Count);

        Assert.Equal(2, losses.Count(r => r.Property is null));
        Assert.Equal(2, losses.Count(r => r.Property == "Tax"));
    }

    [Fact]
    public void AnAttributeTheParserReadsIsNotReportedAsDropped()
    {
        var builder = ParseMapping(Mapping(" schema=\"Warehouse\"", """
                    <property name="Name" type="String" column="ProductName" length="120" not-null="true" unique="true" />
        """));

        Assert.Empty(Losses(builder));
    }

    [Fact]
    public void AnEmptyAttributeStatesNothingAndIsNotReported()
    {
        // The source wrote the attribute without writing a value, so there is no fact to
        // lose - the same threshold ReportUnmodelledFacets applies to index, check and
        // default.
        var builder = ParseMapping(Mapping(" where=\"\"", """
                    <property name="Tax" type="Decimal" formula="" />
        """));

        Assert.Empty(Losses(builder));
    }
}
