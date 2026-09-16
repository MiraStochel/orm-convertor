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
        _ => throw NoRow(framework),
    };

    /// <summary>The same query in the language the framework reads.</summary>
    public static ConversionSource QueryUnit(ORMEnum framework) => framework switch
    {
        ORMEnum.Dapper => new() { Content = DapperQuery, ContentType = ConversionContentType.SqlQuery },
        ORMEnum.EFCore => new() { Content = EFCoreQuery, ContentType = ConversionContentType.CSharpQuery },
        ORMEnum.NHibernate => new() { Content = NHibernateQuery, ContentType = ConversionContentType.CSharpQuery },
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
