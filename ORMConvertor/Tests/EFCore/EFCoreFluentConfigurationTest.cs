using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;

namespace Tests.EFCore;

/// <summary>
/// EF Core's fluent configuration is read as a bounded syntactic subset, before the class and
/// its annotations (decision 132): what it states fills the model first, an annotation that
/// says otherwise is a conflict and the configuration stands, and the conventions the class
/// parser materializes speak only where neither did. Whatever lies outside the subset is a
/// loss record at the statement or the call that says so.
/// </summary>
public class EFCoreFluentConfigurationTest
{
    /// <summary>The two entity parsers of the EF Core row, in its order, over every unit.</summary>
    private static EFCoreEntityBuilder Parsed(params string[] units)
    {
        var builder = new EFCoreEntityBuilder();
        var context = new EFCoreReadingContext();
        var fluent = new EFCoreFluentConfigurationParser(builder, context);
        var classes = new EFCoreEntityParser(builder, context: context);

        foreach (var unit in units)
        {
            fluent.Parse(unit);
        }

        foreach (var unit in units)
        {
            classes.Parse(unit);
        }

        builder.Build();
        return builder;
    }

    private static EntityMap Entity(EFCoreEntityBuilder builder, string name)
        => builder.EntityMaps.Single(em => em.Entity.Name == name);

    private static PropertyMap Property(EFCoreEntityBuilder builder, string entity, string property)
        => Entity(builder, entity).PropertyMaps.Single(pm => pm.Property.Name == property);

    private const string Customer = """
        public class Customer
        {
            public int Id { get; set; }

            public string Name { get; set; } = "";

            public decimal CreditLimit { get; set; }

            public List<Order> Orders { get; set; } = new();
        }
        """;

    private const string Order = """
        public class Order
        {
            public int Id { get; set; }

            public int BuyerRef { get; set; }

            public Customer Buyer { get; set; } = null!;
        }
        """;

    [Fact]
    public void ThePropertyChainStatesTheColumnFacts()
    {
        var builder = Parsed(Customer, """
            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Customer>().Property(c => c.Name).HasColumnName("customer_name").HasMaxLength(80).IsRequired();
                    modelBuilder.Entity<Customer>().Property(c => c.CreditLimit).HasPrecision(18, 2);
                }
            }
            """);

        var name = Property(builder, "Customer", "Name");
        Assert.Equal("customer_name", name.ColumnName);
        Assert.Equal(80, name.Length);
        Assert.False(name.IsNullable);

        var limit = Property(builder, "Customer", "CreditLimit");
        Assert.Equal(18, limit.Precision);
        Assert.Equal(2, limit.Scale);

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    /// <summary>
    /// The configuration outranks the annotation in EF Core: the different column stays the
    /// configuration's and the annotation's is a conflict; the same column is no event.
    /// </summary>
    [Fact]
    public void AnAnnotationThatSaysOtherwiseIsAConflictAndTheConfigurationStands()
    {
        var builder = Parsed("""
            public class Customer
            {
                public int Id { get; set; }

                [Column("A")]
                public string Name { get; set; } = "";

                [MaxLength(80)]
                public string Email { get; set; } = "";
            }

            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Customer>().Property(c => c.Name).HasColumnName("B");
                    modelBuilder.Entity<Customer>().Property(c => c.Email).HasMaxLength(80);
                }
            }
            """);

        Assert.Equal("B", Property(builder, "Customer", "Name").ColumnName);
        var conflict = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal(MappingFactCategory.ColumnName, conflict.Category);
        Assert.Equal("Name", conflict.Property);
    }

    /// <summary>
    /// The nullability EF Core reads from the type is a convention below the configuration:
    /// IsRequired(false) over a string the class declares without a question mark is what
    /// EF Core maps, and it is no conflict.
    /// </summary>
    [Fact]
    public void IsRequiredFalseOverANonNullableTypeIsNoConflictWithTheConvention()
    {
        var builder = Parsed(Customer, """
            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Customer>().Property(c => c.Name).IsRequired(false);
            }
            """);

        Assert.True(Property(builder, "Customer", "Name").IsNullable);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    /// <summary>
    /// HasKey names the parts in the order of the lambda, the convention does not look for a
    /// key of its own, and the strategy the configuration left open is the convention over
    /// the key's type, which only the class declares.
    /// </summary>
    [Fact]
    public void HasKeyStatesTheKeyAndTheClassSuppliesTheStrategy()
    {
        var builder = Parsed("""
            public class OrderLine
            {
                public int Id { get; set; }

                public int OrderNo { get; set; }

                public int LineNo { get; set; }
            }

            public class ProductCode
            {
                public int Id { get; set; }

                public int Code { get; set; }
            }

            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<OrderLine>().HasKey(l => new { l.OrderNo, l.LineNo });
                    modelBuilder.Entity<ProductCode>(b =>
                    {
                        b.HasKey(p => p.Code);
                        b.Property(p => p.Code).ValueGeneratedNever();
                    });
                }
            }
            """);

        var line = Entity(builder, "OrderLine").PrimaryKey!;
        Assert.Equal(["OrderNo", "LineNo"], line.Parts.OrderBy(p => p.Order).Select(p => p.PropertyMap.Property.Name));
        Assert.All(line.Parts, p => Assert.Equal(PrimaryKeyStrategy.Assigned, p.Strategy));

        var code = Assert.Single(Entity(builder, "ProductCode").PrimaryKey!.Parts);
        Assert.Equal("Code", code.PropertyMap.Property.Name);
        Assert.Equal(PrimaryKeyStrategy.Assigned, code.Strategy);
    }

    [Fact]
    public void HasKeyBesideKeylessKeepsTheKeyWithAConflict()
    {
        var builder = Parsed("""
            [Keyless]
            public class Rate
            {
                public int Code { get; set; }
            }

            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Rate>().HasKey(r => r.Code);
            }
            """);

        var rate = Entity(builder, "Rate");
        Assert.False(rate.HasNoKey);
        Assert.Equal("Code", Assert.Single(rate.PrimaryKey!.Parts).PropertyMap.Property.Name);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.PrimaryKey);
    }

    /// <summary>
    /// The DbSet name is EF Core's table convention and materializes (decision 067 applied by
    /// decision 132) - below [Table] and below ToTable, both of which outrank it in silence.
    /// ToTable outranks [Table] with a conflict, as every configuration outranks an annotation.
    /// </summary>
    [Fact]
    public void TheTableComesFromToTableThenTableThenTheDbSet()
    {
        var builder = Parsed("""
            public class Customer
            {
                public int Id { get; set; }
            }

            [Table("Purchases")]
            public class Order
            {
                public int Id { get; set; }
            }

            [Table("Goods")]
            public class Product
            {
                public int Id { get; set; }
            }

            public class ShopContext : DbContext
            {
                public DbSet<Customer> Customers { get; set; } = null!;

                public DbSet<Order> Orders { get; set; } = null!;

                public DbSet<Product> Products { get; set; } = null!;

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Product>().ToTable("Items", "sales");
            }
            """);

        Assert.Equal("Customers", Entity(builder, "Customer").Table);
        Assert.Equal("Purchases", Entity(builder, "Order").Table);
        Assert.Equal("Items", Entity(builder, "Product").Table);
        Assert.Equal("sales", Entity(builder, "Product").Schema);

        var conflict = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal("Product", conflict.Entity);
        Assert.Equal(MappingFactCategory.TableName, conflict.Category);
    }

    [Fact]
    public void TheDefaultSchemaFillsEveryEntityWithoutOneOfItsOwn()
    {
        var builder = Parsed("""
            public class Customer
            {
                public int Id { get; set; }
            }

            [Table("Orders", Schema = "archive")]
            public class Order
            {
                public int Id { get; set; }
            }

            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.HasDefaultSchema("shop");
                }
            }
            """);

        Assert.Equal("shop", Entity(builder, "Customer").Schema);
        Assert.Equal("archive", Entity(builder, "Order").Schema);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    /// <summary>
    /// A many-to-one with a foreign key the convention would not find: the configured side
    /// holds the key through the property HasForeignKey names, and the far end WithMany names
    /// is the one-to-many over the same key - in either order of the units.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HasOneWithManyStatesTheForeignKeyTheConventionWouldNotFind(bool contextFirst)
    {
        const string context = """
            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Order>()
                        .HasOne(o => o.Buyer)
                        .WithMany(c => c.Orders)
                        .HasForeignKey(o => o.BuyerRef);
                }
            }
            """;

        var builder = contextFirst ? Parsed(context, Order, Customer) : Parsed(Customer, Order, context);

        var buyer = Assert.Single(Entity(builder, "Order").Relations);
        Assert.Equal(Cardinality.ManyToOne, buyer.Cardinality);
        Assert.Equal(RelationRole.Owning, buyer.Role);
        Assert.Equal("Orders", buyer.InverseRelationName);
        Assert.Equal("BuyerRef", Assert.Single(buyer.ColumnPairs).Source.Property.Name);

        var orders = Assert.Single(Entity(builder, "Customer").Relations);
        Assert.Equal(Cardinality.OneToMany, orders.Cardinality);
        Assert.Equal("Buyer", orders.InverseRelationName);
    }

    [Fact]
    public void HasManyWithOneConfiguresTheSameRelationshipFromTheOtherSide()
    {
        var builder = Parsed(Customer, Order, """
            public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
            {
                public void Configure(EntityTypeBuilder<Customer> builder)
                    => builder.HasMany(c => c.Orders).WithOne(o => o.Buyer).HasForeignKey(o => o.BuyerRef);
            }
            """);

        var buyer = Assert.Single(Entity(builder, "Order").Relations);
        Assert.Equal(Cardinality.ManyToOne, buyer.Cardinality);
        Assert.Equal("BuyerRef", Assert.Single(buyer.ColumnPairs).Source.Property.Name);
        Assert.Equal(Cardinality.OneToMany, Assert.Single(Entity(builder, "Customer").Relations).Cardinality);
    }

    /// <summary>
    /// The one-to-one the annotations cannot state: HasForeignKey&lt;T&gt; names the dependent,
    /// which owns the key, and the other end is the inverse side.
    /// </summary>
    [Fact]
    public void AOneToOneOwnsTheKeyOnTheSideHasForeignKeyNames()
    {
        var builder = Parsed("""
            public class Person
            {
                public int Id { get; set; }

                public Passport? Passport { get; set; }
            }

            public class Passport
            {
                public int Id { get; set; }

                public int HolderId { get; set; }

                public Person Holder { get; set; } = null!;
            }

            public class Registry : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Person>()
                        .HasOne(p => p.Passport)
                        .WithOne(p => p.Holder)
                        .HasForeignKey<Passport>(p => p.HolderId);
            }
            """);

        var holder = Assert.Single(Entity(builder, "Passport").Relations);
        Assert.Equal(Cardinality.OneToOne, holder.Cardinality);
        Assert.Equal(RelationRole.Owning, holder.Role);
        Assert.Equal("HolderId", Assert.Single(holder.ColumnPairs).Source.Property.Name);

        var passport = Assert.Single(Entity(builder, "Person").Relations);
        Assert.Equal(Cardinality.OneToOne, passport.Cardinality);
        Assert.Equal(RelationRole.Inverse, passport.Role);
    }

    [Fact]
    public void HasManyWithManyPairsTheTwoCollections()
    {
        var builder = Parsed("""
            public class Student
            {
                public int Id { get; set; }

                public List<Course> Enrolled { get; set; } = new();

                public List<Course> Tutored { get; set; } = new();
            }

            public class Course
            {
                public int Id { get; set; }

                public List<Student> Students { get; set; } = new();
            }

            public class School : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Student>().HasMany(s => s.Enrolled).WithMany(c => c.Students);
            }
            """);

        // Two collections of Student face Course, which EF Core's convention cannot pair on its
        // own; the configuration does.
        var enrolled = Entity(builder, "Student").Relations.Single(r => r.SourceNavigationProperty == "Enrolled");
        Assert.Equal(Cardinality.ManyToMany, enrolled.Cardinality);
        Assert.Equal("Students", enrolled.InverseRelationName);

        var students = Assert.Single(Entity(builder, "Course").Relations);
        Assert.Equal(Cardinality.ManyToMany, students.Cardinality);
        Assert.Equal("Enrolled", students.InverseRelationName);
    }

    [Fact]
    public void AForeignKeyAnnotationThatNamesOtherPropertiesIsAConflict()
    {
        var builder = Parsed(Customer, """
            public class Order
            {
                public int Id { get; set; }

                public int BuyerRef { get; set; }

                public int CustomerId { get; set; }

                [ForeignKey(nameof(CustomerId))]
                public Customer Buyer { get; set; } = null!;
            }

            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Order>().HasOne(o => o.Buyer).WithMany(c => c.Orders).HasForeignKey(o => o.BuyerRef);
            }
            """);

        Assert.Equal("BuyerRef", Assert.Single(Entity(builder, "Order").Relations).ColumnPairs.Single().Source.Property.Name);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.ForeignKeyColumns);
    }

    [Fact]
    public void AUniqueIndexAndAnAlternateKeyAreUniqueConstraints()
    {
        var builder = Parsed("""
            public class Customer
            {
                public int Id { get; set; }

                public string Email { get; set; } = "";

                public string Code { get; set; } = "";

                public string Name { get; set; } = "";

                public string Phone { get; set; } = "";
            }

            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Customer>().HasIndex(c => c.Email).IsUnique().HasDatabaseName("UX_Customer_Email");
                    modelBuilder.Entity<Customer>().HasAlternateKey(c => c.Code);
                    modelBuilder.Entity<Customer>().HasIndex(c => c.Name);
                    modelBuilder.Entity<Customer>().HasIndex(c => c.Phone).IsUnique().HasFilter("[Phone] IS NOT NULL");
                }
            }
            """);

        var constraints = Entity(builder, "Customer").UniqueConstraints;
        Assert.Equal(2, constraints.Count);
        Assert.Contains(constraints, c => c.Name == "UX_Customer_Email" && c.PropertyNames.SequenceEqual(["Email"]));
        Assert.Contains(constraints, c => c.PropertyNames.SequenceEqual(["Code"]));

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("(Name) is not unique", StringComparison.Ordinal));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("HasFilter", StringComparison.Ordinal));
    }

    [Fact]
    public void IgnoreTakesThePropertyOutOfTheModelAsNotMappedDoes()
    {
        var builder = Parsed(Customer, """
            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Customer>().Ignore(c => c.CreditLimit);
            }
            """);

        Assert.True(Property(builder, "Customer", "CreditLimit").IsTransient);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    /// <summary>
    /// A call the subset knows to return the same builder is a loss and the chain goes on;
    /// any other one ends the chain, because what the next link stands on is not known - the
    /// column of an owned type must not land on its owner.
    /// </summary>
    [Fact]
    public void AnUnreadCallIsALossAndAnUnknownBuilderEndsTheChain()
    {
        var builder = Parsed(Customer, """
            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Customer>().Property(c => c.Name).HasDefaultValue("n/a").HasColumnName("customer_name");
                    modelBuilder.Entity<Customer>().HasDiscriminator<string>("Kind").HasValue("customer");
                }
            }
            """);

        Assert.Equal("customer_name", Property(builder, "Customer", "Name").ColumnName);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("HasDefaultValue(…)", StringComparison.Ordinal));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss
            && r.Reason.Contains("HasDiscriminator(…)", StringComparison.Ordinal)
            && r.Reason.Contains("HasValue(…)", StringComparison.Ordinal));
    }

    /// <summary>
    /// The fluent spelling of an owned type is excluded area 2 like [Owned] (decision 177): the
    /// member leaves the model with one record, and the rest of the chain, which configures the
    /// owned type, goes with it without a record of its own.
    /// </summary>
    [Fact]
    public void AnOwnedTypeTakesItsMemberAndTheRestOfTheChainOutOfTheModel()
    {
        var builder = Parsed("""
            public class Customer
            {
                public int Id { get; set; }

                public Address Address { get; set; } = new();
            }

            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Customer>().OwnsOne(c => c.Address).Property(a => a.Street).HasColumnName("street");
                }
            }
            """);

        builder.Build();

        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        Assert.DoesNotContain(customer.Entity.Properties, p => p.Name == "Address");
        Assert.Empty(customer.Relations);

        var record = Assert.Single(builder.Records, r => r.Property == "Address");
        Assert.Equal(ConversionRecordKind.Loss, record.Kind);
        Assert.Contains("OwnsOne(…)", record.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(builder.Records, r => r.Reason.Contains("HasColumnName(…)", StringComparison.Ordinal));
    }

    [Fact]
    public void AStatementOutsideTheSubsetAndANonLiteralArgumentAreLosses()
    {
        var builder = Parsed(Customer, """
            public class ShopContext : DbContext
            {
                private const int NameLength = 80;

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    foreach (var entity in modelBuilder.Model.GetEntityTypes())
                    {
                        entity.SetTableName(entity.DisplayName());
                    }

                    modelBuilder.Entity<Customer>().Property(c => c.Name).HasMaxLength(NameLength);
                    base.OnModelCreating(modelBuilder);
                }
            }
            """);

        Assert.Null(Property(builder, "Customer", "Name").Length);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.StartsWith("The statement 'foreach", StringComparison.Ordinal));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("HasMaxLength(…) has an argument", StringComparison.Ordinal));
        Assert.DoesNotContain(builder.Records, r => r.Reason.Contains("base.OnModelCreating", StringComparison.Ordinal));
    }

    [Fact]
    public void AConcurrencyTokenFromTheConfigurationIsReadByTheRuleOfTheAnnotation()
    {
        var builder = Parsed("""
            public class Customer
            {
                public int Id { get; set; }

                public int Revision { get; set; }
            }

            public class ShopContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Customer>().Property(c => c.Revision).IsConcurrencyToken();
            }
            """);

        var revision = Property(builder, "Customer", "Revision");
        Assert.True(revision.IsVersion);
        Assert.True(revision.IsApplicationManagedVersion);
    }

    /// <summary>
    /// DbSet&lt;T&gt; and Entity&lt;T&gt;() name entities of the model, so a context whose
    /// configuration only waits for the classes is no barren unit; but where no unit declares
    /// the class, there is nothing to generate, and the entity is refused out loud rather than
    /// written as an empty class in its name.
    /// </summary>
    [Fact]
    public void AnEntityTheConfigurationNamesWithoutItsClassIsRefusedWithARecord()
    {
        const string context = """
            public class ShopContext : DbContext
            {
                public DbSet<Customer> Customers { get; set; } = null!;

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Order>().HasOne(o => o.Buyer).WithMany().HasForeignKey(o => o.BuyerRef);
            }
            """;

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.EFCore,
            [new() { ContentType = ConversionContentType.CSharp, Content = context, Name = "ShopContext.cs" }]);

        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Entity == "Customer"
            && r.Reason.Contains("no unit of the conversion declares its class", StringComparison.Ordinal));
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Entity == "Order");
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("neither a mapping fact nor a query came of it", StringComparison.Ordinal));
    }

    /// <summary>
    /// End to end through the orchestration: the configuration class is a unit of its own and
    /// no entity, the context is no entity either and says that its configuration is read, and
    /// the target writes what the configuration stated.
    /// </summary>
    [Fact]
    public void AContextAndAConfigurationClassReachTheTarget()
    {
        const string context = """
            namespace Shop;

            public class ShopContext : DbContext
            {
                public DbSet<Customer> Customers { get; set; } = null!;

                public DbSet<Order> Orders { get; set; } = null!;

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.ApplyConfiguration(new OrderConfiguration());
                    modelBuilder.Entity<Customer>().Property(c => c.Name).HasColumnName("customer_name");
                }
            }
            """;

        const string configuration = """
            namespace Shop;

            public class OrderConfiguration : IEntityTypeConfiguration<Order>
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.HasOne(o => o.Buyer).WithMany(c => c.Orders).HasForeignKey(o => o.BuyerRef);
                }
            }
            """;

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate,
        [
            new() { ContentType = ConversionContentType.CSharp, Content = "namespace Shop;\n" + Customer, Name = "Customer.cs" },
            new() { ContentType = ConversionContentType.CSharp, Content = "namespace Shop;\n" + Order, Name = "Order.cs" },
            new() { ContentType = ConversionContentType.CSharp, Content = context, Name = "ShopContext.cs" },
            new() { ContentType = ConversionContentType.CSharp, Content = configuration, Name = "OrderConfiguration.cs" },
        ]);

        var mapping = string.Join("\n", result.Sources.Where(s => s.ContentType == ConversionContentType.XML).Select(s => s.Content));
        Assert.Contains("table=\"Customers\"", mapping, StringComparison.Ordinal);
        Assert.Contains("table=\"Orders\"", mapping, StringComparison.Ordinal);
        Assert.Contains("customer_name", mapping, StringComparison.Ordinal);
        Assert.Contains("BuyerRef", mapping, StringComparison.Ordinal);

        Assert.DoesNotContain(result.Sources, s => s.Content.Contains("class ShopContext", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Sources, s => s.Content.Contains("class OrderConfiguration", StringComparison.Ordinal));
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss
            && r.Reason.Contains("'ShopContext' is the EF Core context", StringComparison.Ordinal)
            && r.Reason.Contains("read as configuration", StringComparison.Ordinal));
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss
            && r.Reason.Contains("'OrderConfiguration' is an EF Core entity type configuration", StringComparison.Ordinal)
            && r.Unit == "OrderConfiguration.cs");
    }
}
