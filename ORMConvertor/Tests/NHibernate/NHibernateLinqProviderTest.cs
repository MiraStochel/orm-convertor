using Tests.Verification;

namespace Tests.NHibernate;

/// <summary>
/// What the LINQ provider of NHibernate 5.7.0 translates and what it refuses, measured by its
/// own query plan over a mapped model without a connection (the third level of decision 027).
/// These are the facts the hooks of <c>NHibernateLinqQueryBuilder</c> and
/// <c>NHibernateLinqQueryVisitor</c> rest on (decision 118): which join operators it knows,
/// where an ordering under DISTINCT may stand, how a COUNT over a column that may hold NULL
/// is spelled, which aggregates it takes over distinct values. A fact the provider stops
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
        { "ordering before Distinct", "session.Query<Customer>().OrderBy(c => c.CustomerName).Select(c => new { c.CustomerName }).Distinct()", "select distinct customer0_.CustomerName as col_0_0_ from Sales.Customers customer0_ order by customer0_.CustomerName asc" },
        { "ordering before Distinct with a slice", "session.Query<Customer>().OrderBy(c => c.CustomerName).Select(c => new { c.CustomerName }).Distinct().Skip(1).Take(2)", "order by customer0_.CustomerName asc OFFSET" },
        { "ordering by an alias after the projection", "session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { Id = g.Key, Count = g.Count() }).OrderByDescending(p => p.Count)", "order by (count(*)) desc" },
        { "COUNT of non-null values as a Sum over a conditional", InnerJoin + ".GroupBy(t => t.c.CustomerName).Select(g => new { Name = g.Key, Rated = g.Sum(e => e.o.Rating != null ? 1 : 0) })", "sum((case when order1_.Rating is not null then" },
        { "COUNT of matched rows of a left join", LeftJoin + ".GroupBy(t => t.c.CustomerName).Select(g => new { Name = g.Key, Orders = g.Sum(e => e.o != null ? 1 : 0) })", "case when order1_.OrderId is not null" },
        { "COUNT over distinct values", InnerJoin + ".GroupBy(t => t.c.CustomerName).Select(g => new { Name = g.Key, D = g.Select(e => e.o.Rating).Distinct().Count() })", "count(distinct order1_.Rating)" },
        { "Like with an escape", "session.Query<Customer>().Where(c => c.CustomerName.Like(\"A!_%\", '!'))", "like ? escape '!'" },
        { "IN over a subquery", "session.Query<Customer>().Where(c => session.Query<Order>().Select(o => o.CustomerId).Distinct().Contains(c.CustomerId))", "in (select distinct" },
        { "EXISTS", "session.Query<Customer>().Where(c => session.Query<Order>().Where(o => o.CustomerId == c.CustomerId).Any())", "where exists (select" },
        { "scalar subquery", "session.Query<Customer>().Where(c => c.CreditLimit > session.Query<Order>().Where(o => o.CustomerId == c.CustomerId).Max(o => o.Total))", "(select max(order1_.Total)" },
        { "Math.Round, Math.Sqrt, a cast and ToString", "session.Query<Order>().Select(o => new { R = Math.Round(o.Total, 1), S = Math.Sqrt((double)(o.Total)), I = (int)(o.Total), T = o.Total.ToString() })", "round(order0_.Total, ?)" },
        { "modulo", "session.Query<Order>().Where(o => o.OrderId % 2 == 0)", "% (?)" },
        { "a nullable member through Value", "session.Query<Customer>().Where(c => c.Score.Value > 3)", "customer0_.Score>?" },
        { "an anonymous grouping key", InnerJoin + ".GroupBy(t => new { t.c.CustomerName, t.o.Rating }).Select(g => new { g.Key.CustomerName, g.Key.Rating, Count = g.Count() })", "group by customer0_.CustomerName , order1_.Rating" },
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

    private static string Translate(string shape, string chain)
    {
        var method = $$"""
            public static IQueryable Query(ISession session)
            {
                return {{chain}};
            }
            """;

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "NHibernateLinqProvider_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            method,
            [Entities],
            GeneratedQueryCompiler.NHibernateConsumerReferences,
            "using NHibernate;" + Environment.NewLine + "using NHibernate.Linq;" + Environment.NewLine + "using Probe;");

        return NHibernateLinqAcceptance.Sql(compiled, [Mapping]);
    }
}
