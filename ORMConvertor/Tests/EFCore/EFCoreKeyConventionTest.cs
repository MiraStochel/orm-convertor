using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;

namespace Tests.EFCore;

/// <summary>
/// EF Core states a key by convention as well as by attribute. The parser has to read both,
/// because the builder turns a model without a key into a keyless type - see decision 015.
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

    /// <summary>
    /// [Keyless] is the source explicitly denying a key, so the convention must stay
    /// silent even over a property it would otherwise pick - the opposite would let a
    /// convention override a statement (decision 063). The denial is read, not lost,
    /// and it survives the round trip as the [Keyless] the builder writes.
    /// </summary>
    [Fact]
    public void KeylessSuppressesTheConventionKeyAndRoundTrips()
    {
        var builder = Parse("""
            namespace EFCoreEntities;

            using Microsoft.EntityFrameworkCore;

            [Keyless]
            public class Customer
            {
                public int Id { get; set; }
            }
            """);

        Assert.Null(builder.EntityMap.PrimaryKey);
        Assert.True(builder.EntityMap.HasNoKey);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);

        var code = builder.Build()
            .Single(o => o.ContentType == ConversionContentType.CSharpEntity)
            .Content;

        Assert.Contains("[Keyless]", code);
        Assert.DoesNotContain("[Key]", code.Replace("[Keyless]", string.Empty));
    }

    /// <summary>
    /// EF Core ignores [Key] on a keyless type with a warning and builds the model
    /// keyless; the translation mirrors that as a conflict record and a keyless artifact
    /// (decision 063).
    /// </summary>
    [Fact]
    public void KeyAttributeBesideKeylessIsDroppedWithAConflict()
    {
        var builder = Parse("""
            namespace EFCoreEntities;

            using Microsoft.EntityFrameworkCore;
            using System.ComponentModel.DataAnnotations;

            [Keyless]
            public class Customer
            {
                [Key]
                public int CustomerNumber { get; set; }
            }
            """);

        Assert.Null(builder.EntityMap.PrimaryKey);

        var conflict = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal(MappingFactCategory.PrimaryKey, conflict.Category);
        Assert.Equal("CustomerNumber", conflict.Property);

        var code = builder.Build()
            .Single(o => o.ContentType == ConversionContentType.CSharpEntity)
            .Content;

        Assert.Contains("[Keyless]", code);
    }

    /// <summary>
    /// The class-level [PrimaryKey] beside [Keyless] is the case EF Core itself refuses
    /// to build a model from, so there is no meaning to translate: the completeness gate
    /// refuses the entity with a failure record (decision 063).
    /// </summary>
    [Fact]
    public void PrimaryKeyAttributeBesideKeylessRefusesTheEntity()
    {
        var builder = Parse("""
            namespace EFCoreEntities;

            using Microsoft.EntityFrameworkCore;

            [Keyless]
            [PrimaryKey(nameof(Number))]
            public class Customer
            {
                public int Number { get; set; }
            }
            """);

        Assert.Empty(builder.Build());

        var failure = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(MappingFactCategory.PrimaryKey, failure.Category);
    }

    /// <summary>
    /// A target that requires a key refuses a keyless source as before, but the reason
    /// has to say the key was denied, not that nobody supplied it - the two send the
    /// user to different repairs (decision 063).
    /// </summary>
    [Fact]
    public void KeylessSourceIsRefusedByATargetRequiringAKey()
    {
        var builder = new NHibernateWrappers.NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            namespace EFCoreEntities;

            using Microsoft.EntityFrameworkCore;

            [Keyless]
            public class Customer
            {
                public int Id { get; set; }
            }
            """);

        Assert.Empty(builder.Build());

        var failure = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Failure && r.Category == MappingFactCategory.PrimaryKey);
        Assert.Contains("states the entity has no key", failure.Reason);
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