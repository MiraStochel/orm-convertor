using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using SampleData;

namespace Tests.Hibernate;

/// <summary>
/// The writing half of decision 077: the intermediate representation as a Java entity in
/// the explicit JPA style of decision 076 - every table, column and strategy written out,
/// AUTO never - and the language axis on the way out.
/// </summary>
public class HibernateEntityBuilderTest
{
    /// <summary>Builds and returns the first entity artifact - the one entity most cases declare.</summary>
    private static (string Code, IReadOnlyList<ConversionRecord> Records) Build(Action<HibernateEntityBuilder> populate)
    {
        var builder = new HibernateEntityBuilder();
        populate(builder);
        var artifacts = builder.Build();

        Assert.Empty(builder.Records.Where(r => r.Kind == ConversionRecordKind.Failure));
        return (artifacts.First(a => a.ContentType == ConversionContentType.JavaEntity).Content, builder.Records);
    }

    private static void Customer(HibernateEntityBuilder builder, PrimaryKeyStrategy strategy = PrimaryKeyStrategy.Identity)
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

    [Fact]
    public void TheSampleMapBecomesAnExplicitJpaEntity()
    {
        var builder = new HibernateEntityBuilder();
        builder.EntityMap = CustomerSampleHibernate.Map;

        var code = builder.Build().Single(a => a.ContentType == ConversionContentType.JavaEntity).Content;

        Assert.StartsWith("package HibernateEntities;", code);
        Assert.Contains("import jakarta.persistence.Entity;", code);
        Assert.Contains("import java.math.BigDecimal;", code);
        Assert.Contains("@Entity", code);
        Assert.Contains("@Table(name = \"Customers\", schema = \"Sales\")", code);
        Assert.Contains("public class Customer {", code);
        Assert.Contains("@Id", code);
        Assert.Contains("@GeneratedValue(strategy = GenerationType.IDENTITY)", code);
        Assert.Contains("@Column(name = \"CustomerID\")", code);
        Assert.Contains("private Integer CustomerID;", code);
        Assert.Contains("@Column(name = \"CustomerName\", length = 200, nullable = false)", code);
        Assert.Contains("private String CustomerName;", code);
        Assert.Contains("@Column(name = \"CreditLimit\", precision = 18, scale = 2, nullable = true)", code);
        Assert.Contains("private BigDecimal CreditLimit;", code);
        Assert.Contains("public Integer getCustomerID() {", code);
        Assert.Contains("public void setCustomerID(Integer value) {", code);
        Assert.DoesNotContain("GenerationType.AUTO", code);
    }

    /// <summary>A C# int is a Java int, a nullable one an Integer, and an identifier always a wrapper (decision 077).</summary>
    [Fact]
    public void ScalarsRenderAsPrimitivesOrWrappersByNullability()
    {
        var (code, _) = Build(builder =>
        {
            Customer(builder);
            builder.AddProperty("int", "Count", "public", hasGetter: true, hasSetter: true);
            builder.AddProperty("int", "Optional", "public", hasGetter: true, hasSetter: true, isNullable: true);
            builder.AddProperty("bool", "Active", "public", hasGetter: true, hasSetter: true);
            builder.AddProperty("DateOnly", "BornOn", "public", hasGetter: true, hasSetter: true, isNullable: true);
        });

        Assert.Contains("private Integer CustomerId;", code);
        Assert.Contains("private int Count;", code);
        Assert.Contains("private Integer Optional;", code);
        Assert.Contains("private boolean Active;", code);
        Assert.Contains("public boolean isActive() {", code);
        Assert.Contains("private LocalDate BornOn;", code);
        Assert.Contains("import java.time.LocalDate;", code);
    }

    [Fact]
    public void AutoResolvesThroughTheProfileToAnExplicitSequence()
    {
        var (code, records) = Build(b => Customer(b, PrimaryKeyStrategy.Auto));

        Assert.Contains("@GeneratedValue(strategy = GenerationType.SEQUENCE, generator = \"Customer_CustomerId_gen\")", code);
        Assert.Contains("@SequenceGenerator(name = \"Customer_CustomerId_gen\", sequenceName = \"Customer_SEQ\", allocationSize = 50)", code);
        Assert.DoesNotContain("AUTO", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Category == MappingFactCategory.PrimaryKeyStrategy && r.Reason.Contains("Hibernate resolves it to Sequence"));
    }

    [Fact]
    public void AnUnstatedStrategyIsAssignedByConventionAndReported()
    {
        var (code, records) = Build(b => Customer(b, PrimaryKeyStrategy.Unspecified));

        Assert.DoesNotContain("@GeneratedValue", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Category == MappingFactCategory.PrimaryKeyStrategy && r.Reason.Contains("assigned by the application"));
    }

    [Fact]
    public void AssignedNeedsNoAnnotationAndNoRecord()
    {
        var (code, records) = Build(b => Customer(b, PrimaryKeyStrategy.Assigned));

        Assert.DoesNotContain("@GeneratedValue", code);
        Assert.DoesNotContain(records, r => r.Category == MappingFactCategory.PrimaryKeyStrategy);
    }

    /// <summary>The canonical parameters of decision 020 come back under the generator's own names.</summary>
    [Fact]
    public void SequenceAndTableGeneratorsCarryTheirParameters()
    {
        var (sequence, _) = Build(b =>
        {
            Customer(b, PrimaryKeyStrategy.Sequence);
            b.SetKeyStrategyDetails("CustomerId", parameters: new Dictionary<GeneratorParameter, string>
            {
                [GeneratorParameter.SequenceName] = "customer_seq",
                [GeneratorParameter.Schema] = "Sales",
                [GeneratorParameter.BlockSize] = "20",
                [GeneratorParameter.InitialValue] = "100",
            });
        });

        Assert.Contains("@GeneratedValue(strategy = GenerationType.SEQUENCE, generator = \"Customer_CustomerId_gen\")", sequence);
        Assert.Contains("@SequenceGenerator(name = \"Customer_CustomerId_gen\", sequenceName = \"customer_seq\", schema = \"Sales\", initialValue = 100, allocationSize = 20)", sequence);

        var (table, records) = Build(b =>
        {
            Customer(b, PrimaryKeyStrategy.HiLo);
            b.SetKeyStrategyDetails("CustomerId", parameters: new Dictionary<GeneratorParameter, string>
            {
                [GeneratorParameter.CounterTable] = "Counters",
                [GeneratorParameter.CounterValueColumn] = "NextHi",
                [GeneratorParameter.BlockSize] = "10",
            });
        });

        Assert.Contains("@GeneratedValue(strategy = GenerationType.TABLE, generator = \"Customer_CustomerId_gen\")", table);
        Assert.Contains("@TableGenerator(name = \"Customer_CustomerId_gen\", table = \"Counters\", valueColumnName = \"NextHi\", allocationSize = 10)", table);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("TABLE"));

        var (seqhilo, _) = Build(b =>
        {
            Customer(b, PrimaryKeyStrategy.HiLo);
            b.SetKeyStrategyDetails("CustomerId", parameters: new Dictionary<GeneratorParameter, string>
            {
                [GeneratorParameter.SequenceName] = "hibernate_sequence",
                [GeneratorParameter.BlockSize] = "10",
            });
        });

        Assert.Contains("GenerationType.SEQUENCE", seqhilo);
        Assert.Contains("sequenceName = \"hibernate_sequence\", allocationSize = 10", seqhilo);
    }

    [Fact]
    public void UuidAndIncrementFollowTheVocabulary()
    {
        var (uuid, _) = Build(b =>
        {
            b.AddClassHeader("public", "Token");
            b.AddProperty("Guid", "Id", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(PrimaryKeyStrategy.Uuid, "Id");
        });

        Assert.Contains("@GeneratedValue(strategy = GenerationType.UUID)", uuid);
        Assert.Contains("private UUID Id;", uuid);

        var (increment, records) = Build(b => Customer(b, PrimaryKeyStrategy.Increment));

        Assert.DoesNotContain("@GeneratedValue", increment);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("Increment"));
    }

    /// <summary>
    /// The key class of decision 006 in its JPA spelling: nested, static, serializable,
    /// with equals and hashCode, named after the source's key class where one was recorded.
    /// </summary>
    [Fact]
    public void ACompositeKeyGetsIdClassAndTheNestedKeyClass()
    {
        var (code, records) = Build(b =>
        {
            b.AddClassHeader("public", "OrderLine");
            b.AddTable("OrderLines");
            b.AddProperty("int", "OrderId", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("int", "LineNo", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("int", "Quantity", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey([("OrderId", 1, PrimaryKeyStrategy.Assigned), ("LineNo", 2, PrimaryKeyStrategy.Assigned)]);
        });

        Assert.Contains("@IdClass(OrderLine.OrderLineId.class)", code);
        Assert.Contains("public static class OrderLineId implements Serializable {", code);
        Assert.Contains("private Integer OrderId;", code);
        Assert.Contains("public OrderLineId() {", code);
        Assert.Contains("public OrderLineId(Integer OrderId, Integer LineNo) {", code);
        Assert.Contains("public boolean equals(Object obj) {", code);
        Assert.Contains("return obj instanceof OrderLineId other", code);
        Assert.Contains("&& Objects.equals(OrderId, other.OrderId)", code);
        Assert.Contains("public int hashCode() {", code);
        Assert.Contains("return Objects.hash(OrderId, LineNo);", code);
        Assert.Contains("import java.io.Serializable;", code);
        Assert.Contains("import java.util.Objects;", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("'OrderLineId' by convention"));

        var (named, namedRecords) = Build(b =>
        {
            b.AddClassHeader("public", "OrderLine");
            b.AddProperty("int", "OrderId", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("int", "LineNo", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(
                [("OrderId", 1, PrimaryKeyStrategy.Assigned), ("LineNo", 2, PrimaryKeyStrategy.Assigned)],
                new Model.AbstractRepresentation.SourceKeyClass("OrderLineKey", KeyClassForm.Mirrored));
        });

        Assert.Contains("@IdClass(OrderLine.OrderLineKey.class)", named);
        Assert.Contains("public static class OrderLineKey implements Serializable {", named);
        Assert.DoesNotContain(namedRecords, r => r.Reason.Contains("by convention"));
    }

    [Fact]
    public void VersionTransientAndUniqueConstraintsAreWritten()
    {
        var (code, _) = Build(b =>
        {
            Customer(b);
            b.AddProperty("int", "RowVersion", "public", hasGetter: true, hasSetter: true);
            b.SetPropertyDatabaseMapping("RowVersion", new() { ["version"] = "true" });
            b.AddProperty("string", "FullName", "public", hasGetter: true, hasSetter: true, isNullable: true);
            b.MarkTransient("FullName");
            b.AddUniqueConstraint("UQ_Name", ["CustomerName"]);
            b.AddUniqueConstraint(null, ["CustomerName", "CreditLimit"]);
        });

        Assert.Contains("@Version", code);
        Assert.Contains("@Transient\n    private String FullName;", code.Replace("\r\n", "\n"));
        Assert.DoesNotContain("@Column(name = \"FullName\"", code);
        Assert.Contains("uniqueConstraints = { @UniqueConstraint(name = \"UQ_Name\", columnNames = { \"CustomerName\" }), @UniqueConstraint(columnNames = { \"CustomerName\", \"CreditLimit\" }) }", code);
    }

    [Fact]
    public void ATableNobodyStatedIsWrittenAndReported()
    {
        var (code, records) = Build(b =>
        {
            b.AddClassHeader("public", "Customer");
            b.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");
        });

        Assert.Contains("@Table(name = \"Customer\")", code);
        Assert.Contains("@Column(name = \"Id\")", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Category == MappingFactCategory.TableName);
        Assert.DoesNotContain(records, r => r.Category == MappingFactCategory.ColumnName);
    }

    [Fact]
    public void NationalizedAndColumnDefinitionAreTheHibernateSpellings()
    {
        var (code, _) = Build(b =>
        {
            Customer(b);
            b.SetPropertyDatabaseType("CustomerName", DatabaseType.VarChar, isUnicode: true);
            b.SetPropertyDatabaseType("CreditLimit", DatabaseType.Decimal, sourceSqlType: "money");
        });

        Assert.Contains("import org.hibernate.annotations.Nationalized;", code);
        Assert.Contains("@Nationalized", code);
        Assert.Contains("columnDefinition = \"money\"", code);
    }

    /// <summary>
    /// The relations of decision 012 in JPA: the owning side carries @JoinColumn from the
    /// resolved pairs, the inverse side names the owning navigation through mappedBy.
    /// </summary>
    [Fact]
    public void RelationsRenderAsJoinColumnAndMappedBy()
    {
        var builder = new HibernateEntityBuilder();

        builder.AddClassHeader("public", "Author");
        builder.AddTable("Authors");
        builder.AddProperty("int", "AuthorId", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("List<Book>", "Books", "public", hasGetter: true, hasSetter: true, defaultValue: "[]");
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "AuthorId");
        builder.AddForeignKey(Cardinality.OneToMany, "Books", "Book");

        builder.BeginEntity();
        builder.AddClassHeader("public", "Book");
        builder.AddTable("Books");
        builder.AddProperty("int", "BookId", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("Author", "Author", "public", hasGetter: true, hasSetter: true, isNullable: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "BookId");
        builder.AddForeignKey(Cardinality.ManyToOne, "Author", "Author", RelationRole.Owning, ["AuthorId"]);

        var artifacts = builder.Build();
        var author = artifacts[0].Content;
        var book = artifacts[1].Content;

        Assert.Contains("@OneToMany(mappedBy = \"Author\")", author);
        Assert.Contains("private List<Book> Books = new ArrayList<>();", author);
        Assert.Contains("import java.util.ArrayList;", author);
        Assert.Contains("@ManyToOne", book);
        Assert.Contains("@JoinColumn(name = \"AuthorId\", referencedColumnName = \"AuthorId\")", book);
        Assert.Contains("private Author Author;", book);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void AUnidirectionalCollectionCarriesTheChildsKeyColumn()
    {
        var (code, _) = Build(b =>
        {
            b.AddClassHeader("public", "Customer");
            b.AddTable("Customers");
            b.AddProperty("int", "CustomerId", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("ISet<Order>", "Orders", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(PrimaryKeyStrategy.Identity, "CustomerId");
            b.AddForeignKey(Cardinality.OneToMany, "Orders", "Order", foreignKeyColumns: ["CustomerId"]);

            b.BeginEntity();
            b.AddClassHeader("public", "Order");
            b.AddTable("Orders");
            b.AddProperty("int", "OrderId", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("int", "CustomerId", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderId");
            b.EntityMap = b.EntityMaps[0];
        });

        Assert.Contains("@OneToMany\n    @JoinColumn(name = \"CustomerId\", referencedColumnName = \"CustomerId\")\n    private Set<Order> Orders = new HashSet<>();", code.Replace("\r\n", "\n"));
    }

    /// <summary>
    /// Modifiers translate rather than travel (decision 076): virtual is implicit in Java
    /// and required is a compile-time device of C#, so both drop in silence; a modifier
    /// with a meaning of its own and no counterpart is reported.
    /// </summary>
    [Fact]
    public void ModifiersWithoutACounterpartAreReported()
    {
        var (code, records) = Build(b =>
        {
            Customer(b);
            b.AddProperty("string", "Note", "public", ["virtual", "required"], hasGetter: true, hasSetter: true);
            b.AddProperty("string", "Kind", "public", ["abstract"], hasGetter: true, hasSetter: true);
        });

        Assert.Contains("private String Note;", code);
        Assert.DoesNotContain("virtual", code);
        Assert.DoesNotContain("required", code);
        Assert.DoesNotContain(records, r => r.Property == "Note" && r.Kind == ConversionRecordKind.Loss);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "Kind" && r.Reason.Contains("'abstract'"));
    }

    [Fact]
    public void InitializersAreTranslatedNotCopied()
    {
        var (code, records) = Build(b =>
        {
            Customer(b);
            b.AddProperty("int", "Count", "public", hasGetter: true, hasSetter: true, defaultValue: "0");
            b.AddProperty("decimal", "Price", "public", hasGetter: true, hasSetter: true, defaultValue: "1.5m");
            b.AddProperty("string", "Label", "public", hasGetter: true, hasSetter: true, defaultValue: "\"none\"");
            b.AddProperty("DateTime", "When", "public", hasGetter: true, hasSetter: true, defaultValue: "DateTime.Now");
        });

        Assert.Contains("private int Count = 0;", code);
        Assert.Contains("private BigDecimal Price = new BigDecimal(\"1.5\");", code);
        Assert.Contains("private String Label = \"none\";", code);
        Assert.Contains("private LocalDateTime When;", code);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "When" && r.Reason.Contains("DateTime.Now"));
    }

    /// <summary>
    /// Decision 079: the precision facet reaches the attribute its column family gives it.
    /// On a temporal column that is secondPrecision - Hibernate 7.4.5 ignores precision
    /// there and builds datetime2(7), which only the Java suite could see (§6.2).
    /// </summary>
    [Fact]
    public void ATemporalColumnStatesItsPrecisionAsSecondPrecision()
    {
        var (code, records) = Build(builder =>
        {
            Customer(builder);
            builder.AddProperty("DateTime", "PlacedAt", "public", hasGetter: true, hasSetter: true);
            builder.SetPropertyDatabaseType("PlacedAt", DatabaseType.Timestamp, precision: 3);
            builder.AddProperty("TimeOnly", "OpensAt", "public", hasGetter: true, hasSetter: true);
            builder.SetPropertyDatabaseType("OpensAt", DatabaseType.Time, precision: 0);
        });

        Assert.Contains("@Column(name = \"PlacedAt\", secondPrecision = 3)", code);
        Assert.Contains("@Column(name = \"OpensAt\", secondPrecision = 0)", code);
        Assert.DoesNotContain("precision = 3,", code);
        Assert.DoesNotContain("@Column(name = \"PlacedAt\", precision", code);

        // Renaming the spelling of a fact the source stated is not a convention (decision 079).
        Assert.DoesNotContain(records, r => r.Property is "PlacedAt" or "OpensAt");
    }

    /// <summary>
    /// Where the map states no family, the language type answers the same question - it is
    /// what the implementation itself consults when nothing names the SQL type (decision 079).
    /// </summary>
    [Fact]
    public void TheLanguageTypeDecidesWhereTheMapStatesNoFamily()
    {
        var (code, _) = Build(builder =>
        {
            Customer(builder);
            builder.AddProperty("DateTime", "SeenAt", "public", hasGetter: true, hasSetter: true);
            builder.SetPropertyDatabaseMapping("SeenAt", new() { ["precision"] = "6" });
        });

        Assert.Contains("@Column(name = \"SeenAt\", secondPrecision = 6)", code);
    }

    /// <summary>A decimal column is untouched by decision 079: precision and scale as before.</summary>
    [Fact]
    public void ADecimalColumnKeepsPrecisionAndScale()
    {
        var (code, records) = Build(builder =>
        {
            Customer(builder);
            builder.SetPropertyDatabaseType("CreditLimit", DatabaseType.Decimal, precision: 18, scale: 2);
        });

        Assert.Contains("@Column(name = \"CreditLimit\", precision = 18, scale = 2, nullable = true)", code);
        Assert.DoesNotContain("secondPrecision", code);
        Assert.DoesNotContain(records, r => r.Category == MappingFactCategory.PrecisionAndScale);
    }

    /// <summary>
    /// The two shapes for which @Column has no attribute at all: a date column has no
    /// fractional seconds, and a scale has no place on a temporal column (decision 079).
    /// </summary>
    [Fact]
    public void APrecisionWithNowhereToGoIsALoss()
    {
        var (code, records) = Build(builder =>
        {
            Customer(builder);
            builder.AddProperty("DateOnly", "BornOn", "public", hasGetter: true, hasSetter: true);
            builder.SetPropertyDatabaseType("BornOn", DatabaseType.Date, precision: 3);
            builder.AddProperty("DateTime", "ClosedAt", "public", hasGetter: true, hasSetter: true);
            builder.SetPropertyDatabaseType("ClosedAt", DatabaseType.Timestamp, precision: 3, scale: 2);
        });

        Assert.Contains("@Column(name = \"BornOn\")", code);
        Assert.Contains("@Column(name = \"ClosedAt\", secondPrecision = 3)", code);
        Assert.DoesNotContain("scale", code);

        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss
            && r.Property == "BornOn"
            && r.Category == MappingFactCategory.PrecisionAndScale
            && r.Reason.Contains("date column"));
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss
            && r.Property == "ClosedAt"
            && r.Category == MappingFactCategory.PrecisionAndScale
            && r.Reason.Contains("scale"));
    }

    [Fact]
    public void AnEntityWithoutAKeyIsRefused()
    {
        var builder = new HibernateEntityBuilder();
        builder.AddClassHeader("public", "Log");
        builder.AddProperty("string", "Message", "public", hasGetter: true, hasSetter: true);

        var artifacts = builder.Build();

        Assert.Empty(artifacts);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Category == MappingFactCategory.PrimaryKey);
    }
}
