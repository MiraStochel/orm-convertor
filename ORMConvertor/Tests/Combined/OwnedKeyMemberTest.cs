using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// EF Core fluent configuration that owns the key member itself - <c>OwnsOne(x => x.Id)</c>,
/// an error in EF Core's own terms - used to end the whole request: the exclusion of decision
/// 177 took the property out of the entity, the key part over it stayed, and the Java targets
/// wrote an accessor for a member the entity no longer had ("Sequence contains no matching
/// element", review of 2026-10-30). The key part leaves with the property, and a defect of the
/// tool, should one remain, is a record and not a 400 (decision 197).
/// </summary>
public class OwnedKeyMemberTest
{
    private const string Entity = "public class A { public int Id { get; set; } public string Name { get; set; } }";

    private const string Context = """
        public class C : DbContext
        {
            protected override void OnModelCreating(ModelBuilder b)
            {
                b.Entity<A>().OwnsOne(x => x.Id);
            }
        }
        """;

    public static TheoryData<ORMEnum> Targets() => CrossFrameworkInputs.Frameworks();

    [Theory]
    [MemberData(nameof(Targets))]
    public void OwningTheKeyMemberIsALossOfTheMemberNotAFailureOfTheRun(ORMEnum target)
    {
        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            target,
            [
                new() { Name = "A.cs", ContentType = ConversionContentType.CSharp, Content = Entity },
                new() { Name = "C.cs", ContentType = ConversionContentType.CSharp, Content = Context },
            ],
            catalogConnectionString: null);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Entity == "A" && r.Property == "Id");
        Assert.Empty(result.Defects);
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("defect of the tool", StringComparison.Ordinal));
    }
}
