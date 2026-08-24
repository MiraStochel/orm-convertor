using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The language side of a property under the source-precedence rule of decision 017, and the
/// fate of a mapping fact the representation has no place for. Both are shapes nobody's
/// parser produces today - one framework, one artifact per fact - and both are written down
/// before the first framework that does, because that is precisely when they stop being
/// findable by trying (decisions 048 and 049).
/// </summary>
public class LanguageFactPrecedenceTest
{
    private static NHibernateEntityBuilder WithEntity()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Product");
        builder.AddTable("Products");
        return builder;
    }

    [Fact]
    public void ASecondDeclarationOfTheSamePropertyDoesNotDuplicateIt()
    {
        var builder = WithEntity();
        builder.AddProperty("string", "Name", "public", hasGetter: true);
        builder.AddProperty("string", "Name", "public", hasSetter: true);

        Assert.Single(builder.EntityMap.Entity.Properties, p => p.Name == "Name");
        Assert.Single(builder.EntityMap.PropertyMaps, pm => pm.Property.Name == "Name");
    }

    [Fact]
    public void ADeclarationFillsWhatOnlyTheMappingKnew()
    {
        // The mapping names the property first, so it exists with no language facts at all;
        // the class declaring it afterwards fills them in rather than adding a second one.
        var builder = WithEntity();
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["column"] = "ProductName" });
        builder.AddProperty("string", "Name", "public", ["virtual"], hasGetter: true, hasSetter: true);

        var property = Assert.Single(builder.EntityMap.Entity.Properties, p => p.Name == "Name");

        Assert.Equal(ScalarType.String, property.Type!.ScalarType);
        Assert.Equal(AccessModifier.Public, property.AccessModifier);
        Assert.Contains("virtual", property.OtherModifiers);
        Assert.True(property.HasGetter);
        Assert.True(property.HasSetter);

        // The column the mapping stated is still on the one map the property has.
        var map = Assert.Single(builder.EntityMap.PropertyMaps, pm => pm.Property.Name == "Name");
        Assert.Equal("ProductName", map.ColumnName);

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void ASecondDeclarationAddsAnAccessorAndAModifierWithoutRemovingAny()
    {
        var builder = WithEntity();
        builder.AddProperty("string", "Name", "public", hasGetter: true);
        builder.AddProperty("string", "Name", "public", ["virtual"], hasSetter: true, defaultValue: "\"\"");

        var property = builder.EntityMap.Entity.Properties.Single(p => p.Name == "Name");

        // Getter and setter are positive-only: the second declaration may add one, never
        // take one away.
        Assert.True(property.HasGetter);
        Assert.True(property.HasSetter);
        Assert.Contains("virtual", property.OtherModifiers);
        Assert.Equal("\"\"", property.DefaultValue);

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void ADifferingLaterClaimKeepsTheFirstValueAndLeavesAConflict()
    {
        var builder = WithEntity();
        builder.AddProperty("string", "Name", "public");
        builder.AddProperty("int", "Name", "private");

        var property = builder.EntityMap.Entity.Properties.Single(p => p.Name == "Name");

        Assert.Equal(ScalarType.String, property.Type!.ScalarType);
        Assert.Equal(AccessModifier.Public, property.AccessModifier);

        var conflicts = builder.Records.Where(r => r.Kind == ConversionRecordKind.Conflict).ToList();
        Assert.Equal(2, conflicts.Count);
        Assert.All(conflicts, r => Assert.Equal("Name", r.Property));
    }

    [Fact]
    public void TwoIdenticalDeclarationsAreNoConflict()
    {
        // Language types are built per parse, so comparing them by reference would call two
        // identical declarations a disagreement.
        var builder = WithEntity();
        builder.AddProperty("List<string>", "Tags", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("List<string>", "Tags", "public", hasGetter: true, hasSetter: true);

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void AMappingFactWithNoPlaceInTheModelIsReportedAsALoss()
    {
        var builder = WithEntity();
        builder.AddProperty("string", "Name", "public", hasGetter: true, hasSetter: true);

        // A fact no branch of the vocabulary knows used to land in a free dictionary on
        // PropertyMap that nobody ever read, so it died at emission without a word.
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string> { ["formula"] = "UPPER(Name)" });

        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal("Name", record.Property);
        Assert.Null(record.Category);
        Assert.Contains("formula", record.Reason, StringComparison.Ordinal);
        Assert.Contains("UPPER(Name)", record.Reason, StringComparison.Ordinal);
    }
}
