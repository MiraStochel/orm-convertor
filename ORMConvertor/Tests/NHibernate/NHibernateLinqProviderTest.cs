using Tests.Verification;

namespace Tests.NHibernate;

/// <summary>
/// What the LINQ provider of NHibernate 5.7.0 translates and what it refuses, measured by its
/// own query plan over a mapped model without a connection (the third level of decision 027).
/// These are the facts the hooks of <c>NHibernateLinqQueryBuilder</c> and
/// <c>NHibernateLinqQueryVisitor</c> rest on (decision 118): which join operators it knows,
/// where an ordering under DISTINCT may stand, how a COUNT over a column that may hold NULL
/// is spelled, which aggregates it takes over distinct values, that its Sum is the bare sum
/// (decision 155), that it compensates == and != for NULL but writes a negation as SQL's NOT
/// and Contains over a subquery as IN (decision 156), that its Average over a whole number is
/// the average in floating point and cast into int SQL's AVG again (decision 161). A fact the provider stops
/// holding on an upgrade shows up here, beside the hook that assumes it.
/// </summary>
public class NHibernateLinqProviderTest
{
    private const string Entities = """
        namespace Probe;

        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
            public virtual decimal? CreditLimit { get; set; }
            public virtual int? Score { get; set; }
        }

        public class Order
        {
            public virtual int OrderId { get; set; }
            public virtual int CustomerId { get; set; }
            public virtual int? Rating { get; set; }
            public virtual decimal Total { get; set; }
        }
        """;

    private const string Mapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Probe">
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32"><generator class="identity" /></id>
            <property name="CustomerName" type="String" />
            <property name="CreditLimit" type="Decimal" />
            <property name="Score" type="Int32" />
          </class>
          <class name="Order" table="Orders" schema="Sales">
            <id name="OrderId" column="OrderId" type="Int32"><generator class="identity" /></id>
            <property name="CustomerId" type="Int32" />
            <property name="Rating" type="Int32" />
            <property name="Total" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;

    private const string LeftJoin =
        "session.Query<Customer>().GroupJoin(session.Query<Order>(), c => c.CustomerId, o => o.CustomerId, (c, oGroup) => new { c, oGroup })"
        + ".SelectMany(pair => pair.oGroup.DefaultIfEmpty(), (pair, o) => new { pair.c, o })";

    private const string InnerJoin =
        "session.Query<Customer>().Join(session.Query<Order>(), c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o })";

    /// <summary>The chains the builder writes and the SQL the provider makes of each: the hallmark is the clause the shape stands for.</summary>
    public static TheoryData<string, string, string> Translated => new()
    {
        { "inner join", InnerJoin + ".Select(t => new { t.c.CustomerName, t.o.Total })", "inner join Sales.Orders" },
        { "left join as GroupJoin with DefaultIfEmpty", LeftJoin + ".Select(t => new { t.c.CustomerName, Total = (decimal?)t.o.Total })", "left outer join Sales.Orders" },
        { "left join then group", LeftJoin + ".GroupBy(t => t.c.CustomerName).Select(g => new { Name = g.Key, Orders = g.Count() })", "left outer join Sales.Orders" },
        { "filtered joined sequence", "session.Query<Customer>().Join(session.Query<Order>().Where(o => o.Total > 5m), c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o }).Select(t => new { t.c.CustomerName })", "inner join (select" },
        { "correlated SelectMany", "session.Query<Customer>().SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.CustomerId && o.Total > c.CreditLimit), (c, o) => new { c, o }).Select(t => new { t.c.CustomerName })", "cross join Sales.Orders" },
        { "correlated SelectMany with DefaultIfEmpty", "session.Query<Customer>().SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.CustomerId && o.Total > c.CreditLimit).DefaultIfEmpty(), (c, o) => new { c, o }).Select(t => new { t.c.CustomerName })", "left outer join Sales.Orders" },
        // A subquery in the condition of a join, as the correlated SelectMany carries it (decision 162).
        { "correlated SelectMany with a subquery in its predicate", "session.Query<Customer>().SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.CustomerId && session.Query<Order>().Where(i => i.Rating > 3).Select(i => i.CustomerId).Contains(c.CustomerId)), (c, o) => new { c, o }).Select(t => new { t.c.CustomerName })", "in (select" },
        { "correlated SelectMany with DefaultIfEmpty and Any in its predicate", "session.Query<Customer>().SelectMany(c => session.Query<Order>().Where(o => o.CustomerId == c.CustomerId && session.Query<Order>().Where(i => i.CustomerId == c.CustomerId && i.Rating > 3).Any()).DefaultIfEmpty(), (c, o) => new { c, o }).Select(t => new { t.c.CustomerName })", "exists (select" },
        { "ordering before Distinct", "session.Query<Customer>().OrderBy(c => c.CustomerName).Select(c => new { c.CustomerName }).Distinct()", "select distinct customer0_.CustomerName as col_0_0_ from Sales.Customers customer0_ order by customer0_.CustomerName asc" },
        { "ordering before Distinct with a slice", "session.Query<Customer>().OrderBy(c => c.CustomerName).Select(c => new { c.CustomerName }).Distinct().Skip(1).Take(2)", "order by customer0_.CustomerName asc OFFSET" },
        { "ordering by an alias after the projection", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { Id = g.Key, Count = g.Count() }).OrderByDescending(p => p.Count)", "order by (count(*)) desc" },
        { "ordering over the group before the projection under a slice", "session.Query<Order>().GroupBy(o => o.CustomerId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new { Id = g.Key, Count = g.Count() }).Take(3)", "order by (count(*)) desc, order0_.CustomerId asc OFFSET 0 ROWS FETCH FIRST ? ROWS ONLY" },
        { "ordering over a group of join tuples by a part of its key", InnerJoin + ".GroupBy(t => new { t.c.CustomerId, t.c.CustomerName }).OrderByDescending(g => g.Select(e => e.o.OrderId).Distinct().Count()).ThenBy(g => g.Key.CustomerId).Select(g => new { Name = g.Key.CustomerName, Orders = g.Select(e => e.o.OrderId).Distinct().Count() }).Take(3)", "order by (count(distinct order1_.OrderId)) desc, customer0_.CustomerId asc OFFSET 0 ROWS FETCH FIRST ? ROWS ONLY" },
        { "COUNT of non-null values as a Sum over a conditional", InnerJoin + ".GroupBy(t => t.c.CustomerName).Select(g => new { Name = g.Key, Rated = g.Sum(e => e.o.Rating != null ? 1 : 0) })", "sum((case when order1_.Rating is not null then" },
        { "COUNT of matched rows of a left join", LeftJoin + ".GroupBy(t => t.c.CustomerName).Select(g => new { Name = g.Key, Orders = g.Sum(e => e.o != null ? 1 : 0) })", "case when order1_.OrderId is not null" },
        { "COUNT over distinct values", InnerJoin + ".GroupBy(t => t.c.CustomerName).Select(g => new { Name = g.Key, D = g.Select(e => e.o.Rating).Distinct().Count() })", "count(distinct order1_.Rating)" },
        { "Like with an escape", "session.Query<Customer>().Where(c => c.CustomerName.Like(\"A!_%\", '!'))", "like ? escape '!'" },
        { "IN over a subquery", "session.Query<Customer>().Where(c => session.Query<Order>().Select(o => o.CustomerId).Distinct().Contains(c.CustomerId))", "in (select distinct" },
        { "EXISTS", "session.Query<Customer>().Where(c => session.Query<Order>().Where(o => o.CustomerId == c.CustomerId).Any())", "where exists (select" },
        { "scalar subquery", "session.Query<Customer>().Where(c => c.CreditLimit > session.Query<Order>().Where(o => o.CustomerId == c.CustomerId).Max(o => o.Total))", "(select max(order1_.Total)" },
        { "Sum in a subquery as the bare sum", "session.Query<Customer>().Where(c => c.CreditLimit > session.Query<Order>().Where(o => o.CustomerId == c.CustomerId).Sum(o => o.Total))", "where customer0_.CreditLimit>(select (sum(order1_.Total)) from" },
        { "Sum over a group as the bare sum", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, S = g.Sum(e => e.Rating) })", "select order0_.CustomerId as col_0_0_, cast(sum(order0_.Rating) as INT) as col_1_0_" },
        { "inequality with a constant compensated", "session.Query<Customer>().Where(c => c.Score != 5)", "where customer0_.Score<>? or customer0_.Score is null" },
        { "equality of two nullable values compensated", "session.Query<Customer>().Where(c => c.Score == c.CreditLimit)", "or (cast(customer0_.Score as DECIMAL(29, 10)) is null) and (customer0_.CreditLimit is null)" },
        { "negation as SQL's NOT", "session.Query<Customer>().Where(c => !(c.Score > 5))", "where  not (customer0_.Score>?)" },
        { "Contains over a subquery as IN", "session.Query<Customer>().Where(c => session.Query<Order>().Select(o => o.Rating).Contains(c.Score))", "where customer0_.Score in (select order1_.Rating from Sales.Orders order1_)" },
        { "inequality tested for NULL", "session.Query<Customer>().Where(c => c.Score != 5 && c.Score != null)", "where customer0_.Score<>? and (customer0_.Score is not null)" },
        { "NOT IN over a nullable subquery as All", "session.Query<Customer>().Where(c => c.Score != null && session.Query<Order>().Select(o => o.Rating).All(v => v != null && v != c.Score))", "where (customer0_.Score is not null) and  not (exists (select order1_.Rating from Sales.Orders order1_ where  not ((order1_.Rating is not null) and" },
        { "Math.Round, Math.Sqrt, a cast and ToString", "session.Query<Order>().Select(o => new { R = Math.Round(o.Total, 1), S = Math.Sqrt((double)(o.Total)), I = (int)(o.Total), T = o.Total.ToString() })", "round(order0_.Total, ?)" },
        { "modulo", "session.Query<Order>().Where(o => o.OrderId % 2 == 0)", "% (?)" },
        { "a nullable member through Value", "session.Query<Customer>().Where(c => c.Score.Value > 3)", "customer0_.Score>?" },
        { "an anonymous grouping key", InnerJoin + ".GroupBy(t => new { t.c.CustomerName, t.o.Rating }).Select(g => new { g.Key.CustomerName, g.Key.Rating, Count = g.Count() })", "group by customer0_.CustomerName , order1_.Rating" },

        // Average over a whole number is the average in floating point, which does not cut the
        // quotient off as SQL's AVG does; cast into int it is cut off again; over a decimal it
        // is a decimal (decision 161).
        { "Average over a whole number in floating point", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = g.Average(e => e.OrderId) })", "cast(avg(cast(order0_.OrderId as FLOAT(53))) as FLOAT(53)) as col_1_0_" },
        { "Average over a nullable whole number in floating point", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = g.Average(e => e.Rating) })", "cast(avg(cast(order0_.Rating as FLOAT(53))) as FLOAT(53)) as col_1_0_" },
        { "Average cast into int as SQL's AVG", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = (int)g.Average(e => e.OrderId) })", "cast(cast(avg(cast(order0_.OrderId as FLOAT(53))) as FLOAT(53)) as INT) as col_1_0_" },
        { "Average cast into a nullable int as SQL's AVG", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = (int?)g.Average(e => e.Rating) })", "cast(cast(avg(cast(order0_.Rating as FLOAT(53))) as FLOAT(53)) as INT) as col_1_0_" },
        { "Average over a decimal as a decimal", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = g.Average(e => e.Total) })", "cast(avg(order0_.Total) as DECIMAL(29, 10)) as col_1_0_" },
        { "Average in a subquery cast into int as SQL's AVG", "session.Query<Customer>().Where(c => c.Score > (int?)session.Query<Order>().Where(o => o.CustomerId == c.CustomerId).Average(o => o.OrderId))", "as INT)" },
    };

    [Theory]
    [MemberData(nameof(Translated))]
    public void TheProviderTranslates(string shape, string chain, string hallmark)
    {
        var sql = Translate(shape, chain);

        Assert.Contains(hallmark, sql);
    }

    /// <summary>The chains the provider refuses, which is why the builder never writes them (decision 118).</summary>
    public static TheoryData<string, string> Refused => new()
    {
        { "ordering after Distinct", "session.Query<Customer>().Select(c => new { c.CustomerName }).Distinct().OrderBy(p => p.CustomerName)" },
        { "Count with a predicate over join tuples", InnerJoin + ".GroupBy(t => t.c.CustomerName).Select(g => new { Name = g.Key, Rated = g.Count(e => e.o.Rating != null) })" },
        { "Sum over distinct values", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { Id = g.Key, S = g.Select(e => e.Total).Distinct().Sum() })" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void TheProviderRefuses(string shape, string chain)
    {
        Assert.ThrowsAny<Exception>(() => Translate(shape, chain));
    }

    private const string NavigatedEntities = """
        namespace Navigated;

        public class Region
        {
            public virtual int RegionId { get; set; }
            public virtual string RegionName { get; set; }
        }

        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
            public virtual Region Region { get; set; }
        }

        public class Order
        {
            public virtual int OrderId { get; set; }
            public virtual Customer Customer { get; set; }
            public virtual decimal Total { get; set; }
        }
        """;

    private const string NavigatedMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Navigated">
          <class name="Region" table="Regions" schema="Sales">
            <id name="RegionId" column="RegionId" type="Int32"><generator class="identity" /></id>
            <property name="RegionName" type="String" />
          </class>
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32"><generator class="identity" /></id>
            <property name="CustomerName" type="String" />
            <many-to-one name="Region" class="Region" column="RegionId" />
          </class>
          <class name="Order" table="Orders" schema="Sales">
            <id name="OrderId" column="OrderId" type="Int32"><generator class="identity" /></id>
            <many-to-one name="Customer" class="Customer" column="CustomerId" />
            <property name="Total" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;

    /// <summary>
    /// The join the provider makes of a navigation in an operand (decision 151): a left outer
    /// join wherever the navigation stands - projection, ordering, grouping key, a test for
    /// NULL, a disjunct - and an inner one only in a filter a NULL cannot satisfy, where the
    /// two return the same rows. The shared LINQ reader joins a reference left where its row
    /// may be absent and inner where it may not, which over this provider is the same rows.
    /// </summary>
    public static TheoryData<string, string, string> Navigations => new()
    {
        { "in a projection", "session.Query<Order>().Select(o => o.Customer.CustomerName)", "left outer join Sales.Customers" },
        { "along a chain", "session.Query<Order>().Select(o => o.Customer.Region.RegionName)", "left outer join Sales.Regions" },
        { "in an ordering", "session.Query<Order>().OrderBy(o => o.Customer.CustomerName).Select(o => o.Total)", "left outer join Sales.Customers" },
        { "in a grouping key", "session.Query<Order>().GroupBy(o => o.Customer.CustomerName).Select(g => new { g.Key, C = g.Count() })", "left outer join Sales.Customers" },
        { "under a test for NULL", "session.Query<Order>().Where(o => o.Customer.CustomerName == null).Select(o => o.Total)", "left outer join Sales.Customers" },
        { "in a disjunct", "session.Query<Order>().Where(o => o.Customer.CustomerName == \"A\" || o.Total > 5m).Select(o => o.Total)", "left outer join Sales.Customers" },
        { "in a comparison with a value", "session.Query<Order>().Where(o => o.Customer.CustomerName == \"A\").Select(o => o.Total)", "inner join Sales.Customers" },
    };

    [Theory]
    [MemberData(nameof(Navigations))]
    public void TheProviderJoinsANavigation(string shape, string chain, string hallmark)
    {
        var sql = Translate(shape, chain, NavigatedEntities, NavigatedMapping, "Navigated");

        Assert.Contains(hallmark, sql);
    }

    private static string Translate(string shape, string chain) => Translate(shape, chain, Entities, Mapping, "Probe");

    private static string Translate(string shape, string chain, string entities, string mapping, string ns)
    {
        var method = $$"""
            public static IQueryable Query(ISession session)
            {
                return {{chain}};
            }
            """;

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            $"NHibernateLinqProvider_{ns}_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            method,
            [entities],
            GeneratedQueryCompiler.NHibernateConsumerReferences,
            "using NHibernate;" + Environment.NewLine + "using NHibernate.Linq;" + Environment.NewLine + $"using {ns};");

        return NHibernateLinqAcceptance.Sql(compiled, [mapping]);
    }
}
