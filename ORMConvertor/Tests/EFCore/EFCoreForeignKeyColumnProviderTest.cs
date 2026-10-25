using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 names a foreign key by, measured on the model it builds without a
/// connection: [ForeignKey] on the navigation or on the property, HasForeignKey and the
/// convention all name properties of the dependent, and the column is the one the property
/// maps to - [Column] or HasColumnName renames it under every form. The EF Core parser hands
/// the names on as properties for that reason (EFCoreRenamedForeignKeyTest).
/// </summary>
public class EFCoreForeignKeyColumnProviderTest
{
    [Fact]
    public void EveryFormNamesThePropertyWhoseColumnTheKeyTakes()
    {
        foreach (var (entity, configure) in new (Type, Action<ModelBuilder>?)[]
        {
            (typeof(NavigationOrder), null),
            (typeof(PropertyOrder), null),
            (typeof(ConventionOrder), null),
            (typeof(ConfiguredOrder), b =>
            {
                b.Entity<ConfiguredOrder>().Property(o => o.PersonId).HasColumnName("person_id");
                b.Entity<ConfiguredOrder>().HasOne(o => o.Person).WithMany().HasForeignKey(o => o.PersonId);
            }),
        })
        {
            using var context = new ProbeContext(entity, configure);

            var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(entity)!;
            var property = Assert.Single(Assert.Single(entityType.GetForeignKeys()).Properties);
            Assert.True(property.Name == "PersonId" && property.GetColumnName() == "person_id",
                $"{entity.Name}: {property.Name} -> {property.GetColumnName()}");
        }
    }

    /// <summary>A context over one dependent and its principal, never connected; the model is cached per dependent.</summary>
    private sealed class ProbeContext(Type entity, Action<ModelBuilder>? configure) : DbContext
    {
        public Type Entity { get; } = entity;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options
                .UseSqlServer("Server=(local);Database=Probe;Trusted_Connection=True;TrustServerCertificate=True")
                .ReplaceService<IModelCacheKeyFactory, EntityModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity(Entity);
            modelBuilder.Entity<Person>();
            configure?.Invoke(modelBuilder);
        }
    }

    private sealed class EntityModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((ProbeContext)context).Entity, designTime);
    }

    public class Person { public int Id { get; set; } }

    public class NavigationOrder
    {
        public int Id { get; set; }
        [Column("person_id")] public int PersonId { get; set; }
        [ForeignKey(nameof(PersonId))] public Person Person { get; set; } = null!;
    }

    public class PropertyOrder
    {
        public int Id { get; set; }
        [Column("person_id"), ForeignKey(nameof(Person))] public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
    }

    public class ConventionOrder
    {
        public int Id { get; set; }
        [Column("person_id")] public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
    }

    public class ConfiguredOrder
    {
        public int Id { get; set; }
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
    }
}
