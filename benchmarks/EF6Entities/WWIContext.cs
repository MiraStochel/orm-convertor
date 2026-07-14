using System.Data.Entity;
using Common;

namespace EF6Entities
{
    [DbConfigurationType(typeof(WWIDbConfiguration))]
    public class WWIContext() : DbContext(DatabaseConfig.MSSQLConnectionString)
    {
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;

        public DbSet<Supplier> Suppliers { get; set; } = null!;

        public DbSet<Order> Orders { get; set; } = null!;

        public DbSet<OrderLine> OrderLines { get; set; } = null!;

        public DbSet<StockItem> StockItems { get; set; } = null!;

        public DbSet<StockGroup> StockGroups { get; set; } = null!;

        public DbSet<Customer> Customers { get; set; } = null!;

        public DbSet<Person> People { get; set; } = null!;

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StockItem>()
                .HasMany(si => si.StockGroups)
                .WithMany(sg => sg.StockItems)
                .Map(m =>
                {
                    m.ToTable("StockItemStockGroups", "Warehouse");
                    m.MapLeftKey("StockItemId");
                    m.MapRightKey("StockGroupId");
                });

            base.OnModelCreating(modelBuilder);
        }
    }
}
