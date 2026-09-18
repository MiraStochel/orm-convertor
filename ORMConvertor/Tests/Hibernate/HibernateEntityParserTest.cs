using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using HibernateWrappers;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Tests.Hibernate;

/// <summary>
/// The reading half of decision 077: the jakarta.persistence annotation subset into the
/// intermediate representation, and the language axis of decision 076 - nullability by
/// type, the wrapper on @Id, the accessor's visibility - applied on the way in.
/// </summary>
public class HibernateEntityParserTest
{
    private static (DummyEntityBuilder Builder, IReadOnlyCollection<EntityMap> Read) Parse(string java)
    {
        var builder = new DummyEntityBuilder();
        var read = new HibernateEntityParser(builder, new JpaReadingContext()).Parse(java);
        return (builder, read);
    }

    private static EntityMap Single(string java) => Parse(java).Builder.EntityMaps.Single();

    private static PropertyMap Map(EntityMap map, string name) => map.PropertyMaps.Single(pm => pm.Property.Name == name);

    [Fact]
    public void PackageTableAndSchemaAreRead()
    {
        var map = Single("""
            package Shop;

            import jakarta.persistence.*;

            @Entity
            @Table(name = "Customers", schema = "Sales")
            public class Customer {
                @Id
                private Integer id;
            }
            """);

        Assert.Equal("Shop", map.Entity.Namespace);
        Assert.Equal("Customer", map.Entity.Name);
        Assert.Equal(AccessModifier.Public, map.Entity.AccessModifier);
        Assert.Equal("Customers", map.Table);
        Assert.Equal("Sales", map.Schema);
    }

    [Fact]
    public void ColumnFactsAreRead()
    {
        var map = Single("""
            import jakarta.persistence.*;
            import java.math.BigDecimal;

            @Entity
            public class Customer {
                @Id private Integer id;

                @Column(name = "CustomerName", length = 200, nullable = false)
                private String name;

                @Column(name = "CreditLimit", precision = 18, scale = 2)
                private BigDecimal creditLimit;
            }
            """);

        var name = Map(map, "name");
        Assert.Equal("CustomerName", name.ColumnName);
        Assert.Equal(200, name.Length);
        Assert.False(name.IsNullable);

        var limit = Map(map, "creditLimit");
        Assert.Equal(18, limit.Precision);
        Assert.Equal(2, limit.Scale);
        Assert.Null(limit.IsNullable); // nobody stated it
        Assert.Equal(ScalarType.Decimal, limit.Property.Type!.ScalarType);
    }

    /// <summary>
    /// Java's one axis of nullability: a primitive cannot be null and Hibernate derives
    /// NOT NULL from it (materialized per decision 067); a reference type is nullable
    /// unless the mapping states a NOT NULL column; a wrapper on @Id is the framework's
    /// idiom, not a domain claim (decision 077).
    /// </summary>
    [Fact]
    public void NullabilityFollowsTheTypeAndTheMapping()
    {
        var map = Single("""
            import jakarta.persistence.*;

            @Entity
            public class Book {
                @Id private Integer id;
                private int publishedYear;
                private String title;
                @Column(nullable = false) private String isbn;
                @Basic(optional = false) private String subtitle;
            }
            """);

        var id = Map(map, "id");
        Assert.False(id.Property.Type!.IsNullable);
        Assert.Equal(ScalarType.Int, id.Property.Type.ScalarType);

        var year = Map(map, "publishedYear");
        Assert.False(year.Property.Type!.IsNullable);
        Assert.False(year.IsNullable);

        var title = Map(map, "title");
        Assert.True(title.Property.Type!.IsNullable);
        Assert.Null(title.IsNullable);

        Assert.False(Map(map, "isbn").Property.Type!.IsNullable);
        Assert.False(Map(map, "isbn").IsNullable);
        Assert.False(Map(map, "subtitle").Property.Type!.IsNullable);
        Assert.False(Map(map, "subtitle").IsNullable);
    }

    [Theory]
    [InlineData("@GeneratedValue", PrimaryKeyStrategy.Auto)]
    [InlineData("@GeneratedValue(strategy = GenerationType.AUTO)", PrimaryKeyStrategy.Auto)]
    [InlineData("@GeneratedValue(strategy = GenerationType.IDENTITY)", PrimaryKeyStrategy.Identity)]
    [InlineData("@GeneratedValue(strategy = GenerationType.SEQUENCE)", PrimaryKeyStrategy.Sequence)]
    [InlineData("@GeneratedValue(strategy = GenerationType.TABLE)", PrimaryKeyStrategy.HiLo)]
    [InlineData("@GeneratedValue(strategy = GenerationType.UUID)", PrimaryKeyStrategy.Uuid)]
    [InlineData("", PrimaryKeyStrategy.Unspecified)]
    public void GenerationStrategyIsReadIntoTheVocabulary(string annotation, PrimaryKeyStrategy expected)
    {
        var map = Single($$"""
            import jakarta.persistence.*;

            @Entity
            public class Customer {
                @Id
                {{annotation}}
                private Long id;
            }
            """);

        var part = Assert.Single(map.PrimaryKey!.Parts);
        Assert.Equal(expected, part.Strategy);
        Assert.Equal("id", part.PropertyMap.Property.Name);
    }

    /// <summary>
    /// The canonical parameters of decision 020, read from @SequenceGenerator and
    /// @TableGenerator: allocationSize is the block size unchanged, and the table
    /// generator's key column and key value get their first producer.
    /// </summary>
    [Fact]
    public void GeneratorParametersAreCanonicalized()
    {
        var sequence = Single("""
            import jakarta.persistence.*;

            @Entity
            public class Customer {
                @Id
                @GeneratedValue(strategy = GenerationType.SEQUENCE, generator = "cust_gen")
                @SequenceGenerator(name = "cust_gen", sequenceName = "customer_seq", schema = "Sales", allocationSize = 20, initialValue = 100)
                private Long id;
            }
            """);

        var part = Assert.Single(sequence.PrimaryKey!.Parts);
        Assert.Equal("customer_seq", part.StrategyParameters[GeneratorParameter.SequenceName]);
        Assert.Equal("Sales", part.StrategyParameters[GeneratorParameter.Schema]);
        Assert.Equal("20", part.StrategyParameters[GeneratorParameter.BlockSize]);
        Assert.Equal("100", part.StrategyParameters[GeneratorParameter.InitialValue]);

        var table = Single("""
            import jakarta.persistence.*;

            @Entity
            @TableGenerator(name = "cust_tab", table = "Counters", pkColumnName = "Name", valueColumnName = "Next", pkColumnValue = "Customer", allocationSize = 10)
            public class Customer {
                @Id
                @GeneratedValue(strategy = GenerationType.TABLE, generator = "cust_tab")
                private Long id;
            }
            """);

        part = Assert.Single(table.PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.HiLo, part.Strategy);
        Assert.Equal("TABLE", part.SourceStrategyName);
        Assert.Equal("Counters", part.StrategyParameters[GeneratorParameter.CounterTable]);
        Assert.Equal("Name", part.StrategyParameters[GeneratorParameter.CounterKeyColumn]);
        Assert.Equal("Next", part.StrategyParameters[GeneratorParameter.CounterValueColumn]);
        Assert.Equal("Customer", part.StrategyParameters[GeneratorParameter.CounterKeyValue]);
        Assert.Equal("10", part.StrategyParameters[GeneratorParameter.BlockSize]);
    }

    [Fact]
    public void VersionAndTransientAreCarriedFacts()
    {
        var map = Single("""
            import jakarta.persistence.*;

            @Entity
            public class Customer {
                @Id private Integer id;
                @Version private Integer version;
                @Transient private String cache;
                private transient String scratch;
            }
            """);

        Assert.True(Map(map, "version").IsVersion);
        Assert.True(Map(map, "cache").IsTransient);
        Assert.True(Map(map, "scratch").IsTransient);
        Assert.Equal(4, map.Entity.Properties.Count);
    }

    /// <summary>
    /// @IdClass records the Mirrored form of decision 031; the nested static key class is
    /// a class of the conversion until the dissolution phase takes it out again.
    /// </summary>
    [Fact]
    public void IdClassBecomesAMirroredCompositeKey()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;
            import java.io.Serializable;

            @Entity
            @IdClass(OrderLine.OrderLineId.class)
            public class OrderLine {
                @Id private Integer orderId;
                @Id private Integer lineNo;
                private int quantity;

                public static class OrderLineId implements Serializable {
                    private Integer orderId;
                    private Integer lineNo;
                }
            }
            """);

        Assert.Equal(2, builder.EntityMaps.Count);

        builder.DissolveKeyClasses();

        var map = Assert.Single(builder.EntityMaps);
        Assert.Equal("OrderLine", map.Entity.Name);
        Assert.Equal(["orderId", "lineNo"], map.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.All(map.PrimaryKey.Parts, p => Assert.Equal(PrimaryKeyStrategy.Unspecified, p.Strategy));
        Assert.Equal(KeyClassForm.Mirrored, map.PrimaryKey.SourceKeyClass!.Form);
        Assert.Equal("OrderLineId", map.PrimaryKey.SourceKeyClass.ClassName);
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Conflict);
    }

    /// <summary>
    /// @EmbeddedId names no part: the claim waits for the embeddable class, whichever unit
    /// it comes in, and the dissolution phase materializes the Embedded form from its
    /// members (decision 077). The order of the units does not matter (S2).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmbeddedIdTakesItsPartsFromTheKeyClass(bool keyClassFirst)
    {
        const string entity = """
            import jakarta.persistence.*;

            @Entity
            public class OrderLine {
                @EmbeddedId private OrderLineId id;
                private int quantity;
            }
            """;

        const string keyClass = """
            import jakarta.persistence.*;
            import java.io.Serializable;

            @Embeddable
            public class OrderLineId implements Serializable {
                @Column(name = "OrderID") private Integer orderId;
                private Integer lineNo;
            }
            """;

        var builder = new DummyEntityBuilder();
        var parser = new HibernateEntityParser(builder, new JpaReadingContext());
        foreach (var unit in keyClassFirst ? new[] { keyClass, entity } : [entity, keyClass])
        {
            parser.Parse(unit);
        }

        builder.DissolveKeyClasses();

        var map = Assert.Single(builder.EntityMaps);
        Assert.Equal("OrderLine", map.Entity.Name);
        Assert.Equal(["orderId", "lineNo"], map.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.Equal(KeyClassForm.Embedded, map.PrimaryKey.SourceKeyClass!.Form);
        Assert.Equal("id", map.PrimaryKey.SourceKeyClass.PropertyName);
        Assert.Equal(ScalarType.Int, map.PrimaryKey.Parts[0].PropertyMap.Property.Type!.ScalarType);
        Assert.DoesNotContain(map.Entity.Properties, p => p.Name == "id");
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "id"); // the change of form
    }

    [Fact]
    public void AMissingEmbeddableClassIsReportedNotGuessed()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;

            @Entity
            public class OrderLine {
                @EmbeddedId private OrderLineId id;
            }
            """);

        builder.DissolveKeyClasses();

        var map = Assert.Single(builder.EntityMaps);
        Assert.Null(map.PrimaryKey);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Category == MappingFactCategory.PrimaryKey && r.Reason.Contains("OrderLineId"));
    }

    [Fact]
    public void ManyToOneWithJoinColumnIsAnOwningRelation()
    {
        var map = Single("""
            import jakarta.persistence.*;

            @Entity
            public class Book {
                @Id private Integer id;

                @ManyToOne(optional = false)
                @JoinColumn(name = "AuthorId", nullable = false)
                private Author author;
            }
            """);

        var relation = Assert.Single(map.Relations);
        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.Equal("Author", relation.TargetEntity);
        Assert.Equal("author", relation.SourceNavigationProperty);
        Assert.Equal(LangTypeCategory.Reference, Map(map, "author").Property.Type!.Category);
        Assert.False(Map(map, "author").IsNullable);
    }

    [Fact]
    public void OneToManyWithMappedByIsTheInverseSide()
    {
        var map = Single("""
            import jakarta.persistence.*;
            import java.util.Set;

            @Entity
            public class Author {
                @Id private Integer id;

                @OneToMany(mappedBy = "author")
                private Set<Book> books;
            }
            """);

        var relation = Assert.Single(map.Relations);
        Assert.Equal(Cardinality.OneToMany, relation.Cardinality);
        Assert.Equal(RelationRole.Inverse, relation.Role);
        Assert.Equal("Book", relation.TargetEntity);

        var type = Map(map, "books").Property.Type!;
        Assert.Equal(LangTypeCategory.Collection, type.Category);
        Assert.Equal(CollectionKind.Set, type.CollectionKind);
        Assert.Equal("Book", type.ElementType!.TargetEntity);
    }

    /// <summary>
    /// @ManyToMany with @JoinTable carries the junction facts the synthesis of decision
    /// 005 builds the junction entity from - the same road as NHibernate's many-to-many.
    /// </summary>
    [Fact]
    public void ManyToManyWithJoinTableSynthesizesTheJunctionEntity()
    {
        var builder = new DummyEntityBuilder();
        new HibernateEntityParser(builder, new JpaReadingContext()).Parse("""
            import jakarta.persistence.*;
            import java.util.List;

            @Entity
            @Table(name = "Books")
            public class Book {
                @Id @Column(name = "BookId") private Integer id;

                @ManyToMany
                @JoinTable(name = "BookCategories", joinColumns = @JoinColumn(name = "BookId"), inverseJoinColumns = @JoinColumn(name = "CategoryId"))
                private List<Category> categories;
            }

            @Entity
            @Table(name = "Categories")
            class Category {
                @Id @Column(name = "CategoryId") private Integer id;

                @ManyToMany(mappedBy = "categories")
                private List<Book> books;
            }
            """);

        // The synthesis runs inside Build; the dummy builder cannot generate, so the
        // relation is checked through the real one.
        var hibernate = new HibernateEntityBuilder();
        new HibernateEntityParser(hibernate, new JpaReadingContext()).Parse("""
            import jakarta.persistence.*;
            import java.util.List;

            @Entity
            @Table(name = "Books")
            public class Book {
                @Id @Column(name = "BookId") private Integer id;

                @ManyToMany
                @JoinTable(name = "BookCategories", joinColumns = @JoinColumn(name = "BookId"), inverseJoinColumns = @JoinColumn(name = "CategoryId"))
                private List<Category> categories;
            }

            @Entity
            @Table(name = "Categories")
            class Category {
                @Id @Column(name = "CategoryId") private Integer id;

                @ManyToMany(mappedBy = "categories")
                private List<Book> books;
            }
            """);

        var artifacts = hibernate.Build();

        Assert.Equal(3, artifacts.Count);
        var junction = hibernate.EntityMaps.Single(em => em.IsJunctionTable);
        Assert.Equal("BookCategories", junction.Table);
        Assert.Equal(2, junction.Relations.Count);
        Assert.Contains(artifacts, a => a.Content.Contains("public class BookCategorie"));
    }

    [Fact]
    public void UniqueConstraintsTranslateColumnsToProperties()
    {
        var map = Single("""
            import jakarta.persistence.*;

            @Entity
            @Table(name = "Products", uniqueConstraints = { @UniqueConstraint(name = "UQ_Sku", columnNames = { "Sku", "Supplier" }) })
            public class Product {
                @Id private Integer id;
                @Column(name = "Sku") private String sku;
                @Column(name = "Supplier") private String supplier;
                @Column(unique = true) private String barcode;
            }
            """);

        Assert.Equal(2, map.UniqueConstraints.Count);
        Assert.Contains(map.UniqueConstraints, c => c.PropertyNames.SequenceEqual(["barcode"]) && c.Name is null);
        Assert.Contains(map.UniqueConstraints, c => c.PropertyNames.SequenceEqual(["sku", "supplier"]) && c.Name == "UQ_Sku");
    }

    /// <summary>
    /// The accessor's visibility is the property's (decision 077): a private field behind
    /// public accessors is a public property, as every caller sees it; a field with no
    /// accessor at all is the whole property under field access and reads as a public one.
    /// </summary>
    [Fact]
    public void ThePropertyTakesTheAccessorsVisibility()
    {
        var map = Single("""
            import jakarta.persistence.*;

            @Entity
            public class Customer {
                @Id private Integer id;
                private String name;
                private String bare;
                private String guarded;

                public Integer getId() { return id; }
                public void setId(Integer id) { this.id = id; }
                public String getName() { return name; }
                protected String getGuarded() { return guarded; }
            }
            """);

        var id = Map(map, "id").Property;
        Assert.Equal(AccessModifier.Public, id.AccessModifier);
        Assert.True(id.HasGetter);
        Assert.True(id.HasSetter);

        var name = Map(map, "name").Property;
        Assert.Equal(AccessModifier.Public, name.AccessModifier);
        Assert.True(name.HasGetter);
        Assert.False(name.HasSetter);

        var bare = Map(map, "bare").Property;
        Assert.Equal(AccessModifier.Public, bare.AccessModifier);
        Assert.True(bare.HasGetter);
        Assert.True(bare.HasSetter);

        var guarded = Map(map, "guarded").Property;
        Assert.Equal(AccessModifier.Protected, guarded.AccessModifier);
        Assert.True(guarded.HasGetter);
        Assert.False(guarded.HasSetter);
    }

    /// <summary>
    /// @Id on a getter switches the class to property access: the annotations are read
    /// from the getters, and an annotation left on a field is ignored, as the provider
    /// ignores it (Jakarta Persistence 3.2 §2.3.1).
    /// </summary>
    [Fact]
    public void IdOnAGetterSelectsPropertyAccess()
    {
        var map = Single("""
            import jakarta.persistence.*;

            @Entity
            public class Customer {
                private Integer id;
                @Column(name = "Ignored") private String name;

                @Id
                @Column(name = "CustomerId")
                public Integer getId() { return id; }
                public void setId(Integer id) { this.id = id; }

                @Column(name = "CustomerName")
                public String getName() { return name; }
            }
            """);

        Assert.Equal("id", map.PrimaryKey!.Parts.Single().PropertyMap.Property.Name);
        Assert.Equal("CustomerId", Map(map, "id").ColumnName);
        Assert.Equal("CustomerName", Map(map, "name").ColumnName);
    }

    [Fact]
    public void NationalizedIsTheUnicodeFacetAndColumnDefinitionTheLiteralType()
    {
        var map = Single("""
            import jakarta.persistence.*;
            import org.hibernate.annotations.Nationalized;

            @Entity
            public class Customer {
                @Id private Integer id;

                @Nationalized
                @Column(name = "Name", length = 100)
                private String name;

                @Column(columnDefinition = "money")
                private java.math.BigDecimal balance;

                @Column(columnDefinition = "geography")
                private Object location;
            }
            """);

        Assert.True(Map(map, "name").IsUnicode);

        var balance = Map(map, "balance");
        Assert.Equal(DatabaseType.Decimal, balance.Type);
        Assert.Equal("money", balance.SourceSqlType);
        Assert.Equal(19, balance.Precision);

        var location = Map(map, "location");
        Assert.Null(location.Type);
        Assert.Equal("geography", location.SourceSqlType);
    }

    [Fact]
    public void AnnotationsWithoutAPlaceInTheModelAreReportedNotDropped()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;
            import org.hibernate.annotations.Formula;

            @Entity
            @Inheritance(strategy = InheritanceType.JOINED)
            public class Customer {
                @Id private Integer id;

                @Formula("(select count(*) from Orders o where o.CustomerId = id)")
                private Long orderCount;

                @Enumerated(EnumType.STRING)
                private String kind;

                private final int constant = 1;
            }
            """);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("@Inheritance"));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "orderCount" && r.Reason.Contains("@Formula"));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "kind" && r.Reason.Contains("@Enumerated"));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "constant" && r.Reason.Contains("'final'"));
    }

    [Fact]
    public void ASyntaxErrorRefusesTheUnitWithAPosition()
    {
        var (builder, read) = Parse("""
            public class Customer {
                private Integer id
            }
            """);

        Assert.Empty(read);
        var failure = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Failure, failure.Kind);
        Assert.Contains("line 3", failure.Reason);
    }

    /// <summary>
    /// The Java types with a counterpart in the neutral vocabulary (decision 071) and the
    /// unknown name that stays as written (decisions 014 and 075).
    /// </summary>
    [Fact]
    public void JavaTypesReadIntoTheNeutralVocabulary()
    {
        var map = Single("""
            import jakarta.persistence.*;
            import java.time.*;
            import java.util.UUID;

            @Entity
            public class Sample {
                @Id private UUID id;
                private LocalDate bornOn;
                private LocalTime opensAt;
                private OffsetDateTime seenAt;
                private Duration elapsed;
                private byte[] payload;
                private boolean active;
                private Instant createdAt;
            }
            """);

        Assert.Equal(ScalarType.Guid, Map(map, "id").Property.Type!.ScalarType);
        Assert.Equal(ScalarType.Date, Map(map, "bornOn").Property.Type!.ScalarType);
        Assert.Equal(ScalarType.TimeOfDay, Map(map, "opensAt").Property.Type!.ScalarType);
        Assert.Equal(ScalarType.DateTimeOffset, Map(map, "seenAt").Property.Type!.ScalarType);
        Assert.Equal(ScalarType.Duration, Map(map, "elapsed").Property.Type!.ScalarType);
        Assert.Equal(ScalarType.ByteArray, Map(map, "payload").Property.Type!.ScalarType);
        Assert.Equal(ScalarType.Bool, Map(map, "active").Property.Type!.ScalarType);
        Assert.False(Map(map, "active").Property.Type!.IsNullable);

        var created = Map(map, "createdAt").Property.Type!;
        Assert.Equal(LangTypeCategory.Unknown, created.Category);
        Assert.Equal("Instant", created.SourceName);
    }

    [Fact]
    public void AnInitializerTravelsOnlyAsASharedLiteral()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;
            import java.util.*;

            @Entity
            public class Sample {
                @Id private Integer id;
                private int count = 0;
                private String label = "none";
                private java.math.BigDecimal price = new BigDecimal("1.50");
                private long stamp = 5L;
                private List<String> tags = new ArrayList<>();
                private Date when = new Date();
            }
            """);

        var map = builder.EntityMaps.Single();
        Assert.Equal("0", Map(map, "count").Property.DefaultValue);
        Assert.Equal("\"none\"", Map(map, "label").Property.DefaultValue);
        Assert.Equal("1.50", Map(map, "price").Property.DefaultValue);
        Assert.Equal("5", Map(map, "stamp").Property.DefaultValue);
        Assert.Null(Map(map, "tags").Property.DefaultValue);
        Assert.Null(Map(map, "when").Property.DefaultValue);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "when" && r.Reason.Contains("new Date()"));
    }

    /// <summary>
    /// Decision 079 on the way in: secondPrecision is the fractional-second precision and
    /// fills the one Precision facet of the model; precision over a temporal attribute is
    /// refused, because the value does not reach the column in Hibernate either and
    /// reading it would put a column in the model that the source never had.
    /// </summary>
    [Fact]
    public void SecondPrecisionIsReadAndPrecisionOverATemporalAttributeIsNot()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;
            import java.time.LocalDateTime;
            import java.time.LocalTime;

            @Entity
            public class Stamp {
                @Id private Integer id;

                @Column(name = "SeenAt", secondPrecision = 3)
                private LocalDateTime seenAt;

                @Column(name = "PlacedAt", precision = 3)
                private LocalDateTime placedAt;

                @Column(name = "OpensAt", secondPrecision = 0)
                private LocalTime opensAt;
            }
            """);

        var map = builder.EntityMaps.Single();
        Assert.Equal(3, Map(map, "seenAt").Precision);
        Assert.Null(Map(map, "placedAt").Precision);
        Assert.Equal(0, Map(map, "opensAt").Precision);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss
            && r.Property == "placedAt"
            && r.Category == MappingFactCategory.PrecisionAndScale
            && r.Reason.Contains("secondPrecision"));
        Assert.DoesNotContain(builder.Records, r => r.Property is "seenAt" or "opensAt");
    }

    /// <summary>The symmetric half: secondPrecision on a column that is not temporal (decision 079).</summary>
    [Fact]
    public void SecondPrecisionOverADecimalAttributeIsALoss()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;
            import java.math.BigDecimal;

            @Entity
            public class Invoice {
                @Id private Integer id;

                @Column(name = "Total", precision = 18, scale = 2, secondPrecision = 3)
                private BigDecimal total;
            }
            """);

        var total = Map(builder.EntityMaps.Single(), "total");
        Assert.Equal(18, total.Precision);
        Assert.Equal(2, total.Scale);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss
            && r.Property == "total"
            && r.Category == MappingFactCategory.PrecisionAndScale
            && r.Reason.Contains("not a time or timestamp"));
    }

    /// <summary>
    /// The family the columnDefinition states outranks the language type in the same
    /// predicate (decision 079): a literal datetime2 is a temporal column whatever the
    /// declaration is called.
    /// </summary>
    [Fact]
    public void TheColumnDefinitionFamilyDecidesWhereItIsStated()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;

            @Entity
            public class Reading {
                @Id private Integer id;

                @Column(name = "TakenAt", precision = 3, columnDefinition = "datetime2(3)")
                private Instant takenAt;
            }
            """);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss
            && r.Property == "takenAt"
            && r.Category == MappingFactCategory.PrecisionAndScale);
    }

    [Fact]
    public void TheParserClaimsOnlyTheJavaEntityLanguage()
    {
        var parser = new HibernateEntityParser(new DummyEntityBuilder(), new JpaReadingContext());

        Assert.True(parser.CanParse(ConversionContentType.JavaEntity));
        Assert.False(parser.CanParse(ConversionContentType.CSharpEntity));
        Assert.False(parser.CanParse(ConversionContentType.XML));
    }
}
