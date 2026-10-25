using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.EFCore;

/// <summary>
/// The key of an EF Core entity that is also its foreign key - the dependent of the shared key
/// of decision 012 - takes the principal's value, and EF Core generates nothing for it
/// (EFCoreValueGenerationProviderTest). Where the source states the foreign key, or a
/// one-to-one pairs it by convention, the key is read as Assigned, after the relation has
/// materialized; where only the convention of a many-to-one would find it, EF Core takes a
/// shadow property instead, so the relation goes without columns and the key stays Auto.
/// </summary>
public class EFCoreSharedKeyTest
{
    private const string Person = """
        public class Person
        {
            public int Id { get; set; }
        }
        """;

    public static TheoryData<string, string, string, string> KeysThatAreTheForeignKey => new()
    {
        {
            "[ForeignKey] on the navigation", Person, "Passport", """
            public class Passport
            {
                [Key]
                public int PersonId { get; set; }

                [ForeignKey(nameof(PersonId))]
                public Person Person { get; set; }
            }
            """
        },
        {
            "[ForeignKey] on the key", Person, "Passport", """
            public class Passport
            {
                [Key, ForeignKey(nameof(Person))]
                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """
        },
        {
            "[ForeignKey] on a key generated on add", Person, "Passport", """
            public class Passport
            {
                [Key, ForeignKey(nameof(Person)), DatabaseGenerated(DatabaseGeneratedOption.Identity)]
                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """
        },
        {
            "a lone reference answering back", """
            public class Citizen
            {
                public int Id { get; set; }

                public IdCard Card { get; set; }
            }
            """, "IdCard", """
            public class IdCard
            {
                [Key]
                public int CitizenId { get; set; }

                public Citizen Citizen { get; set; }
            }
            """
        },
        {
            "[InverseProperty] naming one of two references", """
            public class Captain
            {
                public int Id { get; set; }

                public Warrant Warrant { get; set; }

                public Warrant Spare { get; set; }
            }
            """, "Warrant", """
            public class Warrant
            {
                [Key]
                public int CaptainId { get; set; }

                [InverseProperty("Warrant")]
                public Captain Captain { get; set; }
            }
            """
        },
        {
            "a Guid key", """
            public class Holder
            {
                public Guid Id { get; set; }
            }
            """, "Badge", """
            public class Badge
            {
                [Key]
                public Guid HolderId { get; set; }

                [ForeignKey(nameof(HolderId))]
                public Holder Holder { get; set; }
            }
            """
        },
    };

    /// <summary>
    /// The dependent is parsed before its principal, so whether its key is the foreign key is
    /// known only once the conversion has every entity - the order the claim has to wait out.
    /// </summary>
    [Theory]
    [MemberData(nameof(KeysThatAreTheForeignKey))]
    public void AKeyThatIsTheForeignKeyIsAssigned(string form, string principal, string dependent, string source)
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);
        parser.Parse(source);
        parser.Parse(principal);

        builder.ResolveConventionNavigations();

        var entity = builder.EntityMaps.Single(em => em.Entity.Name == dependent);
        var part = Assert.Single(entity.PrimaryKey!.Parts);
        Assert.True(part.Strategy == PrimaryKeyStrategy.Assigned, $"{form}: {part.Strategy}");
        Assert.Null(part.SourceStrategyName);

        var relation = Assert.Single(entity.Relations);
        Assert.Equal([part.PropertyMap.Property.Name], builder.StatedForeignKeyColumns(relation));
    }

    /// <summary>
    /// The configuration states the foreign key with HasForeignKey - of a one-to-one through
    /// HasForeignKey&lt;T&gt;, of a many-to-one directly - and the key it names is assigned the same way.
    /// </summary>
    [Theory]
    [InlineData("modelBuilder.Entity<Passport>().HasOne(p => p.Person).WithOne().HasForeignKey<Passport>(p => p.PersonId);")]
    [InlineData("modelBuilder.Entity<Passport>().HasOne(p => p.Person).WithMany().HasForeignKey(p => p.PersonId);")]
    public void AKeyTheConfigurationNamesAsTheForeignKeyIsAssigned(string statement)
    {
        var builder = ParsedWithConfiguration(statement, """
            public class Passport
            {
                [Key]
                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """);

        var part = Assert.Single(builder.EntityMaps.Single(em => em.Entity.Name == "Passport").PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Assigned, part.Strategy);
    }

    /// <summary>
    /// The model's UseHiLo() turns what would be an IDENTITY key into hi/lo (decision 187), but
    /// not a key that is the foreign key: nothing generates that one, and the call's name, said
    /// of a key the model does not generate, goes with the strategy it stood beside.
    /// </summary>
    [Fact]
    public void AKeyThatIsTheForeignKeyIsAssignedUnderTheModelsHiLo()
    {
        var builder = ParsedWithConfiguration("modelBuilder.UseHiLo();", """
            public class Passport
            {
                [Key]
                public int PersonId { get; set; }

                [ForeignKey(nameof(PersonId))]
                public Person Person { get; set; }
            }
            """);

        var part = Assert.Single(builder.EntityMaps.Single(em => em.Entity.Name == "Passport").PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Assigned, part.Strategy);
        Assert.Null(part.SourceStrategyName);
    }

    public static TheoryData<string, string, string> KeysTheConventionDoesNotTake => new()
    {
        {
            "nothing answering back", Person, """
            public class Passport
            {
                [Key]
                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """
        },
        {
            "a collection answering back", """
            public class Person
            {
                public int Id { get; set; }

                public List<Passport> Passports { get; set; }
            }
            """, """
            public class Passport
            {
                [Key]
                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """
        },
        {
            "a later candidate matching", Person, """
            public class Passport
            {
                [Key]
                public int OwnerId { get; set; }

                public int PersonId { get; set; }

                public Person Owner { get; set; }
            }
            """
        },
    };

    /// <summary>
    /// A many-to-one whose foreign key only the convention would find takes no property that is
    /// the dependent's whole key: EF Core adds a shadow property beside it, so the relation goes
    /// without columns - the target says so, the catalog may supply them - and the key stays the
    /// IDENTITY column it is. The first name that matches decides; a later one is not tried.
    /// </summary>
    [Theory]
    [MemberData(nameof(KeysTheConventionDoesNotTake))]
    public void TheConventionTakesNoWholeKeyForTheForeignKeyOfAManyToOne(string form, string principal, string source)
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);
        parser.Parse(source);
        parser.Parse(principal);

        builder.ResolveConventionNavigations();

        var entity = builder.EntityMaps.Single(em => em.Entity.Name == "Passport");
        Assert.True(Assert.Single(entity.PrimaryKey!.Parts).Strategy == PrimaryKeyStrategy.Auto, form);

        var relation = Assert.Single(entity.Relations, r => r.TargetEntity == "Person");
        Assert.Null(builder.StatedForeignKeyColumns(relation));
        Assert.Empty(relation.ColumnPairs);
    }

    /// <summary>The configuration's many-to-one without HasForeignKey leaves the foreign key to the same convention.</summary>
    [Fact]
    public void AConfiguredManyToOneWithoutHasForeignKeyTakesNoWholeKeyEither()
    {
        var builder = ParsedWithConfiguration(
            "modelBuilder.Entity<Passport>().HasOne(p => p.Person).WithMany();", """
            public class Passport
            {
                [Key]
                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """);

        var entity = builder.EntityMaps.Single(em => em.Entity.Name == "Passport");
        Assert.Equal(PrimaryKeyStrategy.Auto, Assert.Single(entity.PrimaryKey!.Parts).Strategy);
        Assert.Null(builder.StatedForeignKeyColumns(Assert.Single(entity.Relations)));
    }

    /// <summary>A part of a composite key the convention does take for the foreign key, and the part stays Assigned.</summary>
    [Fact]
    public void APartOfACompositeKeyIsTakenForTheForeignKey()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);
        parser.Parse("""
            [PrimaryKey(nameof(PersonId), nameof(Stamp))]
            public class Visa
            {
                public int PersonId { get; set; }

                public int Stamp { get; set; }

                public Person Person { get; set; }
            }
            """);
        parser.Parse(Person);

        builder.ResolveConventionNavigations();

        var entity = builder.EntityMaps.Single(em => em.Entity.Name == "Visa");
        Assert.Equal(["PersonId"], builder.StatedForeignKeyColumns(Assert.Single(entity.Relations)));
        Assert.All(entity.PrimaryKey!.Parts, p => Assert.Equal(PrimaryKeyStrategy.Assigned, p.Strategy));
    }

    private const string Passport = """
        public class Passport
        {
            [Key]
            public int PersonId { get; set; }

            [ForeignKey(nameof(PersonId))]
            public Person Person { get; set; }
        }
        """;

    /// <summary>
    /// The assigned key reaches every target as the shared key it is: NHibernate assigns it
    /// instead of taking it natively, Hibernate leaves @GeneratedValue out, and EF Core writes
    /// no [DatabaseGenerated] - nothing generates the key there either.
    /// </summary>
    [Fact]
    public void TheSharedKeyLeavesAsAssignedInEveryTarget()
    {
        var nhibernate = Translated(new NHibernateEntityBuilder()).Single(o =>
            o.ContentType == ConversionContentType.XML && o.Content.Contains("<class name=\"Passport\""));
        Assert.Contains("<generator class=\"assigned\" />", nhibernate.Content);

        var hibernate = Translated(new HibernateEntityBuilder()).Single(o => o.Content.Contains("class Passport"));
        Assert.DoesNotContain("@GeneratedValue", hibernate.Content);

        var efCoreBuilder = new EFCoreEntityBuilder();
        var efCore = Translated(efCoreBuilder).Single(o => o.Content.Contains("class Passport"));
        Assert.DoesNotContain("[DatabaseGenerated", efCore.Content);
        Assert.Contains("[ForeignKey(\"PersonId\")]", efCore.Content);
        Assert.DoesNotContain(efCoreBuilder.Records, r => r.Category == MappingFactCategory.PrimaryKeyStrategy);
    }

    /// <summary>
    /// The EF Core builder knows the fact too: a single key the artifact names as a navigation's
    /// foreign key is not generated by convention, so Assigned needs no [DatabaseGenerated(None)],
    /// and a generated mechanism over it is a loss that says why - neither the convention nor the
    /// attribute generates such a key.
    /// </summary>
    [Theory]
    [InlineData("assigned", null)]
    [InlineData("native", ConversionRecordKind.Loss)]
    public void TheEFCoreBuilderGeneratesNothingForAKeyThatIsTheForeignKey(string generator, ConversionRecordKind? record)
    {
        var builder = new EFCoreEntityBuilder();
        var classes = new NHibernateEntityParser(builder);
        classes.Parse("""
            public class Passport
            {
                public virtual int PersonId { get; set; }

                public virtual Person Person { get; set; }
            }
            """);
        classes.Parse("""
            public class Person
            {
                public virtual int Id { get; set; }
            }
            """);

        var parser = new NHibernateXMLMappingParser(builder);
        parser.Parse($"""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Passport" table="Passports">
                    <id name="PersonId" column="PersonId" type="int">
                        <generator class="{generator}" />
                    </id>
                    <many-to-one name="Person" class="Person" column="PersonId" insert="false" update="false" />
                </class>
            </hibernate-mapping>
            """);
        parser.Parse("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Person" table="People">
                    <id name="Id" column="Id" type="int">
                        <generator class="identity" />
                    </id>
                </class>
            </hibernate-mapping>
            """);

        var code = builder.Build().Single(o => o.Content.Contains("class Passport")).Content;

        Assert.Contains("[ForeignKey(\"PersonId\")]", code);
        Assert.DoesNotContain("[DatabaseGenerated", code);

        var records = builder.Records.Where(r => r.Category == MappingFactCategory.PrimaryKeyStrategy && r.Entity == "Passport").ToList();

        if (record is { } kind)
        {
            var single = Assert.Single(records);
            Assert.Equal(kind, single.Kind);
            Assert.Contains("foreign key of the navigation 'Person'", single.Reason);
        }
        else
        {
            Assert.Empty(records);
        }
    }

    private static List<ConversionSource> Translated(AbstractEntityBuilder builder)
    {
        var parser = new EFCoreEntityParser(builder);
        parser.Parse(Passport);
        parser.Parse(Person);
        return builder.Build();
    }

    private static EFCoreEntityBuilder ParsedWithConfiguration(string statement, string entity)
    {
        var builder = new EFCoreEntityBuilder();
        var context = new EFCoreReadingContext();
        new EFCoreFluentConfigurationParser(builder, context).Parse($$"""
            public class TravelContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    {{statement}}
                }
            }
            """);

        var parser = new EFCoreEntityParser(builder, context: context);
        parser.Parse(entity);
        parser.Parse(Person);
        builder.ResolveConventionNavigations();
        return builder;
    }
}
