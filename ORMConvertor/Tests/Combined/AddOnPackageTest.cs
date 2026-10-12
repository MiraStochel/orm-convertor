using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// An add-on package over a framework is not that framework (decision 133): Dapper.Contrib
/// over Dapper and MyBatis-Plus over MyBatis stand outside the scope and are not read, not
/// even in part - but what the reading tells as theirs is named. An attribute or annotation
/// of the package gets a record saying whose it is and what it stated, a call of Contrib is a
/// handover refused by its name, and a mapper extending BaseMapper says that its inherited
/// statements are not translated. The package is told by the unit's imports or a qualified
/// name alone; without either, the reading says what it says of any annotation.
/// </summary>
public class AddOnPackageTest
{
    private static ConversionSource Source(ConversionContentType type, string content, string name) => new()
    {
        Name = name,
        ContentType = type,
        Content = content,
    };

    private static List<ConversionRecord> Records(ConversionResult result, ConversionRecordKind kind, string phrase)
        => [.. result.Records.Where(r => r.Kind == kind && r.Reason.Contains(phrase, StringComparison.Ordinal))];

    /* ---- Dapper.Contrib: the attributes --------------------------------------------- */

    private static string ContribEntity(string usings) => $$"""
        {{usings}}

        namespace Shop;

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }

            [ExplicitKey]
            public int LegacyId { get; set; }

            [Write(false)]
            public string DisplayName { get; set; }

            [Computed]
            public decimal Balance { get; set; }
        }
        """;

    private static ConversionResult FromDapper(ORMEnum target, params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.Dapper, target, [.. units]);

    /// <summary>
    /// Each attribute of the package is a loss that names the package and what the attribute
    /// stated, with the category of the fact where it has one; the class is still translated,
    /// as a Dapper class that states nothing about its table or key.
    /// </summary>
    [Fact]
    public void AnAttributeOfDapperContribIsALossThatNamesThePackage()
    {
        var result = FromDapper(ORMEnum.EFCore,
            Source(ConversionContentType.CSharp, ContribEntity("using Dapper.Contrib.Extensions;"), "Customer.cs"));

        var named = Records(result, ConversionRecordKind.Loss, "it is an attribute of Dapper.Contrib");
        Assert.Equal(5, named.Count);

        Assert.Equal(MappingFactCategory.TableName, Assert.Single(named, r => r.Property is null).Category);
        Assert.Equal(MappingFactCategory.PrimaryKey, Assert.Single(named, r => r.Property == "CustomerId").Category);
        Assert.Equal(MappingFactCategory.PrimaryKey, Assert.Single(named, r => r.Property == "LegacyId").Category);
        Assert.Null(Assert.Single(named, r => r.Property == "DisplayName").Category);
        Assert.Null(Assert.Single(named, r => r.Property == "Balance").Category);

        Assert.All(named, r => Assert.Contains("decision 133", r.Reason, StringComparison.Ordinal));
        Assert.Empty(Records(result, ConversionRecordKind.Loss, "the source framework carries no mapping in the class"));

        var entity = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity);
        Assert.DoesNotContain("Customers", entity.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without the import, the unit does not say whose the attributes are - [Table] may be the
    /// one of DataAnnotations -, so the record is the one of any annotation the structural
    /// reading drops.
    /// </summary>
    [Fact]
    public void WithoutTheImportTheAttributesAreAnyAnnotation()
    {
        var result = FromDapper(ORMEnum.EFCore, Source(ConversionContentType.CSharp, ContribEntity(string.Empty), "Customer.cs"));

        Assert.Empty(Records(result, ConversionRecordKind.Loss, "Dapper.Contrib"));
        Assert.Equal(5, Records(result, ConversionRecordKind.Loss, "the source framework carries no mapping in the class").Count);
    }

    /// <summary>
    /// [Table] and [Key] are names DataAnnotations has too; in a unit that imports both
    /// namespaces the two are no one's in particular, while the names only the package has
    /// stay its.
    /// </summary>
    [Fact]
    public void ANameDataAnnotationsSharesIsNotToldWhereTheUnitImportsBoth()
    {
        var result = FromDapper(ORMEnum.EFCore,
            Source(ConversionContentType.CSharp,
                ContribEntity("""
                    using System.ComponentModel.DataAnnotations;
                    using System.ComponentModel.DataAnnotations.Schema;
                    using Dapper.Contrib.Extensions;
                    """),
                "Customer.cs"));

        var named = Records(result, ConversionRecordKind.Loss, "it is an attribute of Dapper.Contrib");
        Assert.Equal(new[] { "Balance", "DisplayName", "LegacyId" }, named.Select(r => r.Property!).Order());
        Assert.Equal(2, Records(result, ConversionRecordKind.Loss, "the source framework carries no mapping in the class").Count);
    }

    /// <summary>A name qualified with the namespace of the package is its, import or not, and the suffix Attribute changes nothing.</summary>
    [Fact]
    public void AQualifiedAttributeIsThePackagesWithoutTheImport()
    {
        var result = FromDapper(ORMEnum.EFCore,
            Source(ConversionContentType.CSharp,
                """
                namespace Shop;

                [global::Dapper.Contrib.Extensions.TableAttribute("Customers")]
                public class Customer
                {
                    [Dapper.Contrib.Extensions.Key]
                    public int CustomerId { get; set; }
                }
                """,
                "Customer.cs"));

        var named = Records(result, ConversionRecordKind.Loss, "it is an attribute of Dapper.Contrib");
        Assert.Equal(2, named.Count);
        Assert.Contains(named, r => r.Category == MappingFactCategory.TableName && r.Property is null);
        Assert.Contains(named, r => r.Category == MappingFactCategory.PrimaryKey && r.Property == "CustomerId");
    }

    /// <summary>
    /// The conventions of the package are not read either: a class without attributes states
    /// nothing in a unit that imports the package as much as anywhere (decision 067), so the
    /// property Id is no key - and NHibernate, which requires one, refuses the entity.
    /// </summary>
    [Fact]
    public void TheConventionsOfDapperContribAreNotRead()
    {
        var result = FromDapper(ORMEnum.NHibernate,
            Source(ConversionContentType.CSharp,
                """
                using Dapper.Contrib.Extensions;

                namespace Shop;

                public class Customer
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }
                """,
                "Customer.cs"));

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure
            && r.Entity == "Customer"
            && r.Category == MappingFactCategory.PrimaryKey);
        Assert.Empty(Records(result, ConversionRecordKind.Loss, "Dapper.Contrib"));
    }

    /* ---- Dapper.Contrib: the calls --------------------------------------------------- */

    private const string RepositoryUnit = "CustomerRepository.cs";

    private static string Repository(string usings) => $$"""
        {{usings}}

        namespace Shop;

        public class CustomerRepository
        {
            public Customer Find(IDbConnection connection, int id) => connection.Get<Customer>(id);

            public IEnumerable<Customer> All(IDbConnection connection) => connection.GetAll<Customer>();

            public IEnumerable<Customer> Rich(IDbConnection connection)
                => connection.Query<Customer>("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit > 2000");

            public long Add(IDbConnection connection, Customer customer) => connection.Insert(customer);
        }
        """;

    private static ConversionResult FromRepository(string usings)
        => FromDapper(ORMEnum.Dapper,
            [
                .. CrossFrameworkInputs.MappingUnits(ORMEnum.Dapper),
                Source(ConversionContentType.CSharp, Repository(usings), RepositoryUnit),
            ]);

    /// <summary>
    /// A call of the package hands a query over as much as a call of Dapper does (decision 109),
    /// so it has a number of its own and is refused by its name: a read because the package
    /// composes its text at run time, a write because it writes. The Dapper call beside them is
    /// read as before, under its number, and the class around them is the code around queries,
    /// not an entity (decision 111).
    /// </summary>
    [Fact]
    public void ACallOfDapperContribIsAHandoverRefusedByName()
    {
        var result = FromRepository("using Dapper.Contrib.Extensions;");

        var refusals = result.Records.Where(r => r.Kind == ConversionRecordKind.Failure && r.Unit == RepositoryUnit).ToList();
        Assert.Equal(3, refusals.Count);

        var get = Assert.Single(refusals, r => r.Reason.Contains("The call 'Get<Customer>'", StringComparison.Ordinal));
        Assert.Equal("Query01", get.Query);
        Assert.Contains("composes the SQL of the call at run time", get.Reason, StringComparison.Ordinal);

        var getAll = Assert.Single(refusals, r => r.Reason.Contains("The call 'GetAll<Customer>'", StringComparison.Ordinal));
        Assert.Equal("Query02", getAll.Query);

        var insert = Assert.Single(refusals, r => r.Reason.Contains("The call 'Insert'", StringComparison.Ordinal));
        Assert.Equal("Query04", insert.Query);
        Assert.Contains("and it writes", insert.Reason, StringComparison.Ordinal);

        Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Convention
            && r.Reason.Contains("The class 'CustomerRepository' hands a query over", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity
            && s.Content.Contains("class CustomerRepository", StringComparison.Ordinal));
    }

    /// <summary>
    /// Without the import the extension methods are not in scope of the unit, and a call named
    /// Get is anyone's: it is not told, the Dapper call stays the only query and keeps the
    /// fixed name. That the class around it is then read on the Dapper call alone is the
    /// stated limit of a library the reading does not know.
    /// </summary>
    [Fact]
    public void WithoutTheImportACallNamedGetIsNotTold()
    {
        var result = FromRepository(string.Empty);

        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("Dapper.Contrib", StringComparison.Ordinal));
        Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery);
        Assert.DoesNotContain(result.Records, r => r.Unit == RepositoryUnit && r.Query is not null);
    }

    /* ---- MyBatis-Plus ------------------------------------------------------------------ */

    private const string PlusDomainClass = """
        package Shop;

        import com.baomidou.mybatisplus.annotation.*;

        @TableName("customers")
        public class Customer {
            @TableId(type = IdType.AUTO)
            private Integer CustomerId;

            @TableField("customer_name")
            private String CustomerName;

            @Version
            private Integer Revision;

            @TableLogic
            private Integer Deleted;
        }
        """;

    private static ConversionResult FromMyBatis(ORMEnum target, params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.MyBatis, target, [.. units]);

    /// <summary>
    /// Each annotation of the package on the domain class is a loss that names the package,
    /// with the category of the fact where it states one; the class is translated as the plain
    /// MyBatis class it is.
    /// </summary>
    [Fact]
    public void AnAnnotationOfMyBatisPlusIsALossThatNamesThePackage()
    {
        var result = FromMyBatis(ORMEnum.MyBatis, Source(ConversionContentType.Java, PlusDomainClass, "Customer.java"));

        var named = Records(result, ConversionRecordKind.Loss, "it is an annotation of MyBatis-Plus");
        Assert.Equal(5, named.Count);
        Assert.Equal(MappingFactCategory.TableName, Assert.Single(named, r => r.Property is null).Category);
        Assert.Equal(MappingFactCategory.PrimaryKey, Assert.Single(named, r => r.Property == "CustomerId").Category);
        Assert.Equal(MappingFactCategory.ColumnName, Assert.Single(named, r => r.Property == "CustomerName").Category);
        Assert.Equal(MappingFactCategory.VersionColumn, Assert.Single(named, r => r.Property == "Revision").Category);
        Assert.Null(Assert.Single(named, r => r.Property == "Deleted").Category);
        Assert.Empty(Records(result, ConversionRecordKind.Loss, "the source framework carries no mapping in the class"));

        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.JavaEntity && s.Content.Contains("class Customer"));
    }

    /// <summary>
    /// @Version is a name Jakarta Persistence has too; where the unit imports both, it is no
    /// one's in particular, and the names only the package has stay its.
    /// </summary>
    [Fact]
    public void ANameJakartaPersistenceSharesIsNotToldWhereTheUnitImportsBoth()
    {
        var result = FromMyBatis(ORMEnum.Hibernate,
            Source(ConversionContentType.Java,
                PlusDomainClass.Replace(
                    "import com.baomidou.mybatisplus.annotation.*;",
                    "import com.baomidou.mybatisplus.annotation.*;\nimport jakarta.persistence.Version;",
                    StringComparison.Ordinal),
                "Customer.java"));

        Assert.DoesNotContain(Records(result, ConversionRecordKind.Loss, "it is an annotation of MyBatis-Plus"), r => r.Property == "Revision");
        Assert.Single(Records(result, ConversionRecordKind.Loss, "the source framework carries no mapping in the class"), r => r.Property == "Revision");
    }

    /// <summary>
    /// A mapper extending the package's BaseMapper inherits statements no text of the unit
    /// declares: one loss on the interface says so, and the statement the interface declares
    /// itself is read as any other.
    /// </summary>
    [Fact]
    public void AMapperExtendingBaseMapperSaysItsInheritedStatementsAreNotTranslated()
    {
        var result = FromMyBatis(ORMEnum.Dapper,
            Source(ConversionContentType.Java,
                """
                package Shop;

                import java.math.BigDecimal;

                public class Customer {
                    private Integer CustomerId;
                    private String CustomerName;
                    private BigDecimal CreditLimit;
                }
                """,
                "Customer.java"),
            Source(ConversionContentType.Java,
                """
                package Shop;

                import java.util.List;
                import com.baomidou.mybatisplus.core.mapper.BaseMapper;
                import org.apache.ibatis.annotations.Select;

                public interface CustomerMapper extends BaseMapper<Customer> {
                    @Select("SELECT c.CustomerName FROM Sales.Customers AS c")
                    List<Customer> findAll();
                }
                """,
                "CustomerMapper.java"));

        var inherited = Assert.Single(Records(result, ConversionRecordKind.Loss, "extends BaseMapper<Customer> of MyBatis-Plus"));
        Assert.Equal("CustomerMapper.java", inherited.Unit);
        Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
    }
}
