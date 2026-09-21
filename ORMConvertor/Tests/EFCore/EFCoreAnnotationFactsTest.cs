using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using NHibernateWrappers;

namespace Tests.EFCore;

/// <summary>
/// Three annotations the parser used to answer with the record of decision 048 - the answer
/// reserved for a fact the representation has no place for - although the place was there all
/// along: [StringLength] is a length, [Unicode] the IsUnicode facet, [InverseProperty] the
/// name that pairs the two ends of a relation. A record over a place that exists says the
/// opposite of what it means and hides the annotations that really have nowhere to go.
/// </summary>
public class EFCoreAnnotationFactsTest
{
    private static EFCoreEntityBuilder Parsed(string source)
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);
        builder.Build();
        return builder;
    }

    [Fact]
    public void StringLengthIsReadAsTheLength()
    {
        var builder = Parsed("""
            using System.ComponentModel.DataAnnotations;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [StringLength(50)]
                public string CustomerName { get; set; }
            }
            """);

        Assert.Equal(50, builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "CustomerName").Length);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("StringLength"));
    }

    /// <summary>
    /// The minimum length and the message are validation, which EF Core's own model does not
    /// carry either: nothing of the mapping goes with them, so no record is due.
    /// </summary>
    [Fact]
    public void TheValidationHalfOfStringLengthIsNoMappingFact()
    {
        var builder = Parsed("""
            using System.ComponentModel.DataAnnotations;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [StringLength(50, MinimumLength = 5, ErrorMessage = "too short")]
                public string CustomerName { get; set; }
            }
            """);

        Assert.Equal(50, builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "CustomerName").Length);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "CustomerName");
    }

    /// <summary>
    /// Both spellings of one fact on one property: the first reading stands and the
    /// difference is a record, as decision 017 rules everywhere else.
    /// </summary>
    [Fact]
    public void TwoLengthsOnOnePropertyKeepTheFirstAndReportTheDifference()
    {
        var builder = Parsed("""
            using System.ComponentModel.DataAnnotations;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [MaxLength(50)]
                [StringLength(100)]
                public string CustomerName { get; set; }
            }
            """);

        Assert.Equal(50, builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "CustomerName").Length);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Conflict
                 && r.Property == "CustomerName"
                 && r.Reason.Contains("StringLength"));
    }

    [Theory]
    [InlineData("[Unicode(false)]", false)]
    [InlineData("[Unicode(true)]", true)]
    [InlineData("[Unicode]", true)]
    public void UnicodeIsReadAsTheFacet(string annotation, bool expected)
    {
        var builder = Parsed($$"""
            using Microsoft.EntityFrameworkCore;
            using System.ComponentModel.DataAnnotations;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                {{annotation}}
                public string CustomerName { get; set; }
            }
            """);

        Assert.Equal(expected, builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "CustomerName").IsUnicode);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("Unicode"));
    }

    /// <summary>
    /// EF Core lets an explicit store type decide and ignores the facet beside it, so the
    /// type name's claim is the one that stands - and the disagreement is said out loud
    /// rather than resolved in silence.
    /// </summary>
    [Fact]
    public void UnicodeBesideAnExplicitStoreTypeLosesToTheTypeName()
    {
        var builder = Parsed("""
            using Microsoft.EntityFrameworkCore;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [Unicode(false)]
                [Column(TypeName = "nvarchar(50)")]
                public string CustomerName { get; set; }
            }
            """);

        var map = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "CustomerName");

        Assert.True(map.IsUnicode);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Conflict
                 && r.Property == "CustomerName"
                 && r.Category == MappingFactCategory.DatabaseType);
    }

    /// <summary>
    /// A facet stated without a family is a shape the model already knew - @Nationalized on a
    /// Java String arrives the same way - and every target answers it with what it has: EF
    /// Core writes the annotation back, because it is precisely its own convention that the
    /// source ruled out, and NHibernate names the type that means both at once.
    /// </summary>
    [Fact]
    public void AUnicodeFacetWithNoTypeFamilyReachesBothDotNetTargets()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using System.ComponentModel.DataAnnotations;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [Unicode(false)]
                [MaxLength(50)]
                public string CustomerName { get; set; }
            }
            """;

        var efCore = new EFCoreEntityBuilder();
        new EFCoreEntityParser(efCore).Parse(source);
        var again = efCore.Build().Single(a => a.ContentType == ConversionContentType.CSharpEntity).Content;

        Assert.Contains("[Unicode(false)]", again, StringComparison.Ordinal);
        Assert.Contains("using Microsoft.EntityFrameworkCore;", again, StringComparison.Ordinal);

        var nHibernate = new NHibernateEntityBuilder();
        new EFCoreEntityParser(nHibernate).Parse(source);
        var mapping = nHibernate.Build().Single(a => a.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("type=\"AnsiString\"", mapping, StringComparison.Ordinal);
    }

    /// <summary>
    /// What [InverseProperty] is for: two pairs of navigations between the same two entities,
    /// which no convention can pair up. Both ends name their counterpart and the model keeps
    /// the name on each relation.
    /// </summary>
    [Fact]
    public void InversePropertyNamesTheFarEndOfTheRelation()
    {
        var builder = Parsed("""
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            public class Employee
            {
                [Key]
                public int EmployeeId { get; set; }

                [InverseProperty("Author")]
                public List<Article> Written { get; set; }

                [InverseProperty("Reviewer")]
                public List<Article> Reviewed { get; set; }
            }

            public class Article
            {
                [Key]
                public int ArticleId { get; set; }

                [InverseProperty("Written")]
                public Employee Author { get; set; }

                [InverseProperty("Reviewed")]
                public Employee Reviewer { get; set; }
            }
            """);

        var employee = builder.EntityMaps.Single(em => em.Entity.Name == "Employee");
        var article = builder.EntityMaps.Single(em => em.Entity.Name == "Article");

        Assert.Equal("Author", employee.Relations.Single(r => r.SourceNavigationProperty == "Written").InverseRelationName);
        Assert.Equal("Reviewer", employee.Relations.Single(r => r.SourceNavigationProperty == "Reviewed").InverseRelationName);
        Assert.Equal("Written", article.Relations.Single(r => r.SourceNavigationProperty == "Author").InverseRelationName);
        Assert.Equal("Reviewed", article.Relations.Single(r => r.SourceNavigationProperty == "Reviewer").InverseRelationName);

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("InverseProperty"));
    }

    /// <summary>
    /// The whole way through: what [InverseProperty] states in C# is what mappedBy states in
    /// Java. Without the name the JPA builder has two candidates for each collection, answers
    /// neither, and both fall back to a join column of the same made-up name.
    /// </summary>
    [Fact]
    public void ANamedFarEndTravelsFromTheAnnotationIntoTheJavaArtifact()
    {
        var builder = new HibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            public class Employee
            {
                [Key]
                public int EmployeeId { get; set; }

                [InverseProperty("Author")]
                public List<Article> Written { get; set; }

                [InverseProperty("Reviewer")]
                public List<Article> Reviewed { get; set; }
            }

            public class Article
            {
                [Key]
                public int ArticleId { get; set; }

                [ForeignKey("AuthorId")]
                [InverseProperty("Written")]
                public Employee Author { get; set; }

                public int AuthorId { get; set; }

                [ForeignKey("ReviewerId")]
                [InverseProperty("Reviewed")]
                public Employee Reviewer { get; set; }

                public int ReviewerId { get; set; }
            }
            """);

        var employee = builder.Build()
            .First(a => a.ContentType == ConversionContentType.JavaEntity && a.Content.Contains("class Employee"))
            .Content;

        Assert.Contains("@OneToMany(mappedBy = \"Author\")", employee, StringComparison.Ordinal);
        Assert.Contains("@OneToMany(mappedBy = \"Reviewer\")", employee, StringComparison.Ordinal);
    }

    /// <summary>
    /// The annotation reaches the model through a relation, and a scalar founds none: there
    /// the claim really has nowhere to go and the record of decision 048 is due.
    /// </summary>
    [Fact]
    public void InversePropertyOnAScalarHasNowhereToGo()
    {
        var builder = Parsed("""
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [InverseProperty("Nothing")]
                public string CustomerName { get; set; }
            }
            """);

        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss
                 && r.Property == "CustomerName"
                 && r.Reason.Contains("InverseProperty"));
    }
}
