using NHibernate.Engine;
using NHibernate.Id;
using Tests.Verification;

namespace Tests.NHibernate;

/// <summary>
/// What NHibernate 5.7.0 resolves a generator to on the dialect the builder targets,
/// <c>MsSql2012Dialect</c>, read off the session factory built over a mapped model without a
/// connection (decision 187). <c>native</c> leaves the mechanism to the dialect, and this
/// dialect answers with its identity column, though it supports sequences as well: the
/// mechanism the vocabulary's <c>Auto</c> names on SQL Server, the one the NHibernate builder
/// writes <c>native</c> for. <c>identity</c> is the same generator stated outright and
/// <c>sequence</c> the one a sequence needs, so the three stay three.
/// </summary>
public class NHibernateGeneratorProviderTest
{
    private const string Entities = """
        namespace Probe;

        public class Widget
        {
            public virtual int WidgetId { get; set; }
        }
        """;

    private static string Mapping(string generator) => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Probe">
          <class name="Widget" table="Widgets">
            <id name="WidgetId" column="WidgetId" type="Int32">{generator}</id>
          </class>
        </hibernate-mapping>
        """;

    public static TheoryData<string, string, Type> Generators => new()
    {
        { "native", """<generator class="native" />""", typeof(IdentityGenerator) },
        { "identity", """<generator class="identity" />""", typeof(IdentityGenerator) },
        { "sequence", """<generator class="sequence"><param name="sequence">Widgets_Seq</param></generator>""", typeof(SequenceGenerator) },
    };

    [Theory]
    [MemberData(nameof(Generators))]
    public void TheDialectResolvesTheGenerator(string name, string generator, Type resolved)
    {
        var compiled = GeneratedEntityCompiler.CompileOrFail(
            "NHibernateGenerator_" + name.Replace('-', '_'),
            [Entities],
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        NHibernateAcceptance.UseSessionFactory(compiled, [Mapping(generator)], (factory, _) =>
        {
            var actual = ((ISessionFactoryImplementor)factory).GetIdentifierGenerator("Probe.Widget");

            Assert.Equal(resolved, actual.GetType());
        });
    }
}
