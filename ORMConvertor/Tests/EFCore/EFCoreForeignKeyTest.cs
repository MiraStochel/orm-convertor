using EFCoreWrappers;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Tests.EFCore;

/// <summary>
/// EF Core names a foreign key by its properties, not its columns: [ForeignKey("A,B")] on the
/// navigation refers to members of the class. A source that carries only columns therefore leaves
/// the target short of something it needs, and the builder supplies it (decision 012).
/// </summary>
public class EFCoreForeignKeyTest
{
    private static IReadOnlyList<PropertyMap> TwoPartKeyOfOrderLine()
    {
        var builder = new EFCoreEntityBuilder();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddTable("OrderLines");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("long", "CompanyID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Assigned),
            ("CompanyID", 2, PrimaryKeyStrategy.Assigned),
        ]);

        return [.. builder.EntityMap.PrimaryKey!.Parts.Select(p => p.PropertyMap)];
    }

    private static EFCoreEntityBuilder Allocation()
    {
        var builder = new EFCoreEntityBuilder();
        builder.AddClassHeader("public", "OrderLineAllocation");
        builder.AddTable("OrderLineAllocations");
        builder.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Auto, "Id");

        // The navigation is typed by the target entity; the parser keeps the name as an
        // Unknown type and the builder writes it back as the source wrote it.
        builder.AddProperty("OrderLine", "OrderLine", "public", hasGetter: true, hasSetter: true);
        return builder;
    }

    /// <summary>
    /// A mapping that knows the column but has no property behind it - what a source expressing the
    /// foreign key in columns alone, such as NHibernate, leaves in the model.
    /// </summary>
    private static PropertyMap ColumnOnly(string column) => new()
    {
        Property = new Property { Name = column },
        ColumnName = column,
    };

    private static Relation RelationTo(IReadOnlyList<ColumnPair> pairs) => new()
    {
        Cardinality = Cardinality.ManyToOne,
        Role = RelationRole.Owning,
        SourceEntity = "OrderLineAllocation",
        TargetEntity = "OrderLine",
        SourceNavigationProperty = "OrderLine",
        ColumnPairs = pairs,
    };

    [Fact]
    public void ForeignKeyNamesThePropertiesTheEntityAlreadyHas()
    {
        var target = TwoPartKeyOfOrderLine();
        var builder = Allocation();
        builder.AddProperty("int", "OrderRef", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("long", "CompanyRef", "public", hasGetter: true, hasSetter: true);

        builder.AddRelation(RelationTo(
        [
            new ColumnPair
            {
                Source = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "OrderRef"),
                Target = target[0],
            },
            new ColumnPair
            {
                Source = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "CompanyRef"),
                Target = target[1],
            },
        ]));

        var code = builder.Build().Single().Content;

        // Both properties are there already, so nothing is invented, and their order is the order of
        // the key they point at.
        Assert.Contains("[ForeignKey(\"OrderRef,CompanyRef\")]", code);
        Assert.DoesNotContain("OrderLineOrderID", code);
    }

    [Fact]
    public void ForeignKeyKnownOnlyAsColumnsGetsPropertiesOfItsOwn()
    {
        var target = TwoPartKeyOfOrderLine();
        var builder = Allocation();

        builder.AddRelation(RelationTo(
        [
            new ColumnPair { Source = ColumnOnly("OrderId"), Target = target[0] },
            new ColumnPair { Source = ColumnOnly("CompanyId"), Target = target[1] },
        ]));

        var code = builder.Build().Single().Content;

        // The name is the navigation plus the key part, the type comes from the key it points at,
        // and the column keeps the name the source gave it.
        Assert.Contains("[Column(\"OrderId\")]", code);
        Assert.Contains("public int? OrderLineOrderID { get; set; }", code);
        Assert.Contains("[Column(\"CompanyId\")]", code);
        Assert.Contains("public long? OrderLineCompanyID { get; set; }", code);
        Assert.Contains("[ForeignKey(\"OrderLineOrderID,OrderLineCompanyID\")]", code);
    }

    [Fact]
    public void WithoutKnownColumnsNoForeignKeyIsWritten()
    {
        var builder = Allocation();
        builder.AddForeignKey(Cardinality.ManyToOne, "OrderLine", "OrderLine");

        // Nothing says which columns carry the key, so the annotation is left out and EF Core falls
        // back to its own convention rather than to a guess of ours.
        Assert.DoesNotContain("[ForeignKey", builder.Build().Single().Content);
    }
}