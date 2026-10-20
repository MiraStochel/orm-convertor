using System.Reflection;
using Common.Compilation;
using Microsoft.EntityFrameworkCore;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of a DbSet property by the way it is written, measured over a model it
/// builds without a connection - the fact the reading of a DbSet member's value rests on
/// (decision 158): a property declared DbSet&lt;T&gt; is a set of the context whatever supplies
/// its value - an auto-property EF Core assigns, an expression body over Set&lt;T&gt;(), a
/// getter with a body, a get-only property the constructor assigns -, the table is named after
/// it in every one of those spellings, and a query over it reads that table. A field of the same
/// type declares nothing: its entity is not in the model.
/// </summary>
public class EFCoreDbSetDeclarationProviderTest
{
    private const string Source = """
        using System.ComponentModel.DataAnnotations;
        using Microsoft.EntityFrameworkCore;

        namespace Declared;

        public class Customer { [Key] public int CustomerId { get; set; } }
        public class Order { [Key] public int OrderId { get; set; } }
        public class OrderLine { [Key] public int OrderLineId { get; set; } }
        public class Product { [Key] public int ProductId { get; set; } }
        public class Supplier { [Key] public int SupplierId { get; set; } }

        public class ShopContext : DbContext
        {
            private readonly DbSet<Supplier> suppliers;

            public ShopContext(DbContextOptions options)
                : base(options)
            {
                Lines = Set<OrderLine>();
                suppliers = Set<Supplier>();
            }

            public DbSet<Customer> Customers { get; set; } = null!;

            public DbSet<Order> Orders => Set<Order>();

            public DbSet<OrderLine> Lines { get; }

            public DbSet<Product> Products
            {
                get { return Set<Product>(); }
            }
        }
        """;

    [Theory]
    [InlineData("Customer", "Customers")]
    [InlineData("Order", "Orders")]
    [InlineData("OrderLine", "Lines")]
    [InlineData("Product", "Products")]
    public void APropertyDeclaredDbSetNamesTheTableWhateverSuppliesItsValue(string entity, string table)
    {
        using var context = Context();

        var type = context.Model.FindEntityType("Declared." + entity);

        Assert.NotNull(type);
        Assert.Equal(table, type.GetTableName());
    }

    [Fact]
    public void AFieldOfTheSameTypeDeclaresNoSet()
    {
        using var context = Context();

        Assert.Null(context.Model.FindEntityType("Declared.Supplier"));
    }

    [Fact]
    public void AQueryOverTheExpressionBodiedSetReadsItsTable()
    {
        using var context = Context();

        var orders = (IQueryable)context.GetType().GetProperty("Orders")!.GetValue(context)!;

        Assert.Contains("FROM [Orders] AS [o]", orders.ToQueryString(), StringComparison.Ordinal);
    }

    private static DbContext Context()
    {
        var result = CSharpSourceCompiler.Compile(
            "EFCoreDbSetDeclaration",
            [Source],
            GeneratedQueryCompiler.EFCoreConsumerReferences);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors.Select(e => e.ToString())));

        var assembly = Assembly.Load(result.Assembly!);
        var options = new DbContextOptionsBuilder()
            .UseSqlServer()
            .EnableServiceProviderCaching(false)
            .Options;

        return (DbContext)Activator.CreateInstance(assembly.GetType("Declared.ShopContext")!, options)!;
    }
}
