using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// Two ways the tool used to state something nobody told it, and one way it used to stay
/// silent about what it threw away. Both are the same rule seen from opposite ends
/// (decisions 004 and 010): generate only what is known, and say what is lost.
/// </summary>
public class InventedFactsTest
{
    private const string EntityWithNamespace = """
        namespace Shop.Sales;

        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
        }
        """;

    private const string MappingWithNamespace = """
        <?xml version="1.0" encoding="utf-8"?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Shop.Sales">
          <class name="Shop.Sales.Customer, Shop.Sales" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32">
              <generator class="identity" />
            </id>
            <property name="CustomerName" column="CustomerName" type="String" />
          </class>
        </hibernate-mapping>
        """;

    /// <summary>
    /// Decision 028: the namespace belongs on the root, the class name is bare, and the
    /// assembly - which the conversion cannot know - is not written at all.
    /// </summary>
    [Fact]
    public void TheNHibernateMappingDoesNotInventAnAssemblyName()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(EntityWithNamespace);
        new NHibernateXMLMappingParser(builder).Parse(MappingWithNamespace);

        var mapping = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("namespace=\"Shop.Sales\"", mapping);
        Assert.Contains("<class name=\"Customer\"", mapping);
        Assert.DoesNotContain("assembly=", mapping);
        Assert.DoesNotContain("Shop.Sales.Customer, Shop.Sales", mapping);
    }

    /// <summary>The qualified form stays readable - real mappings are written that way.</summary>
    [Fact]
    public void TheQualifiedFormIsStillParsed()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(EntityWithNamespace);
        new NHibernateXMLMappingParser(builder).Parse(MappingWithNamespace);

        var map = Assert.Single(builder.EntityMaps);

        Assert.Equal("Customer", map.Entity.Name);
        Assert.Equal("Customers", map.Table);
    }

    /// <summary>
    /// The EF Core attribute switch had no default branch, so every annotation outside its
    /// seven cases vanished without a record - including two that change what the artifact
    /// means.
    /// </summary>
    [Theory]
    [InlineData("NotMapped")]
    [InlineData("ConcurrencyCheck")]
    [InlineData("StringLength(50)")]
    public void AnUnreadEFCoreAnnotationIsReported(string annotation)
    {
        var source = $$"""
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [{{annotation}}]
                public string CustomerName { get; set; }
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);
        builder.Build();

        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss
                 && r.Property == "CustomerName"
                 && r.Reason.Contains(annotation.Split('(')[0]));
    }

    /// <summary>A recognised annotation must not be reported as lost.</summary>
    [Fact]
    public void ARecognisedAnnotationIsNotReported()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                [MaxLength(50)]
                public string CustomerName { get; set; }
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);
        builder.Build();

        Assert.DoesNotContain(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("MaxLength"));
    }
}
