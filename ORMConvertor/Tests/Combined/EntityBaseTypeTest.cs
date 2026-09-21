using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The stated base class of an entity, in both ecosystems (decision 048). Inheritance
/// itself stays excluded area 2 of the guarantees (architecture, §9) - the model has no
/// place for a hierarchy and this does not give it one. What used to be missing is the word
/// about it: the same fact in an NHibernate mapping (&lt;subclass&gt;) has had a loss record
/// since decision 030, while a class deriving from another entity of the conversion left no
/// trace at all, although EF Core maps exactly that by convention as table per hierarchy.
///
/// The reading is the shared one in each ecosystem, so the record belongs to no single
/// framework: all three .NET entity parsers inherit it from <c>CSharpEntityParser</c> and
/// both Java ones from <c>JavaEntityParser</c>, and the theories below are what hold them
/// to it. The judgement is one judgement - the builder's, once the whole entity set is
/// known - so the two halves cannot drift apart.
/// </summary>
public class EntityBaseTypeTest
{
    private const string Shop = "Shop";

    private static string Unit(string body, string ns = Shop) =>
        $$"""
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace {{ns}};

        {{body}}
        """;

    private static readonly string Hierarchy = Unit("""
        [Table("People")]
        public class Person
        {
            [Key]
            public int PersonId { get; set; }

            public string PersonName { get; set; }
        }

        [Table("People")]
        public class Employee : Person
        {
            public decimal Salary { get; set; }
        }
        """);

    private static ConversionSource Source(string content, string name) => new()
    {
        Name = name,
        ContentType = ConversionContentType.CSharpEntity,
        Content = content,
    };

    private static ConversionResult Convert(ORMEnum source, params ConversionSource[] units)
        => ConversionHandler.Convert(source, ORMEnum.EFCore, [.. units]);

    private static ConversionResult Convert(params ConversionSource[] units)
        => Convert(ORMEnum.EFCore, units);

    /// <summary>
    /// The records this behaviour produces, told from every other loss by the sentence they
    /// share. A test that filtered by kind alone would be hostage to whatever else the
    /// target framework cannot express.
    /// </summary>
    private static List<ConversionRecord> BaseTypeLosses(ConversionResult result)
        => [.. result.Records.Where(r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("derives from"))];

    [Fact]
    public void DerivingFromAnotherEntityOfTheConversionIsAReportedLoss()
    {
        var result = Convert(Source(Hierarchy, "People.cs"));

        var loss = Assert.Single(BaseTypeLosses(result));

        Assert.Equal("Employee", loss.Entity);
        Assert.Contains("Person", loss.Reason);

        // The record is about the class, not about one of its properties.
        Assert.Null(loss.Property);

        // Decision 048: the categories are a closed vocabulary of facts the model holds and
        // a hierarchy is not one of them, so naming the nearest would claim more than is
        // known.
        Assert.Null(loss.Category);

        // Decision 066: a record that needs the whole entity set to arise belongs to no one
        // input unit - either entity may have been declared by several.
        Assert.Null(loss.Unit);
    }

    /// <summary>
    /// The loss is a word about the hierarchy, not a refusal: both classes are translated,
    /// each as an entity of its own, which is what the record says will happen.
    /// </summary>
    [Fact]
    public void BothClassesOfTheHierarchyAreStillTranslated()
    {
        var result = Convert(Source(Hierarchy, "People.cs"));

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        var entities = result.Sources
            .Where(s => s.ContentType == ConversionContentType.CSharpEntity)
            .ToList();

        Assert.Equal(2, entities.Count);
        Assert.Contains(entities, s => s.Content.Contains("class Person"));
        Assert.Contains(entities, s => s.Content.Contains("class Employee"));
    }

    /// <summary>
    /// The reading is shared, so all three .NET source frameworks report it. Dapper and
    /// NHibernate add nothing to the shared C# parser (architecture, §5), and neither of
    /// them may be the one framework whose input is heard on this.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.EFCore)]
    public void EverySharedCSharpParserReportsIt(ORMEnum sourceFramework)
    {
        var result = Convert(sourceFramework, Source(Hierarchy, "People.cs"));

        var loss = Assert.Single(BaseTypeLosses(result));

        Assert.Equal("Employee", loss.Entity);
    }

    /// <summary>
    /// A base type no unit of the conversion declares is dropped without a record, the way
    /// any unknown type name is: the header does not say which of its base types is the
    /// class and which are interfaces, so a record about a type the parser has never seen
    /// would claim more than is known.
    /// </summary>
    [Theory]
    [InlineData("INotifyPropertyChanged")]
    [InlineData("IEquatable<Employee>")]
    [InlineData("EntityBase")]
    [InlineData("EntityBase<Employee>")]
    public void ABaseTypeOutsideTheConversionIsSilent(string baseType)
    {
        var result = Convert(Source(Unit($$"""
            public class Employee : {{baseType}}
            {
                [Key]
                public int EmployeeId { get; set; }
            }
            """), "Employee.cs"));

        Assert.Empty(BaseTypeLosses(result));
    }

    /// <summary>
    /// Entities are referenced by simple name (decision 001), so a base type from outside
    /// the conversion whose simple name is the deriving class's own resolves to that class
    /// itself. No class extends itself, so the claim is dropped rather than reported.
    /// </summary>
    [Fact]
    public void ABaseTypeResolvingToTheClassItselfIsSilent()
    {
        var result = Convert(Source(Unit("""
            public class Employee : Legacy.Employee
            {
                [Key]
                public int EmployeeId { get; set; }
            }
            """), "Employee.cs"));

        Assert.Empty(BaseTypeLosses(result));
    }

    /// <summary>
    /// The judgement waits for the whole entity set, so the order of the declarations does
    /// not decide whether the hierarchy is seen - neither inside one unit nor across two
    /// (S2, decision 066).
    /// </summary>
    [Fact]
    public void TheBaseMayBeDeclaredAfterTheDerivedClass()
    {
        var derived = Unit("""
            [Table("People")]
            public class Employee : Person
            {
                public decimal Salary { get; set; }
            }
            """);

        var basis = Unit("""
            [Table("People")]
            public class Person
            {
                [Key]
                public int PersonId { get; set; }
            }
            """);

        var loss = Assert.Single(BaseTypeLosses(Convert(
            Source(derived, "Employee.cs"),
            Source(basis, "Person.cs"))));

        Assert.Equal("Employee", loss.Entity);
    }

    /// <summary>
    /// Identical repetition is not an event (decision 017): a partial class may state its
    /// base type in another part, and two units may be the same file (decision 094), so the
    /// claim is one claim and the record is one record.
    /// </summary>
    [Fact]
    public void TheSameClaimStatedTwiceIsOneRecord()
    {
        var half = Unit("""
            [Table("People")]
            public class Person
            {
                [Key]
                public int PersonId { get; set; }
            }

            [Table("People")]
            public partial class Employee : Person
            {
                public decimal Salary { get; set; }
            }
            """);

        var loss = Assert.Single(BaseTypeLosses(Convert(
            Source(half, "Employee.cs"),
            Source(half, "Employee.Copy.cs"))));

        Assert.Equal("Employee", loss.Entity);
    }

    /// <summary>
    /// One record per stated base type, not per hierarchy: a chain of three classes loses
    /// two links and says so twice.
    /// </summary>
    [Fact]
    public void EveryLinkOfAChainIsReported()
    {
        var result = Convert(Source(Unit("""
            [Table("People")]
            public class Person
            {
                [Key]
                public int PersonId { get; set; }
            }

            [Table("People")]
            public class Employee : Person
            {
                public decimal Salary { get; set; }
            }

            [Table("People")]
            public class Manager : Employee
            {
                public int TeamSize { get; set; }
            }
            """), "People.cs"));

        var losses = BaseTypeLosses(result);

        Assert.Equal(2, losses.Count);
        Assert.Contains(losses, r => r.Entity == "Employee" && r.Reason.Contains("from Person"));
        Assert.Contains(losses, r => r.Entity == "Manager" && r.Reason.Contains("from Employee"));
    }

    // ---- The Java half: the same fact, the same channel, the same record ------------

    private static ConversionSource JavaSource(string content, string name) => new()
    {
        Name = name,
        ContentType = ConversionContentType.JavaEntity,
        Content = content,
    };

    private static ConversionResult ConvertJava(ORMEnum sourceFramework, params ConversionSource[] units)
        => ConversionHandler.Convert(sourceFramework, ORMEnum.EFCore, [.. units]);

    private const string JavaHierarchy = """
        package shop;

        import jakarta.persistence.Entity;
        import jakarta.persistence.Id;
        import jakarta.persistence.Table;

        @Entity
        @Table(name = "People")
        class Person {
            @Id
            private Integer personId;
            private String personName;
        }

        @Entity
        @Table(name = "Employees")
        class Employee extends Person {
            private java.math.BigDecimal salary;
        }
        """;

    /// <summary>
    /// Until this was read the Java side was silent where the C# side had just learned to
    /// speak: <c>@Inheritance</c> is reported as an annotation the model cannot keep, but
    /// the default strategy of JPA needs no annotation, so a bare <c>extends</c> between two
    /// entities said nothing at all. <c>JavaClass.Extends</c> was read and then read by
    /// nobody.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void EverySharedJavaParserReportsAStatedBaseClass(ORMEnum sourceFramework)
    {
        var result = ConvertJava(sourceFramework, JavaSource(JavaHierarchy, "People.java"));

        var loss = Assert.Single(BaseTypeLosses(result));

        Assert.Equal("Employee", loss.Entity);
        Assert.Contains("Person", loss.Reason);
        Assert.Null(loss.Property);
        Assert.Null(loss.Category);
        Assert.Null(loss.Unit);
    }

    /// <summary>
    /// The word, not a refusal - the same as in C#.
    /// </summary>
    [Fact]
    public void BothJavaClassesOfTheHierarchyAreStillTranslated()
    {
        var result = ConvertJava(ORMEnum.Hibernate, JavaSource(JavaHierarchy, "People.java"));

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        var entities = result.Sources
            .Where(s => s.ContentType == ConversionContentType.CSharpEntity)
            .ToList();

        Assert.Equal(2, entities.Count);
        Assert.Contains(entities, s => s.Content.Contains("class Person"));
        Assert.Contains(entities, s => s.Content.Contains("class Employee"));
    }

    /// <summary>
    /// Java says outright which base type is the class, so the interfaces are never
    /// candidates and a base class no unit declares stays silent, as in C#.
    /// </summary>
    [Theory]
    [InlineData("implements java.io.Serializable")]
    [InlineData("extends AuditedEntity")]
    [InlineData("extends shop.legacy.Employee")]
    public void AJavaBaseTypeOutsideTheConversionIsSilent(string header)
    {
        var result = ConvertJava(ORMEnum.Hibernate, JavaSource($$"""
            package shop;

            import jakarta.persistence.Entity;
            import jakarta.persistence.Id;

            @Entity
            class Employee {{header}} {
                @Id
                private Integer employeeId;
            }
            """, "Employee.java"));

        Assert.Empty(BaseTypeLosses(result));
    }

    /// <summary>
    /// A mapped superclass is the case the criterion "names another entity of the
    /// conversion" does not describe by itself: the base is no entity in the source, yet it
    /// carries mapped attributes that belong to the table of every entity extending it. Two
    /// facts are lost, so two records are written - one about the hierarchy, one about what
    /// the hierarchy carried - and the second no longer arrives as the generic sentence
    /// about an annotation with no counterpart, which said far less than the annotation
    /// means.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void AMappedSuperclassLosesItsHierarchyAndItsAttributes(ORMEnum sourceFramework)
    {
        var result = ConvertJava(sourceFramework, JavaSource("""
            package shop;

            import jakarta.persistence.Entity;
            import jakarta.persistence.Id;
            import jakarta.persistence.MappedSuperclass;

            @MappedSuperclass
            class Auditable {
                private java.time.LocalDateTime createdAt;
            }

            @Entity
            class Employee extends Auditable {
                @Id
                private Integer employeeId;
            }
            """, "Employee.java"));

        var hierarchy = Assert.Single(BaseTypeLosses(result));
        Assert.Equal("Employee", hierarchy.Entity);
        Assert.Contains("Auditable", hierarchy.Reason);

        var carried = Assert.Single(
            result.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("@MappedSuperclass"));

        Assert.Contains("Auditable is not an entity", carried.Reason);
        Assert.Contains("do not receive its attributes", carried.Reason);

        // The generic sentence is what it replaced, not what it sits beside.
        Assert.DoesNotContain(
            result.Records,
            r => r.Reason.Contains("The class annotation @MappedSuperclass has no counterpart"));
    }

    /// <summary>
    /// The judgement waits for the whole entity set on this side too, so the order of the
    /// units does not decide whether the hierarchy is seen (S2).
    /// </summary>
    [Fact]
    public void TheJavaBaseMayBeDeclaredAfterTheDerivedClass()
    {
        const string derived = """
            package shop;

            import jakarta.persistence.Entity;

            @Entity
            class Employee extends Person {
                private java.math.BigDecimal salary;
            }
            """;

        const string basis = """
            package shop;

            import jakarta.persistence.Entity;
            import jakarta.persistence.Id;

            @Entity
            class Person {
                @Id
                private Integer personId;
            }
            """;

        var loss = Assert.Single(BaseTypeLosses(ConvertJava(
            ORMEnum.Hibernate,
            JavaSource(derived, "Employee.java"),
            JavaSource(basis, "Person.java"))));

        Assert.Equal("Employee", loss.Entity);
    }
}
