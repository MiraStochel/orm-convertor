namespace SampleData;

/// <summary>
/// The marketplace in NHibernate: the classes in one C# file, a mapping document per class,
/// and the queries in the three places an NHibernate project keeps them - named queries in a
/// mapping document (HQL in &lt;query&gt;, SQL in &lt;sql-query&gt;, decision 081), a bare
/// HQL file, and code that hands HQL to the session with a page on the query object.
///
/// The mappings state what hbm.xml can: four generators (assigned, identity, a sequence with
/// its name, hi/lo with its table), a composite key whose first part is a key-many-to-one,
/// a version, a literal column type, unique columns and a unique key over a column and a
/// relation, a many-to-many over a join table, and inverse collections. The classes have no
/// foreign-key properties, as NHibernate classes usually do not, so the HQL joins along the
/// associations and the tool derives each join condition from the mapping (decision 101).
///
/// The tables live in the schema Market, as in the other marketplace samples.
/// </summary>
public static class MarketplaceSampleNHibernate
{
    /// <summary>The classes, every member virtual as NHibernate requires, the composite key with its identity members.</summary>
    public const string Entities = """
        using System;
        using System.Collections.Generic;

        namespace Marketplace;

        public class Region
        {
            public virtual int RegionId { get; set; }

            public virtual string Name { get; set; } = "";

            public virtual string CountryCode { get; set; } = "";

            public virtual ISet<Customer> Customers { get; set; } = new HashSet<Customer>();
        }

        public class Customer
        {
            public virtual long CustomerId { get; set; }

            public virtual int Revision { get; set; }

            public virtual string Email { get; set; } = "";

            public virtual string DisplayName { get; set; } = "";

            public virtual Region Region { get; set; } = null!;

            public virtual decimal? CreditLimit { get; set; }

            public virtual DateTime SignedUpOn { get; set; }

            public virtual IList<SalesOrder> Orders { get; set; } = new List<SalesOrder>();
        }

        public class Tag
        {
            public virtual int TagId { get; set; }

            public virtual string Label { get; set; } = "";

            public virtual ISet<Product> Products { get; set; } = new HashSet<Product>();
        }

        public class Product
        {
            public virtual long ProductId { get; set; }

            public virtual string Sku { get; set; } = "";

            public virtual string Name { get; set; } = "";

            public virtual decimal ListPrice { get; set; }

            public virtual bool Discontinued { get; set; }

            public virtual ISet<Tag> Tags { get; set; } = new HashSet<Tag>();
        }

        public class SalesOrder
        {
            public virtual long OrderId { get; set; }

            public virtual string OrderNumber { get; set; } = "";

            public virtual Customer Customer { get; set; } = null!;

            public virtual DateTime OrderedAt { get; set; }

            public virtual string Status { get; set; } = "";

            public virtual decimal ShippingCost { get; set; }

            public virtual IList<OrderLine> Lines { get; set; } = new List<OrderLine>();
        }

        [Serializable]
        public class OrderLine
        {
            public virtual SalesOrder SalesOrder { get; set; } = null!;

            public virtual short LineNumber { get; set; }

            public virtual Product Product { get; set; } = null!;

            public virtual int Quantity { get; set; }

            public virtual decimal UnitPrice { get; set; }

            public override bool Equals(object? obj)
            {
                return obj is OrderLine other
                    && Equals(SalesOrder?.OrderId, other.SalesOrder?.OrderId)
                    && LineNumber == other.LineNumber;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(SalesOrder?.OrderId, LineNumber);
            }
        }
        """;

    /// <summary>An assigned key, a unique column, a literal column type and the inverse side of the customers.</summary>
    public const string RegionMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Marketplace">
            <class name="Region" table="Regions" schema="Market">
                <id name="RegionId" column="RegionId" type="Int32">
                    <generator class="assigned" />
                </id>
                <property name="Name" not-null="true" type="String" length="100" unique="true" />
                <property name="CountryCode" not-null="true">
                    <column name="CountryCode" sql-type="char(2)" />
                </property>
                <set name="Customers" inverse="true">
                    <key column="RegionId" />
                    <one-to-many class="Customer" />
                </set>
            </class>
        </hibernate-mapping>
        """;

    /// <summary>An identity key, a version, a non-national unique column, a column named apart from its property.</summary>
    public const string CustomerMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Marketplace">
            <class name="Customer" table="Customers" schema="Market">
                <id name="CustomerId" column="CustomerId" type="Int64">
                    <generator class="identity" />
                </id>
                <version name="Revision" column="Revision" type="Int32" />
                <property name="Email" not-null="true" type="AnsiString" length="254" unique="true" />
                <property name="DisplayName" column="Name" not-null="true" type="String" length="200" />
                <many-to-one name="Region" class="Region" column="RegionId" />
                <property name="CreditLimit" not-null="false" type="Decimal" precision="12" scale="2" />
                <property name="SignedUpOn" not-null="true" type="Date" />
                <bag name="Orders" inverse="true">
                    <key column="CustomerId" />
                    <one-to-many class="SalesOrder" />
                </bag>
            </class>
        </hibernate-mapping>
        """;

    /// <summary>The inverse side of the many-to-many.</summary>
    public const string TagMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Marketplace">
            <class name="Tag" table="Tags" schema="Market">
                <id name="TagId" column="TagId" type="Int32">
                    <generator class="assigned" />
                </id>
                <property name="Label" not-null="true" type="String" length="50" unique="true" />
                <set name="Products" table="ProductTags" schema="Market" inverse="true">
                    <key column="TagId" />
                    <many-to-many class="Product" column="ProductId" />
                </set>
            </class>
        </hibernate-mapping>
        """;

    /// <summary>A sequence with its name, a named unique key, and the owning side of the many-to-many over its join table.</summary>
    public const string ProductMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Marketplace">
            <class name="Product" table="Products" schema="Market">
                <id name="ProductId" column="ProductId" type="Int64">
                    <generator class="sequence">
                        <param name="sequence">ProductIds</param>
                    </generator>
                </id>
                <property name="Sku" not-null="true" type="AnsiString" length="32" unique-key="UQ_Products_Sku" />
                <property name="Name" not-null="true" type="String" length="200" />
                <property name="ListPrice" not-null="true" type="Decimal" precision="10" scale="2" />
                <property name="Discontinued" not-null="true" type="Boolean" />
                <set name="Tags" table="ProductTags" schema="Market">
                    <key column="ProductId" />
                    <many-to-many class="Tag" column="TagId" />
                </set>
            </class>
        </hibernate-mapping>
        """;

    /// <summary>A hi/lo generator with its table, and a unique key over a column and a relation.</summary>
    public const string SalesOrderMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Marketplace">
            <class name="SalesOrder" table="SalesOrders" schema="Market">
                <id name="OrderId" column="OrderId" type="Int64">
                    <generator class="hilo">
                        <param name="table">OrderIdBlocks</param>
                        <param name="column">NextHigh</param>
                        <param name="max_lo">100</param>
                    </generator>
                </id>
                <property name="OrderNumber" not-null="true" type="AnsiString" length="20" unique-key="UQ_SalesOrders_CustomerNumber" />
                <many-to-one name="Customer" class="Customer" column="CustomerId" unique-key="UQ_SalesOrders_CustomerNumber" />
                <property name="OrderedAt" not-null="true" type="DateTime" />
                <property name="Status" not-null="true" type="String" length="20" />
                <property name="ShippingCost" not-null="true" type="Decimal" precision="8" scale="2" />
                <bag name="Lines" inverse="true">
                    <key column="OrderId" />
                    <one-to-many class="OrderLine" />
                </bag>
            </class>
        </hibernate-mapping>
        """;

    /// <summary>A composite key whose first part is the order itself, through key-many-to-one.</summary>
    public const string OrderLineMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Marketplace">
            <class name="OrderLine" table="OrderLines" schema="Market">
                <composite-id>
                    <key-many-to-one name="SalesOrder" class="SalesOrder" column="OrderId" />
                    <key-property name="LineNumber" column="LineNumber" type="Int16" />
                </composite-id>
                <many-to-one name="Product" class="Product" column="ProductId" />
                <property name="Quantity" not-null="true" type="Int32" />
                <property name="UnitPrice" not-null="true" type="Decimal" precision="10" scale="2" />
            </class>
        </hibernate-mapping>
        """;

    /// <summary>
    /// Two named queries in a mapping document of their own: HQL that joins along two
    /// associations, and SQL with its parameter typed by &lt;query-param&gt;. The name of each
    /// is the name of the method the target generates.
    /// </summary>
    public const string NamedQueries = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Marketplace">
            <query name="OpenOrdersOfRegion">
                <query-param name="regionId" type="Int32" />
                <![CDATA[
                select o.OrderNumber as OrderNumber, c.DisplayName as Customer, o.OrderedAt as OrderedAt
                from SalesOrder o
                    join o.Customer c
                    join c.Region r
                where r.RegionId = :regionId and o.Status not in ('SHIPPED', 'CANCELLED')
                order by o.OrderedAt asc
                ]]>
            </query>
            <sql-query name="RevenueByTag">
                <query-param name="since" type="DateTime" />
                <![CDATA[
                SELECT t.Label AS Label, SUM(l.Quantity * l.UnitPrice) AS Revenue, COUNT(DISTINCT o.OrderId) AS Orders
                FROM Market.Tags AS t
                JOIN Market.ProductTags AS pt ON pt.TagId = t.TagId
                JOIN Market.OrderLines AS l ON l.ProductId = pt.ProductId
                JOIN Market.SalesOrders AS o ON o.OrderId = l.OrderId
                WHERE o.OrderedAt >= :since
                GROUP BY t.Label
                HAVING SUM(l.Quantity) > 0
                ORDER BY Revenue DESC
                ]]>
            </sql-query>
        </hibernate-mapping>
        """;

    /// <summary>A bare HQL file: a join along an association, a scalar subquery, a filter over the groups.</summary>
    public const string BestSellersQuery = """
        select p.Sku as Sku, p.Name as Name, sum(l.Quantity) as Units
        from OrderLine l
            join l.Product p
        where p.Discontinued = false
          and l.UnitPrice >= (select avg(p2.ListPrice) from Product p2)
        group by p.Sku, p.Name
        having sum(l.Quantity) >= :minimumUnits
        order by sum(l.Quantity) desc
        """;

    /// <summary>HQL handed to the session in code, with the page on the query object.</summary>
    public const string OrderQueries = """
        using System.Collections.Generic;
        using NHibernate;

        namespace Marketplace;

        public class OrderQueries(ISession session)
        {
            public IList<SalesOrder> CostlyShipments(decimal minimumShipping, int skip, int take)
            {
                return session.CreateQuery("from SalesOrder o where o.ShippingCost >= :minimumShipping order by o.OrderedAt desc")
                    .SetParameter("minimumShipping", minimumShipping)
                    .SetFirstResult(skip)
                    .SetMaxResults(take)
                    .List<SalesOrder>();
            }
        }
        """;
}
