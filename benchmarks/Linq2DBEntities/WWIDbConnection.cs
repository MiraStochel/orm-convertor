using System.Text.Json;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;

namespace linq2dbEntities;

public class WWIDbConnection(DataOptions options) : DataConnection(options.UseMappingSchema(DBMapping.Schema))
{
    public ITable<Customer> Customers => this.GetTable<Customer>();

    public ITable<PurchaseOrder> PurchaseOrders => this.GetTable<PurchaseOrder>();

    public ITable<Supplier> Suppliers => this.GetTable<Supplier>();

    public ITable<Order> Orders => this.GetTable<Order>();

    public ITable<OrderLine> OrderLines => this.GetTable<OrderLine>();

    public ITable<StockItem> StockItems => this.GetTable<StockItem>();

    public ITable<StockGroup> StockGroups => this.GetTable<StockGroup>();

    public ITable<Person> People => this.GetTable<Person>();
}

public static class DBMapping
{
    public static MappingSchema Schema { get; }

    static DBMapping()
    {
        Schema = new();

        var builder = new FluentMappingBuilder(Schema);

        builder.Entity<Customer>()
            .HasSchemaName("Sales")
            .HasTableName("Customers")
            .Association(c => c.Transactions, c => c.CustomerID, ct => ct.CustomerID, canBeNull: true);

        builder.Entity<CustomerTransaction>()
            .HasSchemaName("Sales")
            .HasTableName("CustomerTransactions");

        builder.Entity<PurchaseOrder>()
            .HasSchemaName("Purchasing")
            .HasTableName("PurchaseOrders");

        builder.Entity<Supplier>()
            .HasSchemaName("Purchasing")
            .HasTableName("Suppliers");

        builder.Entity<OrderLine>()
            .HasSchemaName("Sales")
            .HasTableName("OrderLines");

        builder.Entity<Order>()
            .HasSchemaName("Sales")
            .HasTableName("Orders")
            .HasPrimaryKey(x => x.OrderID)
            .Association(o => o.OrderLines, o => o.OrderID, ol => ol.OrderID, canBeNull: false);

        builder.Entity<PurchaseOrderUpdate>()
            .Property(p => p.OrderID).HasColumnName("WWI Order ID")
            .Property(p => p.Description).HasColumnName("Description")
            .Property(p => p.Quantity).HasColumnName("Quantity")
            .Property(p => p.UnitPrice).HasColumnName("Unit Price")
            .Property(p => p.TaxRate).HasColumnName("Tax Rate")
            .Property(p => p.TotalIncludingTax).HasColumnName("Total Including Tax");

        builder.Entity<StockItemStockGroup>()
            .HasSchemaName("Warehouse")
            .HasTableName("StockItemStockGroups")
            .Association(
                sisg => sisg.StockGroup,
                sisg => sisg.StockGroupID,
                sg => sg.StockGroupID,
                canBeNull: true
            )
            .Association(
                sisg => sisg.StockItem,
                sisg => sisg.StockItemID,
                si => si.StockItemID,
                canBeNull: true
            );

        builder.Entity<StockItem>()
            .HasSchemaName("Warehouse")
            .HasTableName("StockItems")
            .HasPrimaryKey(x => x.StockItemID)
            .Association(
                si => si.StockGroups,
                si => si.StockItemID,
                sisg => sisg.StockItemID,
                canBeNull: true
            );

        builder.Entity<StockGroup>()
            .HasSchemaName("Warehouse")
            .HasTableName("StockGroups")
            .HasPrimaryKey(x => x.StockGroupID)
            .Association(
                sg => sg.StockItems,
                sg => sg.StockGroupID,
                sisg => sisg.StockGroupID,
                canBeNull: true
            );

        builder.Entity<Person>()
            .HasSchemaName("Application")
            .HasTableName("People")
            .HasPrimaryKey(x => x.PersonID)
            .Property(x => x.CustomFields).HasDbType("NVARCHAR(MAX)")
            .Property(x => x.OtherLanguages).HasDbType("NVARCHAR(MAX)");

        // Converters from JSON string to types used in the Person entity
        builder.MappingSchema.SetConverter<string, CustomFields?>(
            str => JsonSerializer.Deserialize<CustomFields>(str));

        builder.MappingSchema.SetConverter<string, List<string>?>(
            str => JsonSerializer.Deserialize<List<string>>(str));

        builder.Build();
    }
}
