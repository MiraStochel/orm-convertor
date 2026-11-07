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
/// written directly is refused; and that the target of a join spelled like an alias in scope is
/// read as a path off it unless qualified with its namespace (HqlNames). A fact the release
/// stops holding on an upgrade shows up here.
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

        // The cross join the representation carries (decision 196), in both spellings HQL 5.7.0
        // reads. Comma-separated range variables come out as SQL's comma join, and every range
        // variable is hoisted before every entity join whatever order the HQL wrote: an entity
        // join whose condition names the first root then stands after a comma, where SQL
        // Server cannot bind the root ("the multi-part identifier could not be bound"), so the
        // HQL writer never spells a cross join with a comma. `cross join` comes out as SQL's
        // CROSS JOIN in the one join chain, before and after an entity join alike.
        {
            "comma-separated range variables as a comma join",
            "select c.CustomerName from Customer c, Purchase p where p.CustomerId = c.CustomerId",
            "from Sales.Customers customer0_, Sales.Orders purchase1_ where purchase1_.CustomerId=customer0_.CustomerId"
        },
        {
            "comma-separated range variables inside a subquery",
            "select c.CustomerName from Customer c where c.CustomerId in (select p.CustomerId from Purchase p, Customer c2 where c2.CustomerId = p.CustomerId and c2.CustomerName = 'x')",
            "(select purchase1_.CustomerId from Sales.Orders purchase1_, Sales.Customers customer2_ where customer2_.CustomerId=purchase1_.CustomerId"
        },
        {
            "a range variable after an entity join is hoisted before it",
            "select c.CustomerName from Customer c inner join Purchase o with o.CustomerId = c.CustomerId, Customer c2 where c2.CustomerId = o.CustomerId",
            "from Sales.Customers customer0_, Sales.Customers customer2_ inner join Sales.Orders purchase1_ on  (purchase1_.CustomerId=customer0_.CustomerId)"
        },
        {
            "an entity join after a range variable",
            "select c.CustomerName from Customer c, Customer c2 inner join Purchase o with o.CustomerId = c.CustomerId where c2.CustomerId = o.CustomerId",
            "from Sales.Customers customer0_, Sales.Customers customer1_ inner join Sales.Orders purchase2_ on  (purchase2_.CustomerId=customer0_.CustomerId)"
        },
        {
            "cross join as SQL's CROSS JOIN",
            "select c.CustomerName from Customer c cross join Purchase p where p.CustomerId = c.CustomerId",
            "from Sales.Customers customer0_ cross join Sales.Orders purchase1_ where purchase1_.CustomerId=customer0_.CustomerId"
        },
        {
            "cross join inside a subquery",
            "select c.CustomerName from Customer c where c.CustomerId in (select p.CustomerId from Purchase p cross join Customer c2 where c2.CustomerId = p.CustomerId and c2.CustomerName = 'x')",
            "(select purchase1_.CustomerId from Sales.Orders purchase1_ cross join Sales.Customers customer2_ where customer2_.CustomerId=purchase1_.CustomerId"
        },
        {
            "cross join after an entity join",
            "select c.CustomerName from Customer c inner join Purchase o with o.CustomerId = c.CustomerId cross join Customer c2 where c2.CustomerId = o.CustomerId",
            "from Sales.Customers customer0_ inner join Sales.Orders purchase1_ on  (purchase1_.CustomerId=customer0_.CustomerId) cross join Sales.Customers customer2_ where customer2_.CustomerId=purchase1_.CustomerId"
        },
        {
            "entity join after a cross join",
            "select c.CustomerName from Customer c cross join Customer c2 inner join Purchase o with o.CustomerId = c.CustomerId where c2.CustomerId = o.CustomerId",
            "from Sales.Customers customer0_ cross join Sales.Customers customer1_ inner join Sales.Orders purchase2_ on  (purchase2_.CustomerId=customer0_.CustomerId) where customer1_.CustomerId=purchase2_.CustomerId"
        },

        // A subquery of HAVING may cross-join and repeat the aliases of the query around it;
        // the target of a join spelled like an alias in scope is read qualified with its
        // namespace (HqlNames, Combined/BenchmarkTrialDefectTest).
        {
            "a cross join in a subquery of HAVING under the outer query's aliases",
            "select p.CustomerId, sum(p.Amount) from Purchase p group by p.CustomerId having sum(p.Amount) > (select sum(p.Amount) * .005 from Purchase p cross join Customer c where c.CustomerId = p.CustomerId)",
            "having sum(purchase0_.Amount)>(select sum(purchase1_.Amount)*0.005 from Sales.Orders purchase1_ cross join Sales.Customers customer2_ where customer2_.CustomerId=purchase1_.CustomerId)"
        },
        {
            "a joined entity spelled like an outer alias, qualified, in a cross join",
            "select Customer.CustomerName from Customer Customer where Customer.CustomerId in (select p.CustomerId from Purchase p cross join HqlProbe.Customer c where c.CustomerId = p.CustomerId)",
            "(select purchase1_.CustomerId from Sales.Orders purchase1_ cross join Sales.Customers customer2_ where customer2_.CustomerId=purchase1_.CustomerId)"
        },
        {
            "a joined entity spelled like an outer alias, qualified, in an entity join",
            "select Customer.CustomerName from Customer Customer where Customer.CustomerId in (select p.CustomerId from Purchase p inner join HqlProbe.Customer c with c.CustomerId = p.CustomerId)",
            "inner join Sales.Customers customer2_ on  (customer2_.CustomerId=purchase1_.CustomerId)"
        },
        {
            "a joined entity spelled like an alias of its own query, qualified",
            "select Customer.CustomerId from Purchase Customer cross join HqlProbe.Customer c where c.CustomerId = Customer.CustomerId",
            "from Sales.Orders purchase0_ cross join Sales.Customers customer1_ where customer1_.CustomerId=purchase0_.CustomerId"
        },
        {
            "a joined entity spelled like its own alias",
            "select p.CustomerId from Purchase p cross join Customer Customer where Customer.CustomerId = p.CustomerId",
            "cross join Sales.Customers customer1_"
        },
        {
            "the root of a subquery spelled like an outer alias",
            "select Customer.CustomerName from Customer Customer where Customer.CustomerId in (select c.CustomerId from Customer c)",
            "in (select customer1_.CustomerId from Sales.Customers customer1_)"
        },
        {
            "a joined entity spelled like an outer alias in another case",
            "select customer.CustomerName from Customer customer where customer.CustomerId in (select p.CustomerId from Purchase p cross join Customer c where c.CustomerId = p.CustomerId)",
            "cross join Sales.Customers customer2_"
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

    /// <summary>
    /// Why the HQL writer qualifies the target of a join spelled like an alias in scope
    /// (HqlNames): bare, NHibernate reads it as a path off the alias, in a cross join and in an
    /// entity join, in a subquery and in the query of the alias alike.
    /// </summary>
    public static TheoryData<string, string> ReadAsAPath => new()
    {
        { "cross join in a subquery", "select Customer.CustomerName from Customer Customer where Customer.CustomerId in (select p.CustomerId from Purchase p cross join Customer c where c.CustomerId = p.CustomerId)" },
        { "entity join in a subquery", "select Customer.CustomerName from Customer Customer where Customer.CustomerId in (select p.CustomerId from Purchase p inner join Customer c with c.CustomerId = p.CustomerId)" },
        { "cross join in the query of the alias", "select Customer.CustomerId from Purchase Customer cross join Customer c where c.CustomerId = Customer.CustomerId" },
        { "entity join in the query of the alias", "select Customer.CustomerId from Purchase Customer inner join Customer c with c.CustomerId = Customer.CustomerId" },
    };

    [Theory]
    [MemberData(nameof(ReadAsAPath))]
    public void TheProviderReadsAJoinedEntitySpelledLikeAnAliasAsAPath(string shape, string hql)
    {
        var failure = Assert.ThrowsAny<Exception>(() => Sql(shape, hql));

        Assert.Contains("Path expected for join!", failure.Message, StringComparison.Ordinal);
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
