using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A property without a setter (decision 134). A target writes the narrowest setter its
/// framework writes the property through - EF Core private set, except a collection a
/// relation navigates, NHibernate protected set, Dapper none -, the C# reading takes a setter
/// with an access modifier of its own for none and an init accessor for one, and an EF Core
/// source's auto-property with a getter alone is not persisted unless EF Core maps it.
/// </summary>
public class PropertyWithoutSetterTest
{
    /// <summary>A JPA entity that exposes its fields through getters alone, as field access allows.</summary>
    private static readonly ConversionSource[] JavaRoomAndBooking =
    [
        new()
        {
            ContentType = ConversionContentType.Java,
            Content = """
                package clinic;

                import jakarta.persistence.*;
                import java.util.List;

                @Entity
                @Table(name = "Rooms")
                public class Room {

                    @Id
                    @Column(name = "RoomId")
                    private Long RoomId;

                    @Column(name = "Code", length = 10, nullable = false)
                    private String Code;

                    @OneToMany(mappedBy = "Room")
                    private List<Booking> Bookings;

                    @Transient
                    private String Label;

                    public Long getRoomId() { return RoomId; }
                    public String getCode() { return Code; }
                    public List<Booking> getBookings() { return Bookings; }
                    public String getLabel() { return Label; }
                }
                """,
        },
        new()
        {
            ContentType = ConversionContentType.Java,
            Content = """
                package clinic;

                import jakarta.persistence.*;

                @Entity
                @Table(name = "Bookings")
                public class Booking {

                    @Id
                    @Column(name = "BookingId")
                    private Long BookingId;

                    @ManyToOne
                    @JoinColumn(name = "RoomId")
                    private Room Room;

                    public Long getBookingId() { return BookingId; }
                    public Room getRoom() { return Room; }
                }
                """,
        },
    ];

    private static ConversionResult Convert(ORMEnum source, ORMEnum target, params ConversionSource[] units)
        => ConversionHandler.Convert(source, target, [.. units]);

    private static IEnumerable<string> Code(ConversionResult result, ConversionContentType contentType)
        => result.Sources.Where(s => s.ContentType == contentType).Select(s => s.Content);

    private static string Class(ConversionResult result, string name)
        => Code(result, ConversionContentType.CSharpEntity).Single(c => c.Contains($" class {name}", StringComparison.Ordinal));

    [Fact]
    public void EFCoreMapsAJavaFieldWithAGetterAloneThroughAPrivateSetter()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.EFCore, JavaRoomAndBooking);

        var room = Class(result, "Room");
        Assert.Contains("public long RoomId { get; private set; }", room);
        Assert.Contains("public string Code { get; private set; }", room);
        Assert.Contains("public string? Label { get; }", room); // transient: EF Core writes nothing into it
        Assert.Contains("Bookings { get; }", room);             // a collection navigation EF Core fills through the backing field
        Assert.Contains("public Room? Room { get; private set; }", Class(result, "Booking"));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        var model = EFCoreAcceptance.BuildModel(GeneratedEntityCompiler.CompileOrFail(
            nameof(EFCoreMapsAJavaFieldWithAGetterAloneThroughAPrivateSetter),
            Code(result, ConversionContentType.CSharpEntity),
            GeneratedEntityCompiler.EFCoreConsumerReferences));

        var roomType = model.GetEntityTypes().Single(e => e.ClrType.Name == "Room");
        Assert.Equal("RoomId", Assert.Single(roomType.FindPrimaryKey()!.Properties).Name);
        Assert.NotNull(roomType.FindProperty("Code"));
        Assert.Null(roomType.FindProperty("Label"));
        Assert.NotNull(roomType.FindNavigation("Bookings"));
        Assert.NotNull(model.GetEntityTypes().Single(e => e.ClrType.Name == "Booking").FindNavigation("Room"));
    }

    [Fact]
    public void NHibernateMapsAJavaFieldWithAGetterAloneThroughAProtectedSetter()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.NHibernate, JavaRoomAndBooking);

        var room = Class(result, "Room");
        Assert.Contains("public virtual long RoomId { get; protected set; }", room);
        Assert.Contains("public virtual string Code { get; protected set; }", room);
        Assert.Contains("public virtual string? Label { get; }", room); // left out of the mapping
        Assert.Contains("Bookings { get; protected set; }", room);      // NHibernate replaces the collection too
        Assert.Contains("public virtual Room? Room { get; protected set; }", Class(result, "Booking"));

        NHibernateAcceptance.BuildSessionFactory(
            GeneratedEntityCompiler.CompileOrFail(
                nameof(NHibernateMapsAJavaFieldWithAGetterAloneThroughAProtectedSetter),
                Code(result, ConversionContentType.CSharpEntity),
                GeneratedEntityCompiler.NHibernateConsumerReferences),
            Code(result, ConversionContentType.XML));
    }

    /// <summary>Dapper fills an auto-property with a getter alone through its backing field, so it needs no setter.</summary>
    [Fact]
    public void DapperWritesAPropertyWithoutASetterAsItIs()
    {
        var result = Convert(ORMEnum.Hibernate, ORMEnum.Dapper, JavaRoomAndBooking);

        var room = Class(result, "Room");
        Assert.Contains("public long RoomId { get; }", room);
        Assert.Contains("public string Code { get; }", room);
        Assert.DoesNotContain("set;", room);
    }

    /// <summary>
    /// A setter with an access modifier of its own is the one the class keeps for itself and
    /// its framework: read as none, it comes back in each target's narrowest form instead of
    /// widening to a public setter - the round trip of the targets' own output.
    /// </summary>
    [Fact]
    public void ARestrictedSetterIsReadAsNoneAndComesBackNarrow()
    {
        var nhibernate = Convert(ORMEnum.NHibernate, ORMEnum.NHibernate,
            new ConversionSource
            {
                ContentType = ConversionContentType.CSharp,
                Content = """
                    public class Ward
                    {
                        public virtual int WardId { get; protected set; }
                        public virtual string Name { get; protected set; }
                        public virtual string Note { get; set; }
                    }
                    """,
            },
            new ConversionSource
            {
                ContentType = ConversionContentType.XML,
                Content = """
                    <?xml version="1.0" encoding="utf-8"?>
                    <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                      <class name="Ward" table="Wards">
                        <id name="WardId" column="WardId" type="Int32">
                          <generator class="assigned" />
                        </id>
                        <property name="Name" column="Name" type="String" />
                        <property name="Note" column="Note" type="String" />
                      </class>
                    </hibernate-mapping>
                    """,
            });

        var ward = Class(nhibernate, "Ward");
        Assert.Contains("public virtual int WardId { get; protected set; }", ward);
        Assert.Contains("public virtual string Name { get; protected set; }", ward);
        Assert.Contains("public virtual string Note { get; set; }", ward);

        var efCoreSource = new ConversionSource
        {
            ContentType = ConversionContentType.CSharp,
            Content = """
                using System.ComponentModel.DataAnnotations;

                public class Ward
                {
                    [Key]
                    public int WardId { get; private set; }

                    public string Name { get; private set; }
                }
                """,
        };

        var efCore = Class(Convert(ORMEnum.EFCore, ORMEnum.EFCore, efCoreSource), "Ward");
        Assert.Contains("public int WardId { get; private set; }", efCore);
        Assert.Contains("public string Name { get; private set; }", efCore);
        Assert.DoesNotContain("required", efCore); // CS9032: a required member cannot have a narrower setter

        Assert.Contains("public virtual string Name { get; protected set; }",
            Class(Convert(ORMEnum.EFCore, ORMEnum.NHibernate, efCoreSource), "Ward"));
    }

    /// <summary>
    /// An init accessor offers the write to callers, in an object initializer, so it is a
    /// setter; its restriction to initialization has no place in the model and is a loss.
    /// </summary>
    [Fact]
    public void AnInitAccessorIsASetterAndItsRestrictionIsALoss()
    {
        var result = Convert(ORMEnum.EFCore, ORMEnum.EFCore, new ConversionSource
        {
            ContentType = ConversionContentType.CSharp,
            Content = """
                using System.ComponentModel.DataAnnotations;

                public class Ward
                {
                    [Key]
                    public int WardId { get; init; }

                    public required string Name { get; init; }
                }
                """,
        });

        var ward = Class(result, "Ward");
        Assert.Contains("public required int WardId { get; set; }", ward);
        Assert.Contains("public required string Name { get; set; }", ward);
        Assert.Equal(2, result.Records.Count(r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("init accessor", StringComparison.Ordinal)));

        GeneratedEntityCompiler.CompileOrFail(
            nameof(AnInitAccessorIsASetterAndItsRestrictionIsALoss),
            Code(result, ConversionContentType.CSharpEntity),
            GeneratedEntityCompiler.EFCoreConsumerReferences);
    }

    /// <summary>
    /// EF Core leaves an auto-property with a getter alone out of its model whatever stands on
    /// it, so the reading takes it the way it takes [NotMapped] - without a record for the
    /// dead annotation -, except a collection navigation, which EF Core fills.
    /// </summary>
    [Fact]
    public void AGetOnlyAutoPropertyOfAnEFCoreSourceIsNotPersisted()
    {
        var source = new ConversionSource
        {
            ContentType = ConversionContentType.CSharp,
            Content = """
                using System.ComponentModel.DataAnnotations;
                using System.ComponentModel.DataAnnotations.Schema;

                [Table("Rooms")]
                public class Room
                {
                    public Room(string code) { Code = code; }

                    [Key]
                    public int RoomId { get; set; }

                    [Column("Label")]
                    public string Code { get; }

                    public List<string> Tags { get; } = [];

                    public ICollection<Booking> Bookings { get; } = new List<Booking>();
                }

                [Table("Bookings")]
                public class Booking
                {
                    [Key]
                    public int BookingId { get; set; }

                    public int RoomId { get; set; }

                    public Room Room { get; set; }
                }
                """,
        };

        var efCore = Convert(ORMEnum.EFCore, ORMEnum.EFCore, source);
        var room = Class(efCore, "Room");
        Assert.Contains("[NotMapped]\r\n    public string Code { get; }".Replace("\r\n", Environment.NewLine), room);
        Assert.DoesNotContain("[Column(\"Label\")]", room);
        Assert.Contains("[NotMapped]\r\n    public List<string> Tags { get; }".Replace("\r\n", Environment.NewLine), room);
        Assert.DoesNotContain("Bookings { get; private set; }", room);
        Assert.Contains("Bookings { get; }", room);
        Assert.DoesNotContain(efCore.Records, r => r.Property is "Code" or "Tags" && r.Kind != ConversionRecordKind.Convention);

        var model = EFCoreAcceptance.BuildModel(GeneratedEntityCompiler.CompileOrFail(
            nameof(AGetOnlyAutoPropertyOfAnEFCoreSourceIsNotPersisted),
            Code(efCore, ConversionContentType.CSharpEntity),
            GeneratedEntityCompiler.EFCoreConsumerReferences));
        var roomType = model.GetEntityTypes().Single(e => e.ClrType.Name == "Room");
        Assert.Null(roomType.FindProperty("Code"));
        Assert.NotNull(roomType.FindNavigation("Bookings"));

        var mapping = Assert.Single(Code(Convert(ORMEnum.EFCore, ORMEnum.NHibernate, source), ConversionContentType.XML),
            m => m.Contains("<class name=\"Room\"", StringComparison.Ordinal));
        Assert.DoesNotContain("name=\"Code\"", mapping);
        Assert.DoesNotContain("name=\"Tags\"", mapping);
        Assert.Contains("name=\"Bookings\"", mapping);
    }

    /// <summary>
    /// [Key] on a property EF Core does not map is a key EF Core builds no model over; it
    /// enters beside the transient flag, and the completeness gate refuses the entity.
    /// </summary>
    [Fact]
    public void AGetOnlyKeyOfAnEFCoreSourceIsRefused()
    {
        var result = Convert(ORMEnum.EFCore, ORMEnum.EFCore, new ConversionSource
        {
            ContentType = ConversionContentType.CSharp,
            Content = """
                using System.ComponentModel.DataAnnotations;

                public class Room
                {
                    [Key]
                    public int RoomId { get; }

                    public string Code { get; set; }
                }
                """,
        });

        Assert.Contains(result.Records, r => r is { Kind: ConversionRecordKind.Failure, Category: MappingFactCategory.TransientProperty, Property: "RoomId" });
        Assert.Empty(Code(result, ConversionContentType.CSharpEntity));
    }

    /// <summary>
    /// A member the fluent configuration names is mapped by EF Core through its backing field,
    /// with or without a setter: as a key, a property, an index member, or a foreign key member
    /// of a one-to-many configured from the collection's side, which belongs to the far entity.
    /// </summary>
    [Fact]
    public void AGetOnlyPropertyTheConfigurationNamesIsPersisted()
    {
        var result = Convert(ORMEnum.EFCore, ORMEnum.EFCore, new ConversionSource
        {
            ContentType = ConversionContentType.CSharp,
            Content = """
                using Microsoft.EntityFrameworkCore;

                public class Shop
                {
                    public int Code { get; }
                    public string Name { get; }
                    public string Label { get; }
                    public string Motto { get; }
                    public ICollection<Order> Orders { get; } = new List<Order>();
                }

                public class Order
                {
                    public int OrderId { get; set; }
                    public int ShopCode { get; }
                }

                public class ShopContext : DbContext
                {
                    public DbSet<Shop> Shops { get; set; }
                    public DbSet<Order> Orders { get; set; }

                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        modelBuilder.Entity<Shop>().HasKey(s => s.Code);
                        modelBuilder.Entity<Shop>().Property(s => s.Name).HasMaxLength(50);
                        modelBuilder.Entity<Shop>().HasIndex(s => s.Label).IsUnique();
                        modelBuilder.Entity<Shop>().HasMany(s => s.Orders).WithOne().HasForeignKey(o => o.ShopCode);
                    }
                }
                """,
        });

        var shop = Class(result, "Shop");
        Assert.Contains("public int Code { get; private set; }", shop);
        Assert.Contains("public string Name { get; private set; }", shop);
        Assert.Contains("public string Label { get; private set; }", shop);
        Assert.Contains("[NotMapped]", shop); // Motto, which nothing names
        Assert.Contains("public int ShopCode { get; private set; }", Class(result, "Order"));
        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Conflict or ConversionRecordKind.Failure);

        var model = EFCoreAcceptance.BuildModel(GeneratedEntityCompiler.CompileOrFail(
            nameof(AGetOnlyPropertyTheConfigurationNamesIsPersisted),
            Code(result, ConversionContentType.CSharpEntity),
            GeneratedEntityCompiler.EFCoreConsumerReferences));
        var shopType = model.GetEntityTypes().Single(e => e.ClrType.Name == "Shop");
        Assert.Equal("Code", Assert.Single(shopType.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(50, shopType.FindProperty("Name")!.GetMaxLength());
        Assert.NotNull(shopType.FindProperty("Label"));
        Assert.Null(shopType.FindProperty("Motto"));
        Assert.NotNull(model.GetEntityTypes().Single(e => e.ClrType.Name == "Order").FindProperty("ShopCode"));
    }
}
