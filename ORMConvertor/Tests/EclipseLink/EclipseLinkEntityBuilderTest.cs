using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EclipseLinkWrappers;
using Model;
using Model.AbstractRepresentation.Enums;

namespace Tests.EclipseLink;

/// <summary>
/// The writing half of decision 080. Everything the shared layer writes is already tested
/// over Hibernate, so what is asserted here is what the profile of the second
/// implementation changes: AUTO resolving to a counter table written out in full, national
/// character data as a literal type because EclipseLink has no annotation for it, and the
/// explicit style that keeps the upper-case default of this implementation out of the
/// output altogether.
/// </summary>
public class EclipseLinkEntityBuilderTest
{
    /// <summary>Builds and returns the entity artifact with the records the build wrote.</summary>
    private static (string Code, IReadOnlyList<ConversionRecord> Records) Build(Action<EclipseLinkEntityBuilder> populate)
    {
        var builder = new EclipseLinkEntityBuilder();
        populate(builder);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return (artifacts.First(a => a.ContentType == ConversionContentType.JavaEntity).Content, builder.Records);
    }

    private static void Customer(EclipseLinkEntityBuilder builder, PrimaryKeyStrategy strategy = PrimaryKeyStrategy.Identity)
    {
        builder.AddNamespace("Shop");
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddSchema("Sales");
        builder.AddProperty("int", "CustomerId", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("string", "CustomerName", "public", hasGetter: true, hasSetter: true);
        builder.SetPropertyDatabaseMapping("CustomerName", new() { ["length"] = "200", ["nullable"] = "false" });
        builder.AddProperty("decimal", "CreditLimit", "public", hasGetter: true, hasSetter: true, isNullable: true);
        builder.AddPrimaryKey(strategy, "CustomerId");
    }

    /// <summary>
    /// The shared layer writes the same explicit JPA artifact for both implementations -
    /// that is the claim of decision 076 that makes this wrapper thin, so it is asserted
    /// rather than assumed.
    /// </summary>
    [Fact]
    public void TheEntityIsTheSameExplicitJpaAsHibernateWrites()
    {
        var (code, _) = Build(b => Customer(b));

        Assert.StartsWith("package Shop;", code);
        Assert.Contains("import jakarta.persistence.Entity;", code);
        Assert.Contains("@Entity", code);
        Assert.Contains("@Table(name = \"Customers\", schema = \"Sales\")", code);
        Assert.Contains("public class Customer {", code);
        Assert.Contains("@Id", code);
        Assert.Contains("@GeneratedValue(strategy = GenerationType.IDENTITY)", code);
        Assert.Contains("@Column(name = \"CustomerId\")", code);
        Assert.Contains("private Integer CustomerId;", code);
        Assert.Contains("@Column(name = \"CustomerName\", length = 200, nullable = false)", code);
        Assert.Contains("public String getCustomerName() {", code);
        Assert.DoesNotContain("org.eclipse.persistence", code);
    }

    /// <summary>
    /// The heart of the profile (decision 080): the same silence that is a sequence under
    /// Hibernate is a counter table here, and the artifact says which one. Writing AUTO
    /// would have swapped one generator for another without a word.
    /// </summary>
    [Fact]
    public void AutoResolvesThroughTheProfileToTheCounterTableWrittenOut()
    {
        var (code, records) = Build(b => Customer(b, PrimaryKeyStrategy.Auto));

        Assert.Contains("@GeneratedValue(strategy = GenerationType.TABLE, generator = \"Customer_CustomerId_gen\")", code);
        Assert.Contains(
            "@TableGenerator(name = \"Customer_CustomerId_gen\", table = \"SEQUENCE\", pkColumnName = \"SEQ_NAME\", "
            + "valueColumnName = \"SEQ_COUNT\", pkColumnValue = \"SEQ_GEN\", allocationSize = 50)",
            code);
        Assert.DoesNotContain("AUTO", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention
            && r.Category == MappingFactCategory.PrimaryKeyStrategy
            && r.Reason.Contains("EclipseLink resolves it to TABLE"));
    }

    /// <summary>What the source states about the counter beats the implementation's default.</summary>
    [Fact]
    public void StatedCounterParametersWinOverTheProfile()
    {
        var (code, _) = Build(b =>
        {
            Customer(b, PrimaryKeyStrategy.HiLo);
            b.SetKeyStrategyDetails("CustomerId", parameters: new Dictionary<GeneratorParameter, string>
            {
                [GeneratorParameter.CounterTable] = "Counters",
                [GeneratorParameter.CounterValueColumn] = "NextHi",
                [GeneratorParameter.BlockSize] = "10",
            });
        });

        Assert.Contains("table = \"Counters\"", code);
        Assert.Contains("valueColumnName = \"NextHi\"", code);
        Assert.Contains("allocationSize = 10", code);

        // The columns the source left unstated still come from the profile, so the
        // generator names one whole counter rather than half of one.
        Assert.Contains("pkColumnName = \"SEQ_NAME\"", code);
        Assert.Contains("pkColumnValue = \"SEQ_GEN\"", code);
        Assert.DoesNotContain("table = \"SEQUENCE\"", code);
    }

    /// <summary>
    /// The second hook of decision 076 in its EclipseLink spelling: no annotation exists at
    /// any level, so the unicode facet reaches the column definition or nowhere.
    /// </summary>
    [Fact]
    public void NationalCharacterDataIsWrittenAsALiteralColumnType()
    {
        var (code, records) = Build(b =>
        {
            Customer(b);
            b.SetPropertyDatabaseType("CustomerName", DatabaseType.VarChar, isUnicode: true);
        });

        Assert.Contains("@Column(name = \"CustomerName\", length = 200, nullable = false, columnDefinition = \"nvarchar(200)\")", code);
        Assert.DoesNotContain("@Nationalized", code);
        Assert.DoesNotContain("import org.hibernate", code);
        Assert.DoesNotContain(records, r => r.Category == MappingFactCategory.DatabaseType);
    }

    /// <summary>
    /// A literal type overrides the length beside it, so a length nobody stated becomes a
    /// claim of the artifact and is reported (decision 080).
    /// </summary>
    [Fact]
    public void AnUnstatedLengthInsideTheLiteralTypeIsReported()
    {
        var (code, records) = Build(b =>
        {
            Customer(b);
            b.AddProperty("string", "Note", "public", hasGetter: true, hasSetter: true);
            b.SetPropertyDatabaseType("Note", DatabaseType.VarChar, isUnicode: true);
        });

        Assert.Contains("columnDefinition = \"nvarchar(255)\"", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention
            && r.Category == MappingFactCategory.Length
            && r.Property == "Note");
    }

    /// <summary>The literal type the source itself spelled wins, and is not written twice (decision 052).</summary>
    [Fact]
    public void ALiteralTypeFromTheSourceIsLeftAsItStands()
    {
        var (code, records) = Build(b =>
        {
            Customer(b);
            b.SetPropertyDatabaseType("CustomerName", DatabaseType.VarChar, isUnicode: true, sourceSqlType: "nvarchar(max)");
        });

        Assert.Contains("columnDefinition = \"nvarchar(max)\"", code);
        Assert.Equal(2, code.Split("columnDefinition").Length); // written once, not beside a derived one
        Assert.DoesNotContain(records, r => r.Category == MappingFactCategory.Length);
    }

    /// <summary>
    /// A unicode claim over a family with no national variant has nowhere to go: EclipseLink
    /// has no annotation and there is no type name to write, so the facet is dropped aloud.
    /// </summary>
    [Fact]
    public void AUnicodeClaimWithoutACharacterFamilyIsALoss()
    {
        var (code, records) = Build(b =>
        {
            Customer(b);
            b.SetPropertyDatabaseType("CreditLimit", DatabaseType.Decimal, isUnicode: true);
        });

        Assert.DoesNotContain("columnDefinition", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss
            && r.Category == MappingFactCategory.DatabaseType
            && r.Property == "CreditLimit"
            && r.Reason.Contains("literal column type"));
    }

    /// <summary>
    /// The enforced members are the specification's, so the composite key of decision 006
    /// comes out exactly as it does for Hibernate - EclipseLink adds nothing of its own.
    /// </summary>
    [Fact]
    public void ACompositeKeyCarriesTheKeyClassTheSpecificationDemands()
    {
        var (code, _) = Build(b =>
        {
            b.AddClassHeader("public", "OrderLine");
            b.AddTable("OrderLines");
            b.AddProperty("int", "OrderId", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("int", "LineNumber", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(
            [
                ("OrderId", 1, PrimaryKeyStrategy.Assigned),
                ("LineNumber", 2, PrimaryKeyStrategy.Assigned),
            ]);
        });

        Assert.Contains("@IdClass(OrderLine.OrderLineId.class)", code);
        Assert.Contains("public static class OrderLineId implements Serializable {", code);
        Assert.Contains("public boolean equals(Object obj) {", code);
        Assert.Contains("public int hashCode() {", code);
    }

    /// <summary>
    /// Every name is written out, which is what makes the artifact portable between the two
    /// implementations (decision 076) - and here it is also what keeps the upper-case
    /// default of EclipseLink from ever reaching a column.
    /// </summary>
    [Fact]
    public void NoNameIsLeftToTheUpperCaseDefaultOfTheImplementation()
    {
        var (code, records) = Build(b =>
        {
            b.AddNamespace("Shop");
            b.AddClassHeader("public", "Customer");
            b.AddProperty("int", "CustomerId", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("string", "BornOn", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(PrimaryKeyStrategy.Identity, "CustomerId");
        });

        // The table nobody stated is written from the entity name and reported; the column
        // names restate the default every framework of both ecosystems shares.
        Assert.Contains("@Table(name = \"Customer\")", code);
        Assert.Contains("@Column(name = \"BornOn\")", code);
        Assert.DoesNotContain("CUSTOMER", code);
        Assert.DoesNotContain("BORNON", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention
            && r.Category == MappingFactCategory.TableName
            && r.Reason.Contains("upper case"));
    }

    /// <summary>The records of this target name EclipseLink and its pinned release (decision 013).</summary>
    [Fact]
    public void TheDescriptorNamesEclipseLinkAndItsRelease()
    {
        var builder = new EclipseLinkEntityBuilder();

        Assert.Equal(ORMEnum.EclipseLink, builder.Descriptor.Framework);
        Assert.Equal("5.0.0", builder.Descriptor.Version);
    }
}
