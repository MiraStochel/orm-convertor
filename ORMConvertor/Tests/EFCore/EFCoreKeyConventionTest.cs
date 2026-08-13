using AbstractWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;

namespace Tests.EFCore;

/// <summary>
/// EF Core states a key by convention as well as by attribute. The parser has to read both,
/// because the builder turns a model without a key into a keyless type - see decision 008.
/// </summary>
public class EFCoreKeyConventionTest
{
    private static string Entity(string className, string body) => $$"""
        namespace EFCoreEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Samples")]
        public class {{className}}
        {
        {{body}}
        }
        """;

    private static EFCoreEntityBuilder Parse(string source)
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);
        return builder;
    }

    [Theory]
    [InlineData("Customer", "Id")]
    [InlineData("Customer", "CustomerId")]
    // Case-insensitive: the spelling used across this repository's own samples.
    [InlineData("Customer", "CustomerID")]
    public void PropertyMatchingTheConventionBecomesTheKey(string className, string propertyName)
    {
        var builder = Parse(Entity(className, $"    public int {propertyName} {{ get; set; }}"));

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(propertyName, Assert.Single(pk.Parts).PropertyMap.Property.Name);
    }

    [Theory]
    [InlineData("Customer", "CustomerNumber")]
    // Belongs to another entity, so the convention must not fire here.
    [InlineData("Customer", "OrderId")]
    public void PropertyOutsideTheConventionDoesNotBecomeTheKey(string className, string propertyName)
    {
        var builder = Parse(Entity(className, $"    public int {propertyName} {{ get; set; }}"));

        Assert.Null(builder.EntityMap.PrimaryKey);
    }

    [Fact]
    public void IdTakesPrecedenceOverEntityNameId()
    {
        var builder = Parse(Entity("Customer", """
                public int CustomerId { get; set; }

                public int Id { get; set; }
        """));

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal("Id", Assert.Single(pk.Parts).PropertyMap.Property.Name);
    }

    [Fact]
    public void ExplicitKeyAttributeWinsOverTheConvention()
    {
        var builder = Parse(Entity("Customer", """
                public int Id { get; set; }

                [Key]
                public int CustomerNumber { get; set; }
        """));

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal("CustomerNumber", Assert.Single(pk.Parts).PropertyMap.Property.Name);
    }

    [Fact]
    public void ClassLevelPrimaryKeyAttributeWinsOverTheConvention()
    {
        const string source = """
            namespace EFCoreEntities;

            using Microsoft.EntityFrameworkCore;
            using System.ComponentModel.DataAnnotations.Schema;

            [Table("Samples")]
            [PrimaryKey(nameof(TenantId), nameof(Number))]
            public class Customer
            {
                public int Id { get; set; }

                public int TenantId { get; set; }

                public int Number { get; set; }
            }
            """;

        var pk = Parse(source).EntityMap.PrimaryKey;

        Assert.NotNull(pk);
        Assert.Equal(new[] { "TenantId", "Number" }, pk.Parts.Select(p => p.PropertyMap.Property.Name));
    }

    /// <summary>
    /// A collection cannot be a key. Without the scalar filter a navigation property would
    /// be a candidate purely on the strength of its name.
    /// </summary>
    [Fact]
    public void CollectionPropertyIsNotAKeyCandidate()
    {
        var builder = Parse(Entity("Customer", """
                public List<Order> CustomerId { get; set; }
        """));

        Assert.Null(builder.EntityMap.PrimaryKey);
    }

    /// <summary>
    /// The point of the whole exercise: an entity whose key is stated by convention must not
    /// lose it in translation, and one that has no key must still be marked keyless.
    /// </summary>
    [Fact]
    public void ConventionKeyBecomesExplicitInTheGeneratedEntity()
    {
        var code = Parse(Entity("Customer", "    public int Id { get; set; }"))
            .Build()
            .Single(o => o.ContentType == ConversionContentType.CSharpEntity)
            .Content;

        Assert.Contains("[Key]", code);
        Assert.DoesNotContain("[Keyless]", code);
    }

    [Fact]
    public void EntityWithoutAnyKeyIsStillGeneratedAsKeyless()
    {
        var code = Parse(Entity("Customer", "    public int CustomerNumber { get; set; }"))
            .Build()
            .Single(o => o.ContentType == ConversionContentType.CSharpEntity)
            .Content;

        Assert.Contains("[Keyless]", code);
        Assert.DoesNotContain("[Key]", code);
    }

    /// <summary>
    /// The key has to survive a change of ecosystem, not just a round trip. NHibernate has no
    /// naming convention of its own, which is why reading it belongs to the source parser.
    /// </summary>
    [Fact]
    public void ConventionKeyReachesTheNHibernateMapping()
    {
        var builder = new NHibernateWrappers.NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(Entity("Customer", "    public int Id { get; set; }"));

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("<id name=\"Id\"", xml);
    }

    [Fact]
    public void ConventionKeyCarriesTheSameStrategyAsAnExplicitKey()
    {
        var byConvention = Parse(Entity("Customer", "    public int Id { get; set; }"));
        var byAttribute = Parse(Entity("Customer", """
                [Key]
                public int Id { get; set; }
        """));

        Assert.Equal(
            byAttribute.EntityMap.PrimaryKey!.Parts[0].Strategy,
            byConvention.EntityMap.PrimaryKey!.Parts[0].Strategy);
        Assert.Equal(PrimaryKeyStrategy.Auto, byConvention.EntityMap.PrimaryKey!.Parts[0].Strategy);
    }
}