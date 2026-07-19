using AbstractWrappers;
using EFCoreWrappers;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

public class CompositeKeyTest
{
    private const string CompositeEntitySource = """
        namespace EFCoreEntities;

        using Microsoft.EntityFrameworkCore;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("OrderLines", Schema = "Sales")]
        [PrimaryKey(nameof(OrderID), nameof(CompanyID))]
        public class OrderLine
        {
            public required int OrderID { get; set; }

            public required int CompanyID { get; set; }

            public required string Description { get; set; }
        }
        """;

    [Fact]
    public void EFCoreCompositeKeyIsParsedIntoModel()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(CompositeEntitySource);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(2, pk.Parts.Count);
        Assert.Equal("OrderID", pk.Parts[0].PropertyMap.Property.Name);
        Assert.Equal(1, pk.Parts[0].Order);
        Assert.Equal("CompanyID", pk.Parts[1].PropertyMap.Property.Name);
        Assert.Equal(2, pk.Parts[1].Order);
        Assert.All(pk.Parts, p => Assert.Equal(PrimaryKeyStrategy.None, p.Strategy));
    }

    [Fact]
    public void EFCoreCompositeKeyRoundTrip()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(CompositeEntitySource);
        var code = builder.Build().First().Content;

        Assert.Contains("using Microsoft.EntityFrameworkCore;", code);
        Assert.Contains("[PrimaryKey(nameof(OrderID), nameof(CompanyID))]", code);
        Assert.DoesNotContain("[Key]", code);
    }

    [Fact]
    public void EFCoreCompositeKeyToNHibernateXml()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(CompositeEntitySource);
        var outputs = builder.Build();
        var xml = outputs.Single(o => o.Content.Contains("<hibernate-mapping")).Content;

        Assert.Contains("<composite-id>", xml);
        Assert.Contains("</composite-id>", xml);

        int orderIdPos = xml.IndexOf("<key-property name=\"OrderID\"");
        int companyIdPos = xml.IndexOf("<key-property name=\"CompanyID\"");
        Assert.True(orderIdPos >= 0 && companyIdPos >= 0, "Both key-property elements must be present.");
        Assert.True(orderIdPos < companyIdPos, "Key parts must keep their declared order.");
        Assert.DoesNotContain("<generator", xml);
    }

    [Fact]
    public void NHibernateCompositeIdXmlIsParsedIntoModel()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        const string xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="OrderLine" table="OrderLines" schema="Sales">
                    <composite-id>
                        <key-property name="OrderID" column="OrderID" type="int" />
                        <key-property name="CompanyID" column="CompanyID" type="int" />
                    </composite-id>
                    <property name="Description" column="Description" type="string" />
                </class>
            </hibernate-mapping>
            """;

        parser.Parse(xmlMapping);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(2, pk.Parts.Count);
        Assert.Equal("OrderID", pk.Parts[0].PropertyMap.Property.Name);
        Assert.Equal("OrderID", pk.Parts[0].PropertyMap.ColumnName);
        Assert.Equal("CompanyID", pk.Parts[1].PropertyMap.Property.Name);
        Assert.Equal(2, pk.Parts[1].Order);
    }
}