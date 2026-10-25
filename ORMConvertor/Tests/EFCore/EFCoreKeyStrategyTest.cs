using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;

namespace Tests.EFCore;

/// <summary>
/// EF Core states how a key value arises in two places: the [DatabaseGenerated] annotation and,
/// far more often, a convention that depends on the type of the key. Reading only the first one
/// makes every key look generated; reading neither makes none of them look generated.
/// </summary>
public class EFCoreKeyStrategyTest
{
    private static string Entity(string keyProperty) => $$"""
        public class Customer
        {
        {{keyProperty}}

            public string Name { get; set; }
        }
        """;

    private static EFCoreEntityBuilder Parse(string source)
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);
        return builder;
    }

    [Theory]
    [InlineData("    [Key]\n    public int CustomerID { get; set; }", PrimaryKeyStrategy.Auto)]
    [InlineData("    [Key]\n    public long CustomerID { get; set; }", PrimaryKeyStrategy.Auto)]
    [InlineData("    [Key]\n    public sbyte CustomerID { get; set; }", PrimaryKeyStrategy.Auto)]
    [InlineData("    [Key]\n    public uint CustomerID { get; set; }", PrimaryKeyStrategy.Auto)]
    // The core convention leaves byte out by name: no IDENTITY column (EFCoreValueGenerationProviderTest).
    [InlineData("    [Key]\n    public byte CustomerID { get; set; }", PrimaryKeyStrategy.Assigned)]
    [InlineData("    [Key]\n    public Guid CustomerID { get; set; }", PrimaryKeyStrategy.Uuid)]
    [InlineData("    [Key]\n    public string CustomerID { get; set; }", PrimaryKeyStrategy.Assigned)]
    // [DatabaseGenerated(Identity)] names no mechanism: the provider makes an IDENTITY column of
    // an integer or decimal key and the client a GUID of a Guid or string key (decision 187).
    [InlineData("    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]\n    public byte CustomerID { get; set; }", PrimaryKeyStrategy.Auto)]
    [InlineData("    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]\n    public decimal CustomerID { get; set; }", PrimaryKeyStrategy.Auto)]
    [InlineData("    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]\n    public Guid CustomerID { get; set; }", PrimaryKeyStrategy.Uuid)]
    [InlineData("    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]\n    public string CustomerID { get; set; }", PrimaryKeyStrategy.Uuid)]
    [InlineData("    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]\n    public DateTime CustomerID { get; set; }", PrimaryKeyStrategy.Unspecified)]
    public void StrategyOfAnUnannotatedKeyFollowsItsType(string keyProperty, PrimaryKeyStrategy expected)
    {
        var builder = Parse(Entity(keyProperty.Replace("\n", "\r\n")));

        // Treating every [Key] as an identity column is what produced <generator class="identity" />
        // for a string key - a mapping the database rejects.
        Assert.Equal(expected, Assert.Single(builder.EntityMap.PrimaryKey!.Parts).Strategy);
    }

    [Fact]
    public void AnnotationOverridesTheConventionAndSurvivesGeneration()
    {
        var builder = Parse(Entity("""
                [Key]
                [DatabaseGenerated(DatabaseGeneratedOption.None)]
                public int CustomerID { get; set; }
        """));

        Assert.Equal(PrimaryKeyStrategy.Assigned, Assert.Single(builder.EntityMap.PrimaryKey!.Parts).Strategy);

        // Here the annotation has to be written back: without it EF Core would generate the
        // value for an int key by convention, so silence would reverse the claim.
        var code = builder.Build().Single().Content;
        Assert.Contains("[DatabaseGenerated(DatabaseGeneratedOption.None)]", code);
    }

    [Fact]
    public void ConventionalGenerationIsNotRestatedInTheOutput()
    {
        var builder = Parse(Entity("""
                [Key]
                public int CustomerID { get; set; }
        """));

        // Auto on an int key is exactly what EF Core does on its own, so the artifact stays as
        // it was; the annotation appears only where it changes something.
        var code = builder.Build().Single().Content;
        Assert.DoesNotContain("[DatabaseGenerated", code);
    }

    [Fact]
    public void ConventionalGenerationOfAnUnstatedStrategyIsReported()
    {
        var builder = new EFCoreEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddProperty("int", "CustomerID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "CustomerID");

        var code = builder.Build().Single().Content;

        // Nothing is written, yet the output behaves as generated: EF Core generates an
        // int key on its own. The claim is the target's and the record says so - the
        // mirror of NHibernate's record for its written assigned fallback (decision 064).
        Assert.DoesNotContain("[DatabaseGenerated", code);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Convention);
        Assert.Equal(MappingFactCategory.PrimaryKeyStrategy, record.Category);
        Assert.Equal("CustomerID", record.Property);
    }

    /// <summary>
    /// The attribute is written where EF Core would not generate the key on its own and the
    /// attribute gives it the mechanism the model carries (decision 187): an IDENTITY column of
    /// an integer or decimal key for Auto and Identity, a GUID the client generates of a Guid or
    /// string key for Uuid. Identity loses its pin either way - only the fluent UseIdentityColumn
    /// states it -, and a mechanism the annotation cannot give the key is a loss that says what
    /// the artifact does instead.
    /// </summary>
    [Theory]
    [InlineData("byte", PrimaryKeyStrategy.Auto, true, null)]
    [InlineData("decimal", PrimaryKeyStrategy.Auto, true, null)]
    [InlineData("int", PrimaryKeyStrategy.Auto, false, null)]
    [InlineData("int", PrimaryKeyStrategy.Identity, false, ConversionRecordKind.Loss)]
    [InlineData("byte", PrimaryKeyStrategy.Identity, true, ConversionRecordKind.Loss)]
    [InlineData("Guid", PrimaryKeyStrategy.Uuid, false, null)]
    [InlineData("string", PrimaryKeyStrategy.Uuid, true, null)]
    [InlineData("string", PrimaryKeyStrategy.Auto, false, ConversionRecordKind.Loss)]
    [InlineData("int", PrimaryKeyStrategy.Sequence, false, ConversionRecordKind.Loss)]
    [InlineData("byte", PrimaryKeyStrategy.Assigned, false, null)]
    public void TheAttributeIsWrittenWhereItGivesTheMechanismTheConventionDoesNot(
        string type, PrimaryKeyStrategy strategy, bool attribute, ConversionRecordKind? record)
    {
        var builder = new EFCoreEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddProperty(type, "CustomerCode", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(strategy, "CustomerCode");

        var code = builder.Build().Single().Content;
        var records = builder.Records.Where(r => r.Category == MappingFactCategory.PrimaryKeyStrategy).ToList();

        if (attribute)
        {
            Assert.Contains("[DatabaseGenerated(DatabaseGeneratedOption.Identity)]", code);
        }
        else
        {
            Assert.DoesNotContain("[DatabaseGenerated", code);
        }

        if (record is { } kind)
        {
            Assert.Equal(kind, Assert.Single(records).Kind);
        }
        else
        {
            Assert.Empty(records);
        }
    }

    /// <summary>
    /// A part of a composite key the convention never generates, so an Identity on it has to be
    /// written down, or EF Core would have the application assign it over an IDENTITY column.
    /// </summary>
    [Fact]
    public void AnIdentityPartOfACompositeKeyIsWrittenDown()
    {
        var builder = new EFCoreEntityBuilder();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddTable("OrderLines");
        builder.AddProperty("int", "LineId", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("int", "OrderId", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey([("LineId", 1, PrimaryKeyStrategy.Identity), ("OrderId", 2, PrimaryKeyStrategy.Assigned)]);

        var code = builder.Build().Single().Content;

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(code, @"\[DatabaseGenerated\(DatabaseGeneratedOption\.Identity\)\]"));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "LineId");
    }

    private static EFCoreEntityBuilder ParsedWithConfiguration(string configuration, string entity)
    {
        var builder = new EFCoreEntityBuilder();
        var context = new EFCoreReadingContext();
        new EFCoreFluentConfigurationParser(builder, context).Parse(configuration);
        new EFCoreEntityParser(builder, context: context).Parse(entity);
        return builder;
    }

    private const string Order = """
        public class Order
        {
            public int Id { get; set; }
        }
        """;

    /// <summary>
    /// A call that names the mechanism on the key, or the model's default, is not read
    /// (decision 132), and the key under it claims no strategy: the convention's IDENTITY
    /// column is not what EF Core makes of it (decision 187). The call's name stays beside it.
    /// </summary>
    [Theory]
    [InlineData("modelBuilder.UseHiLo();", "UseHiLo")]
    [InlineData("modelBuilder.UseKeySequences();", "UseKeySequences")]
    [InlineData("modelBuilder.Entity<Order>().Property(o => o.Id).UseSequence();", "UseSequence")]
    [InlineData("modelBuilder.Entity<Order>().Property(o => o.Id).HasDefaultValueSql(\"NEXT VALUE FOR Numbers\");", "HasDefaultValueSql")]
    [InlineData("modelBuilder.Entity<Order>().Property(o => o.Id).UseIdentityColumn();", "UseIdentityColumn")]
    public void AKeyUnderAnUnreadMechanismClaimsNoStrategy(string statement, string call)
    {
        var builder = ParsedWithConfiguration($$"""
            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    {{statement}}
                }
            }
            """, Order);

        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Unspecified, part.Strategy);
        Assert.Equal(call, part.SourceStrategyName);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains(call));
    }

    /// <summary>Computed on a key is a model EF Core refuses, so nothing is read from it, and that is said.</summary>
    [Fact]
    public void ComputedOnAKeyIsNotRead()
    {
        var builder = Parse(Entity("    [Key, DatabaseGenerated(DatabaseGeneratedOption.Computed)]\r\n    public int CustomerID { get; set; }"));

        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Unspecified, part.Strategy);
        Assert.Equal("Computed", part.SourceStrategyName);
        Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.PrimaryKeyStrategy);
    }
}
