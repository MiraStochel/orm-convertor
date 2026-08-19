using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace SampleData;

public class CustomerSampleDapper
{
    public const string Entity = """
        namespace DapperEntities;

        public class Customer
        {
            public int CustomerID { get; set; }

            public required string CustomerName { get; set; }

            public DateTime AccountOpenedDate { get; set; }

            public decimal? CreditLimit { get; set; }

            public List<CustomerTransaction> Transactions { get; set; } = [];

        }
        
        """;
    /// <summary>
    /// A Dapper query is T-SQL. It names the table outright, because a Dapper source
    /// carries no mapping metadata of its own to resolve it from.
    /// </summary>
    public const string Query = """
        SELECT c.CustomerName, c.CreditLimit
        FROM Sales.Customers AS c
        WHERE c.CreditLimit > 2000
        ORDER BY c.AccountOpenedDate DESC
        """;

    public static EntityMap Map
    {
        get
        {
            var map = new EntityMap
            {
                Entity = new Entity
                {
                    Name = "Customer",
                    Namespace = "DapperEntities",
                    AccessModifier = AccessModifier.Public,
                },
                Table = default,
                Schema = default,
                PropertyMaps = [
                    new() {
                       Property = new Property
                       {
                           Name = "CustomerID",
                           Type = LangType.Scalar(ScalarType.Int),
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true,
                       },
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "CustomerName",
                           Type = LangType.Scalar(ScalarType.String),
                           AccessModifier = AccessModifier.Public,
                           OtherModifiers = ["required"],
                           HasGetter = true,
                           HasSetter = true
                       },
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "AccountOpenedDate",
                           Type = LangType.Scalar(ScalarType.DateTime),
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true
                       },
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "CreditLimit",
                           Type = LangType.Scalar(ScalarType.Decimal, isNullable: true),
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true
                       }
                   },
                   new() {
                       // Dapper records no relations, so the element type stays what the C#
                       // declaration alone can say: an unrecognized name, not a reference.
                       Property = new Property
                       {
                           Name = "Transactions",
                           Type = LangType.Collection(
                               LangType.Unknown("CustomerTransaction"),
                               CollectionKind.List),
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true,
                           DefaultValue = "[]",
                       },
                   },
               ],
            };

            foreach (var propertyMap in map.PropertyMaps)
            {
                map.Entity.Properties.Add(propertyMap.Property);
            }

            return map;
        }
    }
}
