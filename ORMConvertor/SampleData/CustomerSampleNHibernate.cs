using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace SampleData;
public class CustomerSampleNHibernate
{
    public const string Entity = """
        namespace NHibernateEntities;

        public class Customer
        {
            public virtual int CustomerID { get; set; }

            public virtual required string CustomerName { get; set; }

            public virtual DateTime AccountOpenedDate { get; set; }

            public virtual decimal? CreditLimit { get; set; }

            public virtual List<CustomerTransaction> Transactions { get; set; } = [];

        }
        
        """;

    public const string XmlMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.Customer, NHibernateEntities" table="Customers" schema="Sales">
                <id name="CustomerID" column="CustomerID" type="Int32">
                    <generator class="identity" />
                </id>
                <property name="CustomerName" not-null="true" type="String" length="200" />
                <property name="AccountOpenedDate" not-null="true" type="DateTime" precision="7" />
                <property name="CreditLimit" not-null="false" type="Decimal" precision="18" scale="2" />
                <bag name="Transactions" inverse="true" cascade="all-delete-orphan">
                    <key column="CustomerID" />
                    <one-to-many class="CustomerTransaction" />
                </bag>
            </class>
        </hibernate-mapping>
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
                    Namespace = "NHibernateEntities",
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
                           OtherModifiers = ["virtual"],
                           HasGetter = true,
                           HasSetter = true,
                       },
                       OtherDatabaseProperties = new Dictionary<string, string>
                       {
                           { "IsPrimaryKey", "true" },
                           { "PrimaryKeyStrategy", ((int)PrimaryKeyStrategy.Identity).ToString() }
                       }
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "CustomerName",
                           Type = new CLRTypeModel(){ CLRType = CLRType.String },
                           AccessModifier = AccessModifier.Public,
                           OtherModifiers = ["virtual", "required"],
                           HasGetter = true,
                           HasSetter = true
                       },
                       IsNullable = false,
                       Type = DatabaseType.NVarChar,
                       Length = 200
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "AccountOpenedDate",
                           Type = new CLRTypeModel(){ CLRType = CLRType.DateTime },
                           AccessModifier = AccessModifier.Public,
                           OtherModifiers = ["virtual"],
                           HasGetter = true,
                           HasSetter = true
                       },
                       IsNullable = false,
                       Type = DatabaseType.DateTime2,
                       Precision = 7
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "CreditLimit",
                           Type = new CLRTypeModel(){ CLRType = CLRType.Decimal },
                           IsNullable = true,
                           AccessModifier = AccessModifier.Public,
                           OtherModifiers = ["virtual"],
                           HasGetter = true,
                           HasSetter = true
                       },
                       IsNullable = true,
                       Type = DatabaseType.Decimal,
                       Precision = 18,
                       Scale = 2
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "Transactions",
                           Type = new CLRTypeModel(){ CLRType = CLRType.List, GenericParam = "CustomerTransaction" },
                           AccessModifier = AccessModifier.Public,
                           OtherModifiers = ["virtual"],
                           HasGetter = true,
                           HasSetter = true,
                           DefaultValue = "[]",
                       },
                       Relation = new() {
                           Cardinality = Cardinality.OneToMany,
                           Source = "Customer",
                           Target = "CustomerTransaction",
                       },
                       OtherDatabaseProperties = new Dictionary<string, string>
                       {
                           { "IsForeignKey", "true" },
                           { "ForeignKeyCardinality", ((int)Cardinality.OneToMany).ToString() }
                       }
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

            return map;
        }
    }
}
