using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using Model;

namespace Tests.Combined;

/// <summary>
/// A defect of the tool met while generating one entity refuses that entity with a Failure
/// record and a <see cref="TranslationDefect"/> and leaves the others alone (decision 196).
/// The builder under test is the suite's own <see cref="DummyEntityBuilder"/>, whose
/// <c>FinalizeBuild</c> throws by design: it is the one builder in the solution that fails
/// for every entity, which is exactly what a defect looks like from the outside.
/// </summary>
public class TranslationDefectTest
{
    private const string TwoEntities = """
        public class Customer { public int Id { get; set; } public string Name { get; set; } }
        public class Order { public int Id { get; set; } public int CustomerId { get; set; } }
        """;

    private static AbstractEntityBuilder Built()
    {
        AbstractEntityBuilder builder = new DummyEntityBuilder();
        new DapperEntityParser(builder).Parse(TwoEntities);
        builder.Build();
        return builder;
    }

    [Fact]
    public void ADefectInOneEntityIsAFailureRecordNamingTheEntity()
    {
        var builder = Built();

        foreach (var entity in new[] { "Customer", "Order" })
        {
            var failure = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Entity == entity);

            Assert.Contains("generating the entity " + entity, failure.Reason, StringComparison.Ordinal);
            Assert.Contains("NotSupportedException", failure.Reason, StringComparison.Ordinal);
            Assert.Contains("defect of the tool, not of the input", failure.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheExceptionStaysOnTheBuilderForTheLog()
    {
        var builder = Built();

        Assert.Equal(2, builder.Defects.Count);
        Assert.All(builder.Defects, defect => Assert.IsType<NotSupportedException>(defect.Exception));
        Assert.Equal(["Customer", "Order"], builder.Defects.Select(d => d.Entity).Order());
    }
}
