using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace SampleData;

/// <summary>
/// The Hibernate sample (decision 077): the same Customer as the .NET samples, as a Java
/// entity with jakarta.persistence annotations, a query as a Java method over the
/// EntityManager and the same query as bare JPQL.
/// </summary>
public static class CustomerSampleHibernate
{
    public const string Entity = """
        package HibernateEntities;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.OneToMany;
        import jakarta.persistence.Table;
        import java.math.BigDecimal;
        import java.time.LocalDateTime;
        import java.util.ArrayList;
        import java.util.List;

        @Entity
        @Table(name = "Customers", schema = "Sales")
        public class Customer {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "CustomerID")
            private Integer CustomerID;

            @Column(name = "CustomerName", length = 200, nullable = false)
            private String CustomerName;

            @Column(name = "AccountOpenedDate", secondPrecision = 7, nullable = false)
            private LocalDateTime AccountOpenedDate;

            @Column(name = "CreditLimit", precision = 18, scale = 2, nullable = true)
            private BigDecimal CreditLimit;

            @OneToMany(mappedBy = "Customer")
            private List<CustomerTransaction> Transactions = new ArrayList<>();

            public Integer getCustomerID() {
                return CustomerID;
            }

            public void setCustomerID(Integer value) {
                this.CustomerID = value;
            }

            public String getCustomerName() {
                return CustomerName;
            }

            public void setCustomerName(String value) {
                this.CustomerName = value;
            }

            public LocalDateTime getAccountOpenedDate() {
                return AccountOpenedDate;
            }

            public void setAccountOpenedDate(LocalDateTime value) {
                this.AccountOpenedDate = value;
            }

            public BigDecimal getCreditLimit() {
                return CreditLimit;
            }

            public void setCreditLimit(BigDecimal value) {
                this.CreditLimit = value;
            }

            public List<CustomerTransaction> getTransactions() {
                return Transactions;
            }

            public void setTransactions(List<CustomerTransaction> value) {
                this.Transactions = value;
            }
        }
        """;

    /// <summary>
    /// The orm.xml descriptor for the same entity: the standard, implementation-neutral
    /// form of the mapping, read before the class because the specification puts it above
    /// the annotations (decision 068). Here it restates the table, so both units agree.
    /// </summary>
    public const string OrmXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
            <package>HibernateEntities</package>
            <entity class="Customer">
                <table name="Customers" schema="Sales"/>
                <attributes>
                    <id name="CustomerID">
                        <column name="CustomerID"/>
                        <generated-value strategy="IDENTITY"/>
                    </id>
                    <basic name="CustomerName">
                        <column name="CustomerName" length="200" nullable="false"/>
                    </basic>
                </attributes>
            </entity>
        </entity-mappings>
        """;

    /// <summary>
    /// Hibernate queries are read from two languages, told apart by the content type the
    /// unit declares (decisions 025 and 077): a Java method wrapping the JPQL in
    /// createQuery, or the bare JPQL - the same shape the tool itself emits.
    /// </summary>
    public const string Query = """"
        public static TypedQuery<Customer> query(EntityManager em) {
            return em.createQuery("""
                select c
                from Customer c
                where c.CreditLimit > 2000
                order by c.AccountOpenedDate desc, c.CustomerName asc
                """, Customer.class);
        }
        """";

    public const string JpqlQuery = """
        select c
        from Customer c
        where c.CreditLimit > 2000
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
                    Namespace = "HibernateEntities",
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
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "CustomerName",
                           Type = LangType.Scalar(ScalarType.String),
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true
                       },
                       ColumnName = "CustomerName",
                       Length = 200,
                       IsNullable = false,
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
                       ColumnName = "AccountOpenedDate",
                       Precision = 7,
                       IsNullable = false,
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
                       ColumnName = "CreditLimit",
                       Precision = 18,
                       Scale = 2,
                       IsNullable = true,
                   },
                   new() {
                       Property = new Property
                       {
                           Name = "Transactions",
                           Type = LangType.Collection(
                               LangType.Reference("CustomerTransaction", isNullable: true),
                               CollectionKind.List,
                               isNullable: true),
                           AccessModifier = AccessModifier.Public,
                           HasGetter = true,
                           HasSetter = true,
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
