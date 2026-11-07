using System.Reflection;
using Common.Compilation;
using Tests.Verification;

namespace Tests.Foreign;

/// <summary>
/// Two rules of the path over a foreign artifact that decision 207 states: the assembly name of
/// an NHibernate mapping is the consumer's, whatever the document carried, and a parameter of an
/// entity type has no sample value, so the query is not run.
/// </summary>
public class ForeignPathRulesTest
{
    [Fact]
    public void TheConsumerSetsTheAssemblyNameWhateverTheMappingNamed()
    {
        const string named = """<hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Shop" assembly="Shop"><class name="Product" table="Products" /></hibernate-mapping>""";
        const string bare = """<hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Shop"><class name="Product" table="Products" /></hibernate-mapping>""";

        Assert.True(NHibernateAcceptance.NamesAnAssembly(named));
        Assert.False(NHibernateAcceptance.NamesAnAssembly(bare));
        Assert.False(NHibernateAcceptance.NamesAnAssembly("<not xml"));

        var qualified = NHibernateAcceptance.QualifyAssembly(named, "Foreign_x_1");
        Assert.Contains("assembly=\"Foreign_x_1\"", qualified);
        Assert.DoesNotContain("assembly=\"Shop\"", qualified);
        Assert.Contains("assembly=\"Foreign_x_1\"", NHibernateAcceptance.QualifyAssembly(bare, "Foreign_x_1"));
    }

    [Fact]
    public void AParameterOfAnEntityTypeOfTheArtifactHasNoSampleValue()
    {
        var result = CSharpSourceCompiler.Compile("ForeignPathRulesProbe",
            [
                """
                namespace Shop;

                public class Probe { public int Id { get; set; } }

                public static class Queries
                {
                    public static int Scalar(int a, string b, System.DateTime? c, int[] d, System.Collections.Generic.List<string> e) => 0;
                    public static int Entity(int a, Probe p) => 0;
                    public static int Entities(System.Collections.Generic.List<Probe> ps) => 0;
                    public static int Nullable(Probe? p) => 0;
                }
                """,
            ],
            ConsumerReferences.Dapper);
        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.ToString())));

        var queries = Assembly.Load(result.Assembly!).GetType("Shop.Queries")!;
        IReadOnlyList<ParameterInfo> Of(string method) => queries.GetMethod(method)!.GetParameters();

        Assert.Null(ForeignArtifactRun.EntityParameter(Of("Scalar")));
        Assert.Equal("p: Probe", ForeignArtifactRun.EntityParameter(Of("Entity")));
        Assert.Equal("ps: Probe", ForeignArtifactRun.EntityParameter(Of("Entities")));
        Assert.Equal("p: Probe", ForeignArtifactRun.EntityParameter(Of("Nullable")));
    }
}
