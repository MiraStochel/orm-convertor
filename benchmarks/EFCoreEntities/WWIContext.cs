using Microsoft.EntityFrameworkCore;

namespace EFCoreEntities;

public class WWIContext(DbContextOptions<WWIContext> options) : DbContext(options)
{
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; }

    public DbSet<Supplier> Suppliers { get; set; }

    public DbSet<Order> Orders { get; set; }

    public DbSet<OrderLine> OrderLines { get; set; }

    public DbSet<StockItem> StockItems { get; set; }

    public DbSet<StockGroup> StockGroups { get; set; }

    public DbSet<Customer> Customers { get; set; }

    public DbSet<Person> People { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseOrderUpdate>().HasNoKey();

        modelBuilder.Entity<StockItem>()
            .HasMany(s => s.StockGroups)
            .WithMany(sg => sg.StockItems)
            .UsingEntity(
                "Warehouse.StockItemStockGroups",
                l => l.HasOne(typeof(StockGroup)).WithMany().HasForeignKey("StockGroupID"),
                r => r.HasOne(typeof(StockItem)).WithMany().HasForeignKey("StockItemID")
            );

        modelBuilder.Entity<Person>()
            .OwnsOne(p => p.CustomFields, cb =>
            {
                cb.ToJson();
            });

        base.OnModelCreating(modelBuilder);
    }
}
