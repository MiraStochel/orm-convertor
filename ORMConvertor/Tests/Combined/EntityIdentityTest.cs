using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Which declarations of a conversion mean one and the same entity (decision 094). The
/// article's rule E1 has a translation unit define exactly one entity, so it never has to
/// say; we left that rule on both sides - one unit may declare several classes (F14) and
/// several units may state one entity (decisions 017 and 066) - and the sentence between
/// the two is this one: the identity of an entity inside a conversion is the pair of
/// namespace and name, a namespace nobody stated does not distinguish, and two entities
/// left sharing a simple name are a record rather than a silence.
/// </summary>
public class EntityIdentityTest
{
    private const string Shop = "Shop";

    /// <summary>
    /// One half of a partial class - the shape a .NET project really has, and the one the
    /// representation cannot tell from a whole class: the header carries the access
    /// modifier and nothing else (architecture, §5).
    /// </summary>
    private static string Half(string body, string ns = Shop, string table = "Customers") =>
        $$"""
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace {{ns}};

        [Table("{{table}}", Schema = "Sales")]
        public partial class Customer
        {
        {{body}}
        }
        """;

    private static readonly string KeyHalf = Half("""
            [Key]
            public int CustomerId { get; set; }
        """);

    private static readonly string NameHalf = Half("""
            public string CustomerName { get; set; }
        """);

    private static ConversionSource Unit(string content, string name) => new()
    {
        Name = name,
        ContentType = ConversionContentType.CSharpEntity,
        Content = content,
    };

    private static ConversionResult Convert(params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.EFCore, [.. units]);

    private static List<ConversionSource> Entities(ConversionResult result)
        => [.. result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity)];

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var at = text.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    [Fact]
    public void TwoUnitsDeclaringOneClassYieldOneArtifact()
    {
        var result = Convert(Unit(KeyHalf, "Customer.cs"), Unit(NameHalf, "Customer.Extra.cs"));

        var entity = Assert.Single(Entities(result));
        Assert.Contains("int CustomerId", entity.Content, StringComparison.Ordinal);
        Assert.Contains("string CustomerName", entity.Content, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(entity.Content, "class Customer"));
    }

    [Fact]
    public void NeitherUnitIsCalledBarren()
    {
        // The second unit founded no map and enriched one, which decision 066 counts the
        // same way; calling it barren would be the record the merge must not produce.
        var result = Convert(Unit(KeyHalf, "Customer.cs"), Unit(NameHalf, "Customer.Extra.cs"));

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void AFactTheFirstUnitStatedIsNotOverwrittenBySecond()
    {
        var result = Convert(
            Unit(KeyHalf, "Customer.cs"),
            Unit(Half("    public string CustomerName { get; set; }", table: "Clients"), "Customer.Extra.cs"));

        var entity = Assert.Single(Entities(result));
        Assert.Contains("[Table(\"Customers\"", entity.Content, StringComparison.Ordinal);

        var conflict = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal("Customer", conflict.Entity);
        Assert.Contains("Clients", conflict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclarationWithoutANamespaceJoinsTheOneThatHasIt()
    {
        // Nobody stated the namespace of the second declaration, so nothing distinguishes
        // it - the same reading a mapping artifact gets under decision 017.
        var bare = """
            using System.ComponentModel.DataAnnotations;

            public partial class Customer
            {
                public string CustomerName { get; set; }
            }
            """;

        var result = Convert(Unit(KeyHalf, "Customer.cs"), Unit(bare, "Customer.Extra.cs"));

        var entity = Assert.Single(Entities(result));
        Assert.Contains("namespace Shop", entity.Content, StringComparison.Ordinal);
        Assert.Contains("string CustomerName", entity.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnplacedEntityIsPlacedByTheDeclarationThatSaysWhereItIs()
    {
        // Three declarations: one that says nothing, one that places the entity in Shop, and
        // one from Billing. The middle one settles where the first belongs, so the third is a
        // different class and not a third loose match - and the answer must not depend on the
        // order the units came in (S2).
        var bare = """
            using System.ComponentModel.DataAnnotations;

            public partial class Customer
            {
                public string CustomerName { get; set; }
            }
            """;

        var result = Convert(
            Unit(bare, "Customer.cs"),
            Unit(KeyHalf, "Shop.Customer.cs"),
            Unit(Half("""
                    [Key]
                    public int CustomerId { get; set; }
                """, ns: "Billing", table: "Clients"), "Billing.Customer.cs"));

        Assert.Equal(2, Entities(result).Count);

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal("Customer", record.Entity);
        Assert.Contains("Shop", record.Reason, StringComparison.Ordinal);
        Assert.Contains("Billing", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoNamespacesAreTwoEntitiesAndTheSharedNameIsSaid()
    {
        var result = Convert(
            Unit(KeyHalf, "Shop.Customer.cs"),
            Unit(Half("""
                    [Key]
                    public int CustomerId { get; set; }
                """, ns: "Billing", table: "Clients"), "Billing.Customer.cs"));

        Assert.Equal(2, Entities(result).Count);

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal("Customer", record.Entity);
        Assert.Contains("Shop", record.Reason, StringComparison.Ordinal);
        Assert.Contains("Billing", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OneEntityOfItsNameIsNoRecord()
    {
        var result = Convert(Unit(KeyHalf, "Customer.cs"));

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void ARepeatedDeclarationDoesNotDuplicateTheRelation()
    {
        const string order = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            namespace Shop;

            [Table("Orders", Schema = "Sales")]
            public class Order
            {
                [Key]
                public int OrderId { get; set; }

                public int CustomerId { get; set; }

                [ForeignKey(nameof(CustomerId))]
                public Customer Customer { get; set; }
            }
            """;

        var result = Convert(
            Unit(KeyHalf, "Customer.cs"),
            Unit(order, "Order.cs"),
            Unit(order, "Order.copy.cs"));

        var artifact = Assert.Single(Entities(result), s => s.Content.Contains("class Order", StringComparison.Ordinal));
        Assert.Equal(1, Occurrences(artifact.Content, "Customer Customer"));
        Assert.Equal(1, Occurrences(artifact.Content, "[ForeignKey"));

        // The second reading writes the navigation's type as the source spells it, where the
        // first reading has already resolved it into a reference; one claim, not two.
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    private static string JavaHalf(string body, string package = Shop, string table = "Customers") =>
        $$"""
        package {{package}};

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.Id;
        import jakarta.persistence.Table;

        @Entity
        @Table(name = "{{table}}", schema = "Sales")
        public class Customer {
        {{body}}
        }
        """;

    private static readonly string JavaKeyHalf = JavaHalf("""
            @Id
            @Column(name = "CustomerId")
            private int customerId;
        """);

    private static ConversionResult ConvertJava(params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.Hibernate, [.. units]);

    private static ConversionSource JavaUnit(string content, string name) => new()
    {
        Name = name,
        ContentType = ConversionContentType.JavaEntity,
        Content = content,
    };

    [Fact]
    public void TheJavaSideReadsIdentityTheSameWay()
    {
        var result = ConvertJava(
            JavaUnit(JavaKeyHalf, "Customer.java"),
            JavaUnit(JavaHalf("""
                    @Column(name = "CustomerName")
                    private String customerName;
                """), "CustomerExtra.java"));

        var entity = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaEntity);
        Assert.Contains("customerId", entity.Content, StringComparison.Ordinal);
        Assert.Contains("customerName", entity.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPackagesAreTwoJavaEntitiesAndTheSharedNameIsSaid()
    {
        var result = ConvertJava(
            JavaUnit(JavaKeyHalf, "Customer.java"),
            JavaUnit(JavaHalf("""
                    @Id
                    @Column(name = "CustomerId")
                    private int customerId;
                """, package: "billing", table: "Clients"), "BillingCustomer.java"));

        Assert.Equal(2, result.Sources.Count(s => s.ContentType == ConversionContentType.JavaEntity));

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal("Customer", record.Entity);
        Assert.Contains("billing", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The container is half of a nested class's identity, though the model does not record
    /// it: two key classes named Key under two entities are two types, and reading them as
    /// one entity would give one of the two the other's key parts.
    /// </summary>
    private static string WithNestedKey(string entity, string table, params string[] parts) =>
        $$"""
        package shop;

        import jakarta.persistence.Column;
        import jakarta.persistence.Embeddable;
        import jakarta.persistence.EmbeddedId;
        import jakarta.persistence.Entity;
        import jakarta.persistence.Table;

        @Entity
        @Table(name = "{{table}}", schema = "Sales")
        public class {{entity}} {
            @EmbeddedId
            private Key id;

            @Embeddable
            public static class Key implements java.io.Serializable {
        {{string.Join("\n", parts.Select(p => $"        @Column(name = \"{p}\")\n        private int {p};\n"))}}
            }
        }
        """;

    [Fact]
    public void TwoNestedKeyClassesOfOneNameAreTwoEntitiesAndAreSaid()
    {
        var result = ConvertJava(
            JavaUnit(WithNestedKey("OrderLine", "OrderLines", "orderId", "lineNumber"), "OrderLine.java"),
            JavaUnit(WithNestedKey("Invoice", "Invoices", "invoiceId", "invoiceYear"), "Invoice.java"));

        // Two key classes, not one merged out of both - and because the representation
        // resolves a key class by its simple name (decision 001), the pair is exactly what
        // the record is for. Which of the two a claim then reaches is decision 001's rule
        // and is not what this decision changes; that it is no longer silent, is.
        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal("Key", record.Entity);
        Assert.Contains("shop.OrderLine", record.Reason, StringComparison.Ordinal);
        Assert.Contains("shop.Invoice", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedClassOfOneContainerIsOneEntityAcrossUnits()
    {
        // The same class twice, container and all: one entity, one key class, nothing to say.
        var unit = WithNestedKey("OrderLine", "OrderLines", "orderId", "lineNumber");

        var result = ConvertJava(JavaUnit(unit, "OrderLine.java"), JavaUnit(unit, "OrderLine.copy.java"));

        var artifact = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaEntity);
        Assert.Contains("class OrderLine", artifact.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }
}
