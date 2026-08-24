using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// An identifier cannot be nullable in either C# target, so a source's <c>int?</c> key comes
/// out as <c>int</c>. That behaviour is right and does not change; what changes is that it no
/// longer happens in silence (decision 054). Dapper is the control: it knows no keys, keeps
/// the question mark, and must therefore not report the loss.
/// </summary>
public class NullableKeyPartTest
{
    private const string NullableKeyEntity = """
        public class Product
        {
            public int? Id { get; set; }

            public string Name { get; set; }
        }
        """;

    private static bool ReportsNullabilityLoss(AbstractEntityBuilder builder)
        => builder.Records.Any(r =>
            r.Kind == ConversionRecordKind.Loss
            && r.Category == MappingFactCategory.Nullability
            && r.Property == "Id");

    [Fact]
    public void FlatteningIsReportedByBothCSharpTargets()
    {
        var efCore = new EFCoreEntityBuilder();
        new EFCoreEntityParser(efCore).Parse(NullableKeyEntity);
        efCore.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");

        var nhibernate = new NHibernateEntityBuilder();
        new NHibernateEntityParser(nhibernate).Parse(NullableKeyEntity);
        nhibernate.AddTable("Products");
        nhibernate.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");

        foreach (var builder in new AbstractEntityBuilder[] { efCore, nhibernate })
        {
            var code = builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpEntity).Content;

            Assert.Contains("int Id", code, StringComparison.Ordinal);
            Assert.DoesNotContain("int? Id", code, StringComparison.Ordinal);

            Assert.True(ReportsNullabilityLoss(builder), $"{builder.Descriptor.Framework} said nothing about the dropped nullability.");
        }
    }

    [Fact]
    public void DapperKeepsTheQuestionMarkAndSaysNothing()
    {
        var builder = new DapperEntityBuilder();
        new DapperEntityParser(builder).Parse(NullableKeyEntity);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");

        var code = builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpEntity).Content;

        // Dapper has no key mechanism, so nothing is flattened and the record would be untrue.
        Assert.Contains("int? Id", code, StringComparison.Ordinal);
        Assert.False(ReportsNullabilityLoss(builder));
    }

    [Fact]
    public void ANonNullableKeyPartIsReportedByNobody()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Product
            {
                public int Id { get; set; }
            }
            """);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");
        builder.Build();

        // Reporting "nothing was lost" is the noise decision 010 keeps out of the records.
        Assert.False(ReportsNullabilityLoss(builder));
    }
}
