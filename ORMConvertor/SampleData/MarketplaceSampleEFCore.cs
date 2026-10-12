namespace SampleData;

/// <summary>
/// The marketplace in EF Core, configured the way most EF Core projects are: plain classes
/// and everything about the tables in fluent configuration - <c>OnModelCreating</c> and an
/// <c>IEntityTypeConfiguration&lt;T&gt;</c> -, which the tool reads as a bounded syntactic
/// subset before the annotations (decision 132). The classes stand several to a file, as a
/// unit is a whole file in one language (decision 111).
///
/// The configuration states what the annotations could and some of what they cannot: a
/// default schema, keys the database generates and keys it does not, a composite key, an
/// alternate key, unique indexes over one and two columns, a one-to-one with its dependent
/// side, a self-reference, an explicit junction entity, a row version, a concurrency token,
/// literal column types, a column named apart from its property and an ignored property. Two
/// calls lie outside the subset on purpose - a default value written as SQL and a delete
/// behaviour -, so the records show what a call the tool does not read becomes.
///
/// The tables live in the schema Market, as in the JPA marketplace.
/// </summary>
public static class MarketplaceSampleEFCore
{
    /// <summary>The customer side: a region, a customer with an ignored property, and its one-to-one profile.</summary>
    public const string Customers = """
        namespace Marketplace;

        public class Region
        {
            public int RegionId { get; set; }

            public string Name { get; set; } = "";

            public string CountryCode { get; set; } = "";

            public List<Customer> Customers { get; set; } = [];
        }

        public class Customer
        {
            public long CustomerId { get; set; }

            public string Email { get; set; } = "";

            public string DisplayName { get; set; } = "";

            public int RegionId { get; set; }

            public Region Region { get; set; } = null!;

            public decimal? CreditLimit { get; set; }

            public DateOnly SignedUpOn { get; set; }

            public byte[] RowVersion { get; set; } = [];

            public bool IsPreferred { get; set; }

            public CustomerProfile? Profile { get; set; }

            public List<SalesOrder> Orders { get; set; } = [];
        }

        public class CustomerProfile
        {
            public long CustomerId { get; set; }

            public Customer Customer { get; set; } = null!;

            public string? Bio { get; set; }

            public string? AvatarUrl { get; set; }
        }
        """;

    /// <summary>The catalog side: a category hierarchy, products, tags and the junction entity between them.</summary>
    public const string Catalog = """
        namespace Marketplace;

        public class Category
        {
            public int CategoryId { get; set; }

            public string Name { get; set; } = "";

            public int? ParentCategoryId { get; set; }

            public Category? Parent { get; set; }

            public List<Category> Children { get; set; } = [];

            public List<Product> Products { get; set; } = [];
        }

        public class Product
        {
            public long ProductId { get; set; }

            public string Sku { get; set; } = "";

            public string Name { get; set; } = "";

            public int CategoryId { get; set; }

            public Category Category { get; set; } = null!;

            public decimal ListPrice { get; set; }

            public bool Discontinued { get; set; }

            public DateOnly? LaunchedOn { get; set; }

            public List<ProductTag> Tags { get; set; } = [];

            public List<OrderLine> OrderLines { get; set; } = [];
        }

        public class Tag
        {
            public int TagId { get; set; }

            public string Label { get; set; } = "";

            public List<ProductTag> Products { get; set; } = [];
        }

        public class ProductTag
        {
            public long ProductId { get; set; }

            public Product Product { get; set; } = null!;

            public int TagId { get; set; }

            public Tag Tag { get; set; } = null!;

            public DateTime TaggedAt { get; set; }
        }
        """;

    /// <summary>The order side: an order with a concurrency token, and its lines keyed by order and number.</summary>
    public const string Orders = """
        namespace Marketplace;

        public class SalesOrder
        {
            public long OrderId { get; set; }

            public string OrderNumber { get; set; } = "";

            public long CustomerId { get; set; }

            public Customer Customer { get; set; } = null!;

            public DateTime OrderedAt { get; set; }

            public string Status { get; set; } = "";

            public string PlacedFrom { get; set; } = "";

            public decimal ShippingCost { get; set; }

            public int Revision { get; set; }

            public List<OrderLine> Lines { get; set; } = [];
        }

        public class OrderLine
        {
            public long OrderId { get; set; }

            public short LineNumber { get; set; }

            public SalesOrder SalesOrder { get; set; } = null!;

            public long ProductId { get; set; }

            public Product Product { get; set; } = null!;

            public int Quantity { get; set; }

            public decimal UnitPrice { get; set; }

            public decimal? Discount { get; set; }
        }
        """;

    /// <summary>
    /// The context: a default schema, then one chain per fact over the entity builder. The
    /// order lines are configured in a class of their own, applied at the end.
    /// </summary>
    public const string Context = """
        using Microsoft.EntityFrameworkCore;

        namespace Marketplace;

        public class MarketplaceContext(DbContextOptions<MarketplaceContext> options) : DbContext(options)
        {
            public DbSet<Region> Regions { get; set; } = null!;
            public DbSet<Customer> Customers { get; set; } = null!;
            public DbSet<Category> Categories { get; set; } = null!;
            public DbSet<Product> Products { get; set; } = null!;
            public DbSet<Tag> Tags { get; set; } = null!;
            public DbSet<SalesOrder> SalesOrders { get; set; } = null!;
            public DbSet<OrderLine> OrderLines { get; set; } = null!;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.HasDefaultSchema("Market");

                modelBuilder.Entity<Region>().ToTable("Regions");
                modelBuilder.Entity<Region>().HasKey(r => r.RegionId);
                modelBuilder.Entity<Region>().Property(r => r.RegionId).ValueGeneratedNever();
                modelBuilder.Entity<Region>().Property(r => r.Name).HasMaxLength(100).IsRequired();
                modelBuilder.Entity<Region>().HasIndex(r => r.Name).IsUnique();
                modelBuilder.Entity<Region>().Property(r => r.CountryCode).HasColumnType("char(2)").IsRequired();

                modelBuilder.Entity<Customer>().ToTable("Customers");
                modelBuilder.Entity<Customer>().HasKey(c => c.CustomerId);
                modelBuilder.Entity<Customer>().Property(c => c.CustomerId).ValueGeneratedOnAdd();
                modelBuilder.Entity<Customer>().Property(c => c.Email).HasMaxLength(254).IsUnicode(false).IsRequired();
                modelBuilder.Entity<Customer>().HasAlternateKey(c => c.Email);
                modelBuilder.Entity<Customer>().Property(c => c.DisplayName).HasColumnName("Name").HasMaxLength(200).IsRequired();
                modelBuilder.Entity<Customer>().Property(c => c.CreditLimit).HasPrecision(12, 2);
                modelBuilder.Entity<Customer>().Property(c => c.RowVersion).IsRowVersion();
                modelBuilder.Entity<Customer>().Ignore(c => c.IsPreferred);
                modelBuilder.Entity<Customer>()
                    .HasOne(c => c.Region)
                    .WithMany(r => r.Customers)
                    .HasForeignKey(c => c.RegionId);
                modelBuilder.Entity<Customer>()
                    .HasOne(c => c.Profile)
                    .WithOne(p => p.Customer)
                    .HasForeignKey<CustomerProfile>(p => p.CustomerId);

                modelBuilder.Entity<CustomerProfile>().ToTable("CustomerProfiles");
                modelBuilder.Entity<CustomerProfile>().HasKey(p => p.CustomerId);
                modelBuilder.Entity<CustomerProfile>().Property(p => p.Bio).HasMaxLength(2000);
                modelBuilder.Entity<CustomerProfile>().Property(p => p.AvatarUrl).HasMaxLength(500).IsUnicode(false);

                modelBuilder.Entity<Category>().ToTable("Categories");
                modelBuilder.Entity<Category>().HasKey(c => c.CategoryId);
                modelBuilder.Entity<Category>().Property(c => c.CategoryId).ValueGeneratedNever();
                modelBuilder.Entity<Category>().Property(c => c.Name).HasMaxLength(100).IsRequired();
                modelBuilder.Entity<Category>()
                    .HasOne(c => c.Parent)
                    .WithMany(c => c.Children)
                    .HasForeignKey(c => c.ParentCategoryId);

                modelBuilder.Entity<Product>().ToTable("Products");
                modelBuilder.Entity<Product>().HasKey(p => p.ProductId);
                modelBuilder.Entity<Product>().Property(p => p.Sku).HasMaxLength(32).IsUnicode(false).IsRequired();
                modelBuilder.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
                modelBuilder.Entity<Product>().Property(p => p.Name).HasMaxLength(200).IsRequired();
                modelBuilder.Entity<Product>().Property(p => p.ListPrice).HasPrecision(10, 2);
                modelBuilder.Entity<Product>().Property(p => p.LaunchedOn).HasColumnType("date");
                modelBuilder.Entity<Product>()
                    .HasOne(p => p.Category)
                    .WithMany(c => c.Products)
                    .HasForeignKey(p => p.CategoryId);

                modelBuilder.Entity<Tag>().ToTable("Tags");
                modelBuilder.Entity<Tag>().HasKey(t => t.TagId);
                modelBuilder.Entity<Tag>().Property(t => t.Label).HasMaxLength(50).IsRequired();
                modelBuilder.Entity<Tag>().HasIndex(t => t.Label).IsUnique();

                modelBuilder.Entity<ProductTag>().ToTable("ProductTags");
                modelBuilder.Entity<ProductTag>().HasKey(pt => new { pt.ProductId, pt.TagId });
                modelBuilder.Entity<ProductTag>()
                    .HasOne(pt => pt.Product)
                    .WithMany(p => p.Tags)
                    .HasForeignKey(pt => pt.ProductId);
                modelBuilder.Entity<ProductTag>()
                    .HasOne(pt => pt.Tag)
                    .WithMany(t => t.Products)
                    .HasForeignKey(pt => pt.TagId);

                modelBuilder.Entity<SalesOrder>().ToTable("SalesOrders");
                modelBuilder.Entity<SalesOrder>().HasKey(o => o.OrderId);
                modelBuilder.Entity<SalesOrder>().Property(o => o.OrderNumber).HasMaxLength(20).IsUnicode(false).IsRequired();
                modelBuilder.Entity<SalesOrder>().HasIndex(o => new { o.CustomerId, o.OrderNumber }).IsUnique();
                modelBuilder.Entity<SalesOrder>().Property(o => o.OrderedAt).HasColumnType("datetime2(3)");
                modelBuilder.Entity<SalesOrder>().Property(o => o.Status).HasMaxLength(20).IsRequired();
                modelBuilder.Entity<SalesOrder>().Property(o => o.PlacedFrom).HasMaxLength(10).HasDefaultValueSql("'web'");
                modelBuilder.Entity<SalesOrder>().Property(o => o.ShippingCost).HasPrecision(8, 2);
                modelBuilder.Entity<SalesOrder>().Property(o => o.Revision).IsConcurrencyToken();
                modelBuilder.Entity<SalesOrder>()
                    .HasOne(o => o.Customer)
                    .WithMany(c => c.Orders)
                    .HasForeignKey(o => o.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);

                modelBuilder.ApplyConfiguration(new OrderLineConfiguration());
            }
        }
        """;

    /// <summary>A configuration class of its own: the composite key and the two relations of the order line.</summary>
    public const string OrderLineConfiguration = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;

        namespace Marketplace;

        public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
        {
            public void Configure(EntityTypeBuilder<OrderLine> builder)
            {
                builder.ToTable("OrderLines");
                builder.HasKey(l => new { l.OrderId, l.LineNumber });
                builder.Property(l => l.UnitPrice).HasPrecision(10, 2);
                builder.Property(l => l.Discount).HasPrecision(4, 3);
                builder
                    .HasOne(l => l.SalesOrder)
                    .WithMany(o => o.Lines)
                    .HasForeignKey(l => l.OrderId);
                builder
                    .HasOne(l => l.Product)
                    .WithMany(p => p.OrderLines)
                    .HasForeignKey(l => l.ProductId);
            }
        }
        """;

    /// <summary>Three queries in the code that runs them: a correlated subquery, a grouping over a join, a page under a nested filter.</summary>
    public const string Queries = """
        using Microsoft.EntityFrameworkCore;

        namespace Marketplace;

        public class MarketplaceQueries(MarketplaceContext ctx)
        {
            public List<Customer> BigSpendersIn(int regionId, decimal minimumShipping)
            {
                return ctx.Customers
                    .Where(c => c.RegionId == regionId
                        && ctx.SalesOrders.Any(o => o.CustomerId == c.CustomerId && o.ShippingCost >= minimumShipping))
                    .OrderBy(c => c.DisplayName)
                    .ToList();
            }

            public void UnitsPerProduct(DateTime since)
            {
                var rows = ctx.OrderLines
                    .Join(ctx.SalesOrders, l => l.OrderId, o => o.OrderId, (l, o) => new { l, o })
                    .Where(x => x.o.OrderedAt >= since && x.o.Status != "CANCELLED")
                    .GroupBy(x => x.l.ProductId)
                    .Where(g => g.Sum(x => x.l.Quantity) >= 10)
                    .Select(g => new
                    {
                        ProductId = g.Key,
                        Units = g.Sum(x => x.l.Quantity),
                        Revenue = g.Sum(x => x.l.Quantity * x.l.UnitPrice),
                        Orders = g.Select(x => x.o.OrderId).Distinct().Count(),
                    })
                    .OrderByDescending(r => r.Units)
                    .ToList();
            }

            public List<Product> TaggedProducts(string label, int skip, int take)
            {
                return ctx.Products
                    .Where(p => !p.Discontinued
                        && ctx.Set<ProductTag>()
                            .Where(pt => ctx.Tags.Where(t => t.Label == label).Select(t => t.TagId).Contains(pt.TagId))
                            .Select(pt => pt.ProductId)
                            .Contains(p.ProductId))
                    .OrderBy(p => p.Sku)
                    .Skip(skip)
                    .Take(take)
                    .ToList();
            }
        }
        """;
}
