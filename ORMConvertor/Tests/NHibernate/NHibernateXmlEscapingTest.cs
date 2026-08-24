using System.Xml.Linq;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Verification;

namespace Tests.NHibernate;

/// <summary>
/// The emission side of the XML mapping (decision 046). A name holding a markup character
/// used to be interpolated into the document raw, so the artifact was not well-formed XML at
/// all - which F11 claims it always is. Two shapes of the same defect are covered: a name the
/// caller states directly, and a legal NHibernate mapping read back in, whose entities the
/// XML reader decodes on the way in and which therefore has to be encoded again on the way
/// out for the round trip to close.
/// </summary>
public class NHibernateXmlEscapingTest
{
    private static string GeneratedMapping(NHibernateEntityBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.XML).Content;

    [Fact]
    public void MarkupCharactersInNamesLeaveAWellFormedDocument()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Product");
        builder.AddTable("R&D \"Products\"");
        builder.AddSchema("<Sales>");
        builder.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("string", "Name", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["column"] = "R&D \"Name\"" });

        var xml = GeneratedMapping(builder);

        // Parsing at all is the claim: before the writer this threw on the bare ampersand.
        var document = XDocument.Parse(xml);
        var element = document.Descendants().Single(e => e.Name.LocalName == "class");

        Assert.Equal("R&D \"Products\"", element.Attribute("table")!.Value);
        Assert.Equal("<Sales>", element.Attribute("schema")!.Value);

        var property = document.Descendants().Single(e => e.Name.LocalName == "property");
        Assert.Equal("R&D \"Name\"", property.Attribute("column")!.Value);

        // The escaped forms, not the raw characters, are what stands in the text.
        Assert.Contains("table=\"R&amp;D &quot;Products&quot;\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AMappingWithEscapedNamesSurvivesTheRoundTrip()
    {
        const string mapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Product" table="R&amp;D">
                    <id name="Id" column="Id" type="Int32">
                        <generator class="identity" />
                    </id>
                    <property name="Name" column="Name &amp; Title" type="String" />
                </class>
            </hibernate-mapping>
            """;

        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse("""
            public class Product
            {
                public virtual int Id { get; set; }

                public virtual string Name { get; set; }
            }
            """);
        new NHibernateXMLMappingParser(builder).Parse(mapping);

        var xml = GeneratedMapping(builder);

        Assert.Equal("R&D", XDocument.Parse(xml).Descendants().Single(e => e.Name.LocalName == "class").Attribute("table")!.Value);

        // Second verification level (decision 016): the schema validator parses first, so an
        // unescaped ampersand fails here before any schema rule is even consulted.
        Assert.Empty(NHibernateMappingSchema.Validate(xml));
    }
}
