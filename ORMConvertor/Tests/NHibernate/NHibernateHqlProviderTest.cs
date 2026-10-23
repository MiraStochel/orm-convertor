using Tests.Verification;

namespace Tests.NHibernate;

/// <summary>
/// What HQL of NHibernate 5.7.0 accepts in the condition of an entity join, measured by its
/// own query plan over a mapped model without a connection (the third level of decision
/// 027): a subquery after <c>with</c> - <c>in (select …)</c>, <c>not in</c>, a correlated
/// one - in an inner and in a left join. These are the facts the HQL visitor's join rests
/// on (decision 162). Beside them, what avg hands over, which the visitor's conversion of an
/// average rests on (decision 161), and what the trimming of a fixed-width value is written as
/// (decision 169): <c>trim(trailing from x)</c> is <c>rtrim(x)</c>, while <c>rtrim(x)</c>
/// written directly is refused. A fact the release stops holding on an upgrade shows up here.
/// </summary>
public class NHibernateHqlProviderTest
{
    private const string Entities = """
        namespace HqlProbe;

        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
        }

        public class Purchase
        {
            public virtual int OrderId { get; set; }
            public virtual int CustomerId { get; set; }
            public virtual int? Rating { get; set; }
            public virtual decimal Amount { get; set; }
        }
        """;

    private const string Mapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="HqlProbe">
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32"><generator class="identity" /></id>
            <property name="CustomerName" type="String" />
          </class>
          <class name="Purchase" table="Orders" schema="Sales">
            <id name="OrderId" column="OrderId" type="Int32"><generator class="identity" /></id>
            <property name="CustomerId" type="Int32" />
            <property name="Rating" type="Int32" />
            <property name="Amount" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;

    /// <summary>The HQL the builder writes and the SQL the provider makes of it: the hallmark is the subquery inside the join's ON.</summary>
    public static TheoryData<string, string, string> Translated => new()
    {
        {
            "IN over a subquery in an inner join",
            "select c.CustomerName from Customer c inner join Purchase o with o.CustomerId = c.CustomerId and c.CustomerId in (select o2.CustomerId from Purchase o2 where o2.Rating > 3)",
            "inner join Sales.Orders purchase1_ on  (purchase1_.CustomerId=customer0_.CustomerId and (customer0_.CustomerId in (select purchase2_.CustomerId from Sales.Orders purchase2_ where purchase2_.Rating>3)))"
        },
        {
            "NOT IN over a subquery in an inner join",
            "select c.CustomerName from Customer c inner join Purchase o with o.CustomerId = c.CustomerId and c.CustomerId not in (select o2.CustomerId from Purchase o2 where o2.Rating > 3)",
            "and (customer0_.CustomerId not in  (select purchase2_.CustomerId from Sales.Orders purchase2_ where purchase2_.Rating>3)))"
        },
        {
            "a correlated subquery in a left join",
            "select c.CustomerName from Customer c left join Purchase o with o.CustomerId = c.CustomerId and c.CustomerId in (select o2.CustomerId from Purchase o2 where o2.CustomerId = c.CustomerId and o2.Rating > 3)",
            "left outer join Sales.Orders purchase1_ on  (purchase1_.CustomerId=customer0_.CustomerId and (customer0_.CustomerId in (select purchase2_.CustomerId from Sales.Orders purchase2_ where purchase2_.CustomerId=customer0_.CustomerId and purchase2_.Rating>3)))"
        },
        {
            "EXISTS in an inner join",
            "select c.CustomerName from Customer c inner join Purchase o with o.CustomerId = c.CustomerId and exists (select o2.OrderId from Purchase o2 where o2.CustomerId = c.CustomerId and o2.Rating > 3)",
            "and (exists (select purchase2_.OrderId from Sales.Orders purchase2_ where purchase2_.CustomerId=customer0_.CustomerId and purchase2_.Rating>3)))"
        },

        // The HQL spelling of TrimEnd (decision 169).
        { "a trailing trim as rtrim", "select trim(trailing from c.CustomerName) from Customer c", "select rtrim( customer0_.CustomerName ) as col_0_0_" },
        { "a trailing trim over a maximum", "select trim(trailing from max(c.CustomerName)) from Customer c", "select rtrim( max(customer0_.CustomerName) ) as col_0_0_" },
    };

    /// <summary>Why the HQL writer spells TrimEnd as trim(trailing from …): rtrim written directly has no type in the HQL parser (decision 169).</summary>
    [Fact]
    public void TheProviderRefusesRtrimWrittenDirectly()
    {
        Assert.ThrowsAny<Exception>(() => Sql("rtrim", "select rtrim(c.CustomerName) from Customer c"));
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void TheProviderTranslates(string shape, string hql, string hallmark)
    {
        var sql = Sql(shape, hql);

        Assert.True(sql.Contains(hallmark, StringComparison.Ordinal), sql);
    }

    /// <summary>
    /// What avg hands over (decision 161): SQL's AVG, as the SQL shows - over a whole number the
    /// quotient cut off -, but as a Double whatever it averages; the conversions the HQL writer
    /// writes hand over SQL's type - cast into int and long, and into decimal, which NHibernate
    /// writes as DECIMAL(29,10).
    /// </summary>
    public static TheoryData<string, string, string, Type> Averaged => new()
    {
        { "avg over a whole number", "select avg(p.CustomerId) from Purchase p", "select avg(purchase0_.CustomerId) as col_0_0_", typeof(double) },
        { "avg over a decimal", "select avg(p.Amount) from Purchase p", "select avg(purchase0_.Amount) as col_0_0_", typeof(double) },
        { "avg cast into int", "select cast(avg(p.CustomerId) as int) from Purchase p", "select cast(avg(purchase0_.CustomerId) as INT) as col_0_0_", typeof(int) },
        { "avg cast into long", "select cast(avg(p.CustomerId) as long) from Purchase p", "select cast(avg(purchase0_.CustomerId) as BIGINT) as col_0_0_", typeof(long) },
        { "avg cast into decimal", "select cast(avg(p.Amount) as decimal) from Purchase p", "select cast(avg(purchase0_.Amount) as DECIMAL(29, 10)) as col_0_0_", typeof(decimal) },
        { "avg cast into decimal in a subquery", "select c.CustomerName from Customer c where c.CustomerId > (select cast(avg(p.Amount) as decimal) from Purchase p)", "(select cast(avg(purchase1_.Amount) as DECIMAL(29, 10)) from Sales.Orders purchase1_)", typeof(string) },
    };

    [Theory]
    [MemberData(nameof(Averaged))]
    public void AvgHandsOverItsType(string shape, string hql, string hallmark, Type handedOver)
    {
        var compiled = GeneratedEntityCompiler.CompileOrFail(
            "NHibernateHqlProvider_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            [Entities],
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        Assert.Contains(hallmark, NHibernateQueryAcceptance.Sql(compiled, [Mapping], hql));
        Assert.Equal([handedOver], NHibernateQueryAcceptance.ReturnTypes(compiled, [Mapping], hql));
    }

    private static string Sql(string shape, string hql)
    {
        var compiled = GeneratedEntityCompiler.CompileOrFail(
            "NHibernateHqlProvider_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            [Entities],
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        return NHibernateQueryAcceptance.Sql(compiled, [Mapping], hql);
    }
}
