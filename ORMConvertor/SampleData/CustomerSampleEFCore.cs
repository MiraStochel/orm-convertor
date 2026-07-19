using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace SampleData;

public static class CustomerSampleEFCore
{
    public const string Entity = """
        namespace EFCoreEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key]
            public required int CustomerID { get; set; }

            [MaxLength(200)]
            public required string CustomerName { get; set; }

            [Column(TypeName="datetime2")]
            [Precision(7)]
            public required DateTime AccountOpenedDate { get; set; }

            [Column(TypeName="decimal")]
            [Precision(18, 2)]
            public decimal? CreditLimit { get; set; }

            public List<CustomerTransaction> Transactions { get; set; } = [];

        }
        
        """;

    public const string Query = """
        public List<Customer> Query()
        {
            return ctx.Customers
               .Where(c => c.CreditLimit > 2000)
               .Where(c => c.AccountOpenedDate > new System.DateTime(2025, 1, 1))
               .OrderByDescending(c => c.AccountOpenedDate)
               .ThenBy(c => c.CustomerName)
               .ToList();
        }
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
                    Namespace = "EFCoreEntities",
                    AccessModifier = AccessModifier.Public,
                },
                Table = "Customers",
                Schema = "Sales",
                PropertyMaps = [
                    new() {
                       Property = new Property
                       {
                           Name = "CustomerID",
                           Type = new CLRTypeModel(){ CLRType = CLRType.Int },
                           AccessModifier = AccessModifier.Public,
                           OtherModifiers = ["required"],
                           HasGetter = true,
                           HasSetter = true,
                       },
                       IsNullable = false,
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "CustomerName",
                           Type = new CLRTypeModel(){ CLRType = CLRType.String },
                           AccessModifier = AccessModifier.Public,
                           OtherModifiers = ["required"],
                           HasGetter = true,
                           HasSetter = true
                       },
                       Length = 200,
                       IsNullable = false,
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "AccountOpenedDate",
                           Type = new CLRTypeModel(){ CLRType = CLRType.DateTime },
                           AccessModifier = AccessModifier.Public,
                           OtherModifiers = ["required"],
                           HasGetter = true,
                           HasSetter = true
                       },
                       Precision = 7,
                       IsNullable = false,
                       Type = DatabaseType.DateTime2
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "CreditLimit",
                           Type = new CLRTypeModel(){ CLRType = CLRType.Decimal },
                           IsNullable = true,
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true
                       },
                       Precision = 18,
                       Scale = 2,
                       IsNullable = true,
                       Type = DatabaseType.Decimal
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "Transactions",
                           Type = new CLRTypeModel(){ CLRType = CLRType.List, GenericParam = "CustomerTransaction" },
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true,
                           DefaultValue = "[]",
                       },
                       IsNullable = false,
                   },
               ],
            };

            foreach (var propertyMap in map.PropertyMaps)
            {
                map.Entity.Properties.Add(propertyMap.Property);
            }

            map.PrimaryKey = new PrimaryKey
            {
                Parts =
                [
                    new PrimaryKeyPart
                    {
                        PropertyMap = map.PropertyMaps.First(pm => pm.Property.Name == "CustomerID"),
                        Order = 1,
                        Strategy = PrimaryKeyStrategy.Identity,
                    },
                ],
            };

            map.Relations.Add(new Relation
            {
                Cardinality = Cardinality.OneToMany,
                Role = RelationRole.Inverse,
                SourceEntity = "Customer",
                TargetEntity = "CustomerTransaction",
                SourceNavigationProperty = "Transactions",
            });

            return map;
        }
    }
}
