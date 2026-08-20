using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using NHibernateWrappers;

namespace Tests.EFCore;

/// <summary>
/// Database nullability travels through [Required], not through the language shape: EF Core
/// reads a column's nullability from the property's type unless [Required] overrides it, so
/// the builder writes the annotation exactly where the stated claim and the type disagree.
/// The required modifier stays what it is - a language device for a non-nullable property
/// without an initializer - and the question mark stays the language claim of the source.
/// </summary>
public class EFCoreNullabilityTest
{
    [Fact]
    public void RequiredAnnotationRoundTrips()
    {
        const string source = """
            public class Customer
            {
                [Key]
                public int CustomerID { get; set; }

                [Required]
                public string? DeliveryRun { get; set; }
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);

        // The two channels stay apart in the model: the question mark is the language
        // claim, [Required] the database one. Folding them used to lose the question mark.
        var map = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "DeliveryRun");
        Assert.True(map.Property.Type!.IsNullable);
        Assert.False(map.IsNullable);

        var code = builder.Build().Single().Content;
        Assert.Contains("[Required]", code);
        Assert.Contains("public string? DeliveryRun { get; set; }", code);
    }

    [Fact]
    public void StatedNotNullOverANullableTypeBecomesRequired()
    {
        var builder = new EFCoreEntityBuilder();
        new NHibernateEntityParser(builder).Parse("""
            public class Customer
            {
                public virtual int CustomerID { get; set; }

                public virtual string? CustomerName { get; set; }
            }
            """);
        new NHibernateXMLMappingParser(builder).Parse("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Customer" table="Customers">
                    <id name="CustomerID" column="CustomerID" type="Int32">
                        <generator class="assigned" />
                    </id>
                    <property name="CustomerName" not-null="true" />
                </class>
            </hibernate-mapping>
            """);

        var code = builder.Build().Single().Content;

        // The type says nullable, the mapping says NOT NULL; only [Required] can carry
        // that claim into the annotation artifact, and the language shape stays the
        // source's - the question mark survives.
        Assert.Contains("[Required]", code);
        Assert.Contains("public virtual string? CustomerName { get; set; }", code);
    }

    [Fact]
    public void AgreementWithTheTypeEmitsNoAnnotation()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Customer
            {
                [Key]
                public int CustomerID { get; set; }

                public required string CustomerName { get; set; }
            }
            """);

        var code = builder.Build().Single().Content;

        // A non-nullable property already claims a non-nullable column (rule E4);
        // restating it with [Required] would write the target's own reading down as noise.
        Assert.DoesNotContain("[Required]", code);
        Assert.Contains("public required string CustomerName { get; set; }", code);
    }

    [Fact]
    public void ANullableColumnBehindANonNullableTypeIsALoss()
    {
        var builder = new EFCoreEntityBuilder();
        new NHibernateEntityParser(builder).Parse("""
            public class Customer
            {
                public virtual int CustomerID { get; set; }

                public virtual string CustomerName { get; set; }
            }
            """);
        new NHibernateXMLMappingParser(builder).Parse("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Customer" table="Customers">
                    <id name="CustomerID" column="CustomerID" type="Int32">
                        <generator class="assigned" />
                    </id>
                    <property name="CustomerName" not-null="false" />
                </class>
            </hibernate-mapping>
            """);

        var code = builder.Build().Single().Content;

        // The opposite disagreement has no annotation - EF Core reads NOT NULL from the
        // type and only the fluent API could override it - so the claim is dropped and
        // the drop is recorded instead of silent (decision 004).
        Assert.DoesNotContain("[Required]", code);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal(MappingFactCategory.Nullability, record.Category);
        Assert.Equal("CustomerName", record.Property);
    }
}
