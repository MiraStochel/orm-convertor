using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Catalog;

namespace Tests.EFCore;

/// <summary>
/// EF Core names a foreign key by its properties - in [ForeignKey], in HasForeignKey and by
/// convention - and its column is whatever the property maps to
/// (EFCoreForeignKeyColumnProviderTest). The pairing finds such a property by its name, so a
/// foreign key property renamed by [Column] or HasColumnName reaches the targets as the
/// renamed column and the catalog compares that column with its own.
/// </summary>
public class EFCoreRenamedForeignKeyTest
{
    private const string Person = """
        [Table("People")]
        public class Person
        {
            public int Id { get; set; }
        }
        """;

    public static TheoryData<string, string, string?> RenamedForeignKeys => new()
    {
        {
            "[ForeignKey] on the navigation", """
            [Table("Orders")]
            public class Order
            {
                public int Id { get; set; }

                [Column("person_id")]
                public int PersonId { get; set; }

                [ForeignKey(nameof(PersonId))]
                public Person Person { get; set; }
            }
            """, null
        },
        {
            "[ForeignKey] on the property", """
            [Table("Orders")]
            public class Order
            {
                public int Id { get; set; }

                [Column("person_id"), ForeignKey(nameof(Person))]
                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """, null
        },
        {
            "the convention", """
            [Table("Orders")]
            public class Order
            {
                public int Id { get; set; }

                [Column("person_id")]
                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """, null
        },
        {
            "HasForeignKey beside HasColumnName", """
            [Table("Orders")]
            public class Order
            {
                public int Id { get; set; }

                public int PersonId { get; set; }

                public Person Person { get; set; }
            }
            """, """
            modelBuilder.Entity<Order>().Property(o => o.PersonId).HasColumnName("person_id");
            modelBuilder.Entity<Order>().HasOne(o => o.Person).WithMany().HasForeignKey(o => o.PersonId);
            """
        },
    };

    [Theory]
    [MemberData(nameof(RenamedForeignKeys))]
    public void TheRenamedColumnReachesTheTargets(string form, string order, string? configuration)
    {
        var nhibernate = Parsed(new NHibernateEntityBuilder(), order, configuration).Build()
            .Single(o => o.ContentType == ConversionContentType.XML && o.Content.Contains("<class name=\"Order\""))
            .Content;
        Assert.True(nhibernate.Contains("<many-to-one name=\"Person\" class=\"Person\" column=\"person_id\""), $"{form}:{Environment.NewLine}{nhibernate}");
        Assert.DoesNotContain("\"PersonId\" />", nhibernate);

        var hibernate = Parsed(new HibernateEntityBuilder(), order, configuration).Build()
            .Single(o => o.Content.Contains("class Order"))
            .Content;
        Assert.True(hibernate.Contains("@JoinColumn(name = \"person_id\""), $"{form}:{Environment.NewLine}{hibernate}");
    }

    /// <summary>
    /// The catalog completes before the pairing, so it reads the source's names while they wait:
    /// it gets the column of the property they name and finds the schema's foreign key agreeing.
    /// </summary>
    [Theory]
    [MemberData(nameof(RenamedForeignKeys))]
    public void TheCatalogFindsTheRenamedColumnInAgreement(string form, string order, string? configuration)
    {
        var builder = Parsed(new NHibernateEntityBuilder(), order, configuration);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(PeopleImage(), OrdersImage()));

        Assert.True(!builder.Records.Any(r => r.Category == MappingFactCategory.ForeignKeyColumns),
            $"{form}: {string.Join(Environment.NewLine, builder.Records.Select(r => r.Reason))}");
    }

    private static AbstractEntityBuilder Parsed(AbstractEntityBuilder builder, string order, string? configuration)
    {
        var context = new EFCoreReadingContext();

        if (configuration is not null)
        {
            new EFCoreFluentConfigurationParser(builder, context).Parse($$"""
                public class ShopContext : DbContext
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        {{configuration}}
                    }
                }
                """);
        }

        var parser = new EFCoreEntityParser(builder, context: context);
        parser.Parse(order);
        parser.Parse(Person);
        return builder;
    }

    private static TableImage PeopleImage() => new()
    {
        Schema = "dbo",
        Name = "People",
        Columns = [new ColumnImage { Name = "Id", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true }],
        PrimaryKeyColumns = ["Id"],
        ForeignKeys = [],
    };

    private static TableImage OrdersImage() => new()
    {
        Schema = "dbo",
        Name = "Orders",
        Columns =
        [
            new ColumnImage { Name = "Id", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "person_id", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["Id"],
        ForeignKeys =
        [
            new ForeignKeyImage
            {
                Name = "FK_Orders_People",
                ReferencedSchema = "dbo",
                ReferencedTable = "People",
                Columns = [new ForeignKeyColumn("person_id", "Id")],
            },
        ],
    };
}
