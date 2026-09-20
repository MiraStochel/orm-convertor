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

            public virtual IList<CustomerTransaction> Transactions { get; set; } = new List<CustomerTransaction>();

        }
        
        """;

    public const string XmlMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="Customer" table="Customers" schema="Sales">
                <id name="CustomerID" column="CustomerID" type="Int32">
                    <generator class="identity" />
                </id>
                <property name="CustomerName" not-null="true" type="String" length="200" />
                <property name="AccountOpenedDate" not-null="true" type="DateTime" precision="7" />
                <property name="CreditLimit" not-null="false" type="Decimal" precision="18" scale="2" />
                <bag name="Transactions">
                    <key column="CustomerID" />
                    <one-to-many class="CustomerTransaction" />
                </bag>
            </class>
        </hibernate-mapping>
        """;
    /// <summary>
    /// NHibernate queries are read from two languages, told apart by the content type the
    /// unit declares (decisions 025 and 062): a LINQ chain rooted in
    /// session.Query&lt;T&gt;(), or bare HQL — the same shape the tool itself emits.
    ///
    /// Both spell the filter with a parameter (decision 083), which is the shape a real
    /// query has: the value comes from the caller, the generated method of every target
    /// declares it typed from the column it is compared against, and each target writes its
    /// own placeholder and its own binding. The mapping states the table, which is what lets
    /// the scalar be derived without reaching the catalog - a Dapper source cannot, and that
    /// is why the sample lives here.
    /// </summary>
    public const string Query = """
        public List<Customer> Query(decimal minimumCreditLimit)
        {
            return session.Query<Customer>()
               .Where(c => c.CreditLimit > minimumCreditLimit)
               .OrderByDescending(c => c.AccountOpenedDate)
               .ThenBy(c => c.CustomerName)
               .ToList();
        }
        """;

    public const string HqlQuery = """
        from Customer c
        where c.CreditLimit > :minimumCreditLimit
        order by c.AccountOpenedDate desc, c.CustomerName asc
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
                           Type = LangType.Scalar(ScalarType.Int),
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true,
                       },
                       ColumnName = "CustomerID",
                       Type = DatabaseType.Integer,
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
                       IsNullable = false,
                       Type = DatabaseType.VarChar,
                       IsUnicode = true,
                       Length = 200
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
                       IsNullable = false,
                       Type = DatabaseType.Timestamp,
                       Precision = 7
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "CreditLimit",
                           Type = LangType.Scalar(ScalarType.Decimal, isNullable: true),
                           AccessModifier = AccessModifier.Public,
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
                           Type = LangType.Collection(
                               LangType.Reference("CustomerTransaction"),
                               CollectionKind.List),
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true,
                           DefaultValue = "new List<CustomerTransaction>()",
                       },
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
