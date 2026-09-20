using Model;

namespace Tests.Combined;

/// <summary>
/// The sample every cross-framework test converts, per source framework: a Shop.Customer
/// entity, the mapping artifact the framework keeps beside it, and one query over it.
/// Directions are the product of <see cref="ORMEnum"/> with itself, not a list written
/// here, so a framework enters the matrix the moment its enum value exists - and every
/// direction that touches it fails until the wrapper brings its row below. The row is the
/// wrapper's to bring, like its row in <see cref="EnforcedMembersTest"/> (decision 037);
/// what differs is that the matrix cannot stay silently at nine when a fourth value arrives.
/// </summary>
public static class CrossFrameworkInputs
{
    /// <summary>The namespace every framework's sample declares when it declares one.</summary>
    public const string Namespace = "Shop";

    /// <summary>Every source x target pair the enum yields, identity directions included.</summary>
    public static TheoryData<ORMEnum, ORMEnum> Directions()
    {
        var data = new TheoryData<ORMEnum, ORMEnum>();
        foreach (var source in Enum.GetValues<ORMEnum>())
        {
            foreach (var target in Enum.GetValues<ORMEnum>())
            {
                data.Add(source, target);
            }
        }

        return data;
    }

    /// <summary>Every framework the enum yields, for a theory that runs once per target.</summary>
    public static TheoryData<ORMEnum> Frameworks()
    {
        var data = new TheoryData<ORMEnum>();
        foreach (var framework in Enum.GetValues<ORMEnum>())
        {
            data.Add(framework);
        }

        return data;
    }

    /// <summary>The mapping units followed by the query unit - a whole conversion input.</summary>
    public static List<ConversionSource> Units(ORMEnum framework, bool withNamespace = true) =>
        [.. MappingUnits(framework, withNamespace), QueryUnit(framework)];

    /// <summary>
    /// What the framework reads the entity from: the class, and for NHibernate the hbm.xml
    /// beside it. Without a namespace when a test needs to see that none gets invented.
    /// </summary>
    public static List<ConversionSource> MappingUnits(ORMEnum framework, bool withNamespace = true) => framework switch
    {
        ORMEnum.Dapper =>
        [
            new() { Content = DapperEntity(withNamespace), ContentType = ConversionContentType.CSharpEntity },
        ],
        ORMEnum.EFCore =>
        [
            new() { Content = EFCoreEntity(withNamespace), ContentType = ConversionContentType.CSharpEntity },
        ],
        ORMEnum.NHibernate =>
        [
            new() { Content = NHibernateEntity(withNamespace), ContentType = ConversionContentType.CSharpEntity },
            new() { Content = NHibernateMapping(withNamespace), ContentType = ConversionContentType.XML },
        ],
        ORMEnum.Hibernate =>
        [
            new() { Content = JpaEntity(withNamespace), ContentType = ConversionContentType.JavaEntity },
        ],

        // The same text for the second implementation of the specification, and that is the
        // point rather than a shortcut: an entity that states every name has no room for the
        // defaults the two differ in, so one source really is both (decisions 076 and 080).
        ORMEnum.EclipseLink =>
        [
            new() { Content = JpaEntity(withNamespace), ContentType = ConversionContentType.JavaEntity },
        ],

        // One unit, and the mapping is not in it: MyBatis keeps the mapping in the mapper
        // beside the statement, so the mapper is the query unit below and carries both halves
        // (decisions 081 and 084). The domain class alone is what the framework asks of the
        // entity, which is nothing at all.
        ORMEnum.MyBatis =>
        [
            new() { Content = MyBatisEntity(withNamespace), ContentType = ConversionContentType.JavaEntity },
        ],
        _ => throw NoRow(framework),
    };

    /// <summary>The same query in the language the framework reads.</summary>
    public static ConversionSource QueryUnit(ORMEnum framework) => framework switch
    {
        ORMEnum.Dapper => new() { Content = DapperQuery, ContentType = ConversionContentType.SqlQuery },
        ORMEnum.EFCore => new() { Content = EFCoreQuery, ContentType = ConversionContentType.CSharpQuery },
        ORMEnum.NHibernate => new() { Content = NHibernateQuery, ContentType = ConversionContentType.CSharpQuery },
        ORMEnum.Hibernate => new() { Content = JpaQuery, ContentType = ConversionContentType.JpqlQuery },
        ORMEnum.EclipseLink => new() { Content = JpaQuery, ContentType = ConversionContentType.JpqlQuery },

        // The one row whose query unit is a mapping as well: a MyBatis mapper carries the
        // <resultMap> beside the <select>, so this document is read by both passes of the
        // orchestration (decision 081) and the matrix exercises that without a case of its own.
        ORMEnum.MyBatis => new() { Content = MyBatisMapper, ContentType = ConversionContentType.XML },
        _ => throw NoRow(framework),
    };

    // Deliberately not a fallback to some framework's inputs: the former `_ =>` arm of each
    // test handed NHibernate's units to whatever value it did not know, which is how a fourth
    // framework would have passed the matrix on another framework's input.
    private static ArgumentOutOfRangeException NoRow(ORMEnum framework) => new(
        nameof(framework),
        framework,
        $"{framework} has no sample inputs in {nameof(CrossFrameworkInputs)}; its wrapper brings them.");

    private static string NamespaceLine(bool withNamespace) =>
        withNamespace ? $"namespace {Namespace};{Environment.NewLine}{Environment.NewLine}" : string.Empty;

    private static string DapperEntity(bool withNamespace) =>
        NamespaceLine(withNamespace) + """
        public class Customer
        {
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal CreditLimit { get; set; }
        }
        """;

    private static string EFCoreEntity(bool withNamespace) =>
        """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;


        """ + NamespaceLine(withNamespace) + """
        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal CreditLimit { get; set; }
        }
        """;

    private static string NHibernateEntity(bool withNamespace) =>
        NamespaceLine(withNamespace) + """
        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
            public virtual decimal CreditLimit { get; set; }
        }
        """;

    // No assembly attribute: which assembly the class ends up in is the consumer project's
    // fact (decision 028), and the parser does not read it either way.
    private static string NHibernateMapping(bool withNamespace)
    {
        var namespaceAttribute = withNamespace ? $" namespace=\"{Namespace}\"" : string.Empty;

        return $"""
        <?xml version="1.0" encoding="utf-8"?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2"{namespaceAttribute}>
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32">
              <generator class="identity" />
            </id>
            <property name="CustomerName" column="CustomerName" type="String" />
            <property name="CreditLimit" column="CreditLimit" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;
    }

    // The Java sample: the same entity with jakarta.persistence annotations, a package
    // where the C# samples have a namespace (decision 077). It is named after the
    // specification, not after an implementation, because both Java rows read this one text.
    private static string JpaEntity(bool withNamespace) =>
        (withNamespace ? $"package {Namespace};{Environment.NewLine}{Environment.NewLine}" : string.Empty) + """
        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.Id;
        import jakarta.persistence.Table;
        import java.math.BigDecimal;

        @Entity
        @Table(name = "Customers", schema = "Sales")
        public class Customer {
            @Id
            @Column(name = "CustomerId")
            private Integer CustomerId;

            @Column(name = "CustomerName")
            private String CustomerName;

            @Column(name = "CreditLimit")
            private BigDecimal CreditLimit;

            public Integer getCustomerId() { return CustomerId; }
            public void setCustomerId(Integer value) { this.CustomerId = value; }
            public String getCustomerName() { return CustomerName; }
            public void setCustomerName(String value) { this.CustomerName = value; }
            public BigDecimal getCreditLimit() { return CreditLimit; }
            public void setCreditLimit(BigDecimal value) { this.CreditLimit = value; }
        }
        """;

    // The MyBatis domain class: the same entity with nothing on it at all - no annotation,
    // no base class, no import from the framework (decision 084). It is the poorest sample
    // of the six by design, and the pair with Dapper is what makes the asymmetry of F6
    // measurable in both ecosystems.
    private static string MyBatisEntity(bool withNamespace) =>
        (withNamespace ? $"package {Namespace};{Environment.NewLine}{Environment.NewLine}" : string.Empty) + """
        import java.math.BigDecimal;

        public class Customer {
            private Integer CustomerId;
            private String CustomerName;
            private BigDecimal CreditLimit;

            public Integer getCustomerId() { return CustomerId; }
            public void setCustomerId(Integer value) { this.CustomerId = value; }
            public String getCustomerName() { return CustomerName; }
            public void setCustomerName(String value) { this.CustomerName = value; }
            public BigDecimal getCreditLimit() { return CreditLimit; }
            public void setCreditLimit(BigDecimal value) { this.CreditLimit = value; }
        }
        """;

    // The mapper: the pairs of column and property, and the same query the Dapper row
    // states, in the same language. The namespace is the mapper's own name and says nothing
    // about the entity's package, which comes from the domain class.
    private const string MyBatisMapper = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
        <mapper namespace="Shop.CustomerMapper">
          <resultMap id="customer" type="Customer">
            <id     column="CustomerId"   property="CustomerId"/>
            <result column="CustomerName" property="CustomerName"/>
            <result column="CreditLimit"  property="CreditLimit"/>
          </resultMap>
          <select id="findCustomers" resultMap="customer">
            SELECT c.CustomerName
            FROM Sales.Customers AS c
            WHERE c.CreditLimit &gt; 2000
            ORDER BY c.CustomerName ASC
          </select>
        </mapper>
        """;

    private const string JpaQuery = """
        select c.CustomerName as Name
        from Customer c
        where c.CreditLimit > 2000
        order by c.CustomerName asc
        """;

    private const string DapperQuery = """
        SELECT c.CustomerName
        FROM Sales.Customers AS c
        WHERE c.CreditLimit > 2000
        ORDER BY c.CustomerName ASC
        """;

    private const string EFCoreQuery = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => c.CreditLimit > 2000)
                .OrderBy(c => c.CustomerName)
                .Select(c => new { Name = c.CustomerName })
                .ToList();
        }
        """;

    private const string NHibernateQuery = """
        public void Query()
        {
            var q = session.Query<Customer>()
                .Where(c => c.CreditLimit > 2000)
                .OrderBy(c => c.CustomerName)
                .Select(c => new { Name = c.CustomerName })
                .ToList();
        }
        """;
}
