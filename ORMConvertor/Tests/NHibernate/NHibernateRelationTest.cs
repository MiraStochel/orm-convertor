using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// What the builder writes for a relation follows the shape of the columns, not the multiplicity:
/// &lt;one-to-one&gt; carries no column at all, so the owning side of a 1:1 with its own key is a
/// &lt;many-to-one unique="true"&gt; (decision 012).
///
/// Navigation properties are declared as object here. The type model has no value for an entity
/// type, so a navigation typed by the target entity cannot be added at all - the same blocker that
/// holds up the key class. Only the mapping is asserted, where the language type plays no part.
/// </summary>
public class NHibernateRelationTest
{
    private static string MappingOf(NHibernateEntityBuilder builder)
        => builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

    private static NHibernateEntityBuilder Entity(string name, string table)
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", name);
        builder.AddTable(table);
        builder.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");
        return builder;
    }

    /// <summary>
    /// A separate entity with a two-part key, built only to give the pairs something to point at.
    /// </summary>
    private static IReadOnlyList<PropertyMap> TwoPartKeyOf(string name)
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", name);
        builder.AddTable(name + "s");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("long", "CompanyID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Assigned),
            ("CompanyID", 2, PrimaryKeyStrategy.Assigned),
        ]);

        return [.. builder.EntityMap.PrimaryKey!.Parts.Select(p => p.PropertyMap)];
    }

    [Fact]
    public void ManyToOneWritesEveryColumnOfFheForeignKey()
    {
        var target = TwoPartKeyOf("OrderLine");
        var builder = Entity("OrderLineAllocation", "OrderLineAllocations");
        builder.AddProperty("int", "OrderRef", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("long", "CompanyRef", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("object", "OrderLine", "public", hasGetter: true, hasSetter: true);
        builder.SetPropertyDatabaseMapping("OrderRef", new Dictionary<string, string> { ["column"] = "OrderId" });
        builder.SetPropertyDatabaseMapping("CompanyRef", new Dictionary<string, string> { ["column"] = "CompanyId" });

        builder.AddRelation(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "OrderLineAllocation",
            TargetEntity = "OrderLine",
            SourceNavigationProperty = "OrderLine",
            ColumnPairs =
            [
                new ColumnPair { Source = ColumnOf(builder, "OrderRef"), Target = target[0] },
                new ColumnPair { Source = ColumnOf(builder, "CompanyRef"), Target = target[1] },
            ],
        });

        // More than one column means the nested form; the order is the one the pairs were stored
        // in, because it has to match the key they point at (decision 012).
        var xml = MappingOf(builder);
        Assert.Contains("<many-to-one name=\"OrderLine\" class=\"OrderLine\">", xml);
        Assert.Contains("<column name=\"OrderId\" />", xml);
        Assert.Contains("<column name=\"CompanyId\" />", xml);
        Assert.Contains("</many-to-one>", xml);
        Assert.True(xml.IndexOf("OrderId") < xml.IndexOf("CompanyId"));
    }

    [Fact]
    public void ManyToOneWithoutKnownColumnsNamesNone()
    {
        var builder = Entity("CustomerTransaction", "CustomerTransactions");
        builder.AddProperty("object", "Owner", "public", hasGetter: true, hasSetter: true);
        builder.AddForeignKey(Cardinality.ManyToOne, "Owner", "Customer");

        // Until the columns are resolved we name none: writing the navigation property name as a
        // column, which is what we used to do, states something the source never said.
        Assert.Contains("<many-to-one name=\"Owner\" class=\"Customer\" />", MappingOf(builder));
    }

    [Fact]
    public void OwningSideOfAOneToOneIsAUniqueManyToOne()
    {
        var target = TwoPartKeyOf("CustomerProfile");
        var builder = Entity("Customer", "Customers");
        builder.AddProperty("int", "ProfileRef", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("object", "Profile", "public", hasGetter: true, hasSetter: true);
        builder.SetPropertyDatabaseMapping("ProfileRef", new Dictionary<string, string> { ["column"] = "ProfileID" });

        builder.AddRelation(new Relation
        {
            Cardinality = Cardinality.OneToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Customer",
            TargetEntity = "CustomerProfile",
            SourceNavigationProperty = "Profile",
            ColumnPairs = [new ColumnPair { Source = ColumnOf(builder, "ProfileRef"), Target = target[0] }],
        });

        // The side holding the key needs an element that admits a column, and uniqueness is what
        // makes that element a 1:1 rather than an N:1.
        Assert.Contains(
            "<many-to-one name=\"Profile\" class=\"CustomerProfile\" column=\"ProfileID\" unique=\"true\" />",
            MappingOf(builder));
    }

    [Fact]
    public void SideWithoutAForeignKeyIsAPlainOneToOne()
    {
        var builder = Entity("Customer", "Customers");
        builder.AddProperty("object", "Profile", "public", hasGetter: true, hasSetter: true);

        builder.AddRelation(new Relation
        {
            Cardinality = Cardinality.OneToOne,
            Role = RelationRole.Inverse,
            SourceEntity = "Customer",
            TargetEntity = "CustomerProfile",
            SourceNavigationProperty = "Profile",
        });

        Assert.Contains("<one-to-one name=\"Profile\" class=\"CustomerProfile\" />", MappingOf(builder));
    }

    [Fact]
    public void InverseOneToOneNamesTheCounterpartNavigationAsPropertyRef()
    {
        var builder = Entity("Customer", "Customers");
        builder.AddForeignKey(Cardinality.OneToOne, "Profile", "CustomerProfile", RelationRole.Inverse);

        builder.BeginEntity();
        builder.AddClassHeader("public", "CustomerProfile");
        builder.AddTable("CustomerProfiles");
        builder.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");
        builder.AddForeignKey(Cardinality.OneToOne, "Owner", "Customer", RelationRole.Owning);

        // The attribute names the property of the owning entity that holds the key - the
        // counterpart navigation, reachable once the resolution phase has run (decision 012).
        var xmls = builder.Build()
            .Where(o => o.ContentType == ConversionContentType.XML)
            .Select(o => o.Content)
            .ToList();
        Assert.Contains(
            "<one-to-one name=\"Profile\" class=\"CustomerProfile\" property-ref=\"Owner\" />",
            xmls.Single(x => x.Contains("<class name=\"Customer\"")));
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness);
    }

    [Fact]
    public void ParentOfASharedKeyOneToOneWritesNoPropertyRef()
    {
        var builder = Entity("Customer", "Customers");
        builder.AddForeignKey(Cardinality.OneToOne, "Profile", "CustomerProfile", RelationRole.Inverse);

        builder.BeginEntity();
        builder.AddClassHeader("public", "CustomerProfile");
        builder.AddTable("CustomerProfiles");
        builder.AddProperty("int", "CustomerID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "CustomerID");
        builder.SetKeyStrategyDetails(
            "CustomerID",
            sourceStrategyName: "foreign",
            sourceParameters: new Dictionary<string, string> { ["property"] = "Owner" });
        builder.AddForeignKey(Cardinality.OneToOne, "Owner", "Customer", RelationRole.Owning);

        // The counterpart takes its identity from this entity, so the association joins over
        // the primary keys - exactly what a bare <one-to-one> says. No attribute, no record.
        var xmls = builder.Build()
            .Where(o => o.ContentType == ConversionContentType.XML)
            .Select(o => o.Content)
            .ToList();
        Assert.Contains(
            "<one-to-one name=\"Profile\" class=\"CustomerProfile\" />",
            xmls.Single(x => x.Contains("<class name=\"Customer\"")));
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness);
    }

    [Fact]
    public void InverseOneToOneWithoutACounterpartIsReported()
    {
        var builder = Entity("Customer", "Customers");
        builder.AddForeignKey(Cardinality.OneToOne, "Profile", "CustomerProfile", RelationRole.Inverse);

        builder.BeginEntity();
        builder.AddClassHeader("public", "CustomerProfile");
        builder.AddTable("CustomerProfiles");
        builder.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");

        // The target takes part in the conversion but nothing owns the key back here, so the
        // bare <one-to-one> claims a shared-key join the inverse role never asserted - the
        // omission is recorded rather than passed off as silence (decision 012).
        var xmls = builder.Build()
            .Where(o => o.ContentType == ConversionContentType.XML)
            .Select(o => o.Content)
            .ToList();
        Assert.Contains(
            "<one-to-one name=\"Profile\" class=\"CustomerProfile\" />",
            xmls.Single(x => x.Contains("<class name=\"Customer\"")));

        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness);
        Assert.Equal("Customer", record.Entity);
        Assert.Equal("Profile", record.Property);
        Assert.Contains("property-ref", record.Reason);
    }

    [Fact]
    public void SharedPrimaryKeyIsWrittenAsAConstrainedOneToOne()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "CustomerProfile");
        builder.AddTable("CustomerProfiles");
        builder.AddProperty("int", "CustomerID", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("object", "Customer", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "CustomerID");
        builder.SetKeyStrategyDetails(
            "CustomerID",
            sourceStrategyName: "foreign",
            sourceParameters: new Dictionary<string, string> { ["property"] = "Customer" });

        builder.AddRelation(new Relation
        {
            Cardinality = Cardinality.OneToOne,
            Role = RelationRole.Owning,
            SourceEntity = "CustomerProfile",
            TargetEntity = "Customer",
            SourceNavigationProperty = "Customer",
        });

        // The entity owns the relation, yet has no column of its own: its identity is the foreign
        // key. That is exactly what the foreign generator says, and it says it locally.
        var xml = MappingOf(builder);
        Assert.Contains("<one-to-one name=\"Customer\" class=\"Customer\" constrained=\"true\" />", xml);

        // The generator itself comes back too: assigned in its place would stop describing the
        // shared key the constrained one-to-one claims (decision 021), and the escape-path
        // parameters travel verbatim (decision 020), so the property survives with it.
        Assert.Contains("<generator class=\"foreign\">", xml);
        Assert.Contains("<param name=\"property\">Customer</param>", xml);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.PrimaryKeyStrategy);
    }

    [Fact]
    public void CollectionKeyWritesTheColumnsItKnows()
    {
        var target = TwoPartKeyOf("OrderLine");
        var builder = Entity("Order", "Orders");
        builder.AddProperty("int", "OrderRef", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("long", "CompanyRef", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("List<OrderLine>", "OrderLines", "public", hasGetter: true, hasSetter: true);
        builder.SetPropertyDatabaseMapping("OrderRef", new Dictionary<string, string> { ["column"] = "OrderId" });
        builder.SetPropertyDatabaseMapping("CompanyRef", new Dictionary<string, string> { ["column"] = "CompanyId" });

        builder.AddRelation(new Relation
        {
            Cardinality = Cardinality.OneToMany,
            Role = RelationRole.Inverse,
            SourceEntity = "Order",
            TargetEntity = "OrderLine",
            SourceNavigationProperty = "OrderLines",
            ColumnPairs =
            [
                new ColumnPair { Source = ColumnOf(builder, "OrderRef"), Target = target[0] },
                new ColumnPair { Source = ColumnOf(builder, "CompanyRef"), Target = target[1] },
            ],
        });

        // The key of a collection used to be the first part of the parent key, which was wrong in
        // both respects: wrong table and one column short.
        var xml = MappingOf(builder);
        Assert.Contains("<key>", xml);
        Assert.Contains("<column name=\"OrderId\" />", xml);
        Assert.Contains("<column name=\"CompanyId\" />", xml);
        Assert.Contains("</key>", xml);
    }

    private static PropertyMap ColumnOf(NHibernateEntityBuilder builder, string propertyName)
        => builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == propertyName);
}