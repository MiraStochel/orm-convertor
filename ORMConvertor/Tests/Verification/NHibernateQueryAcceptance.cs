using System.Reflection;
using System.Xml.Linq;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Driver;

namespace Tests.Verification;

/// <summary>
/// Third verification level for an HQL query (decision 027): NHibernate compiles the query
/// against the mapped model. Creating a query builds its plan, which parses the HQL and
/// resolves every entity and property name against the mapping — so this is rule Q13 checked
/// by the target framework itself, and a stronger verdict than the EF Core side gives.
///
/// No connection is opened. NHibernate acquires an ADO connection lazily, and nothing here
/// executes the query.
/// </summary>
internal static class NHibernateQueryAcceptance
{
    /// <summary>
    /// Compiles the HQL against the generated mapping. Throws whatever NHibernate throws when
    /// it refuses it — a syntax error and an unmapped property both surface as
    /// <c>QuerySyntaxException</c>.
    /// </summary>
    public static void CompileQuery(byte[] compiledEntities, IEnumerable<string> mappingXmls, string hql)
    {
        var entities = Assembly.Load(compiledEntities);
        var assemblyName = entities.GetName().Name!;

        ResolveEventHandler resolveGeneratedEntities = (_, args) =>
            new AssemblyName(args.Name).Name == assemblyName ? entities : null;

        // The same process-global handler as the entity acceptance, so the same gate.
        using var gate = NHibernateAcceptance.AssemblyResolveGate.EnterScope();

        AppDomain.CurrentDomain.AssemblyResolve += resolveGeneratedEntities;
        try
        {
            var configuration = new Configuration();
            configuration.SetProperty(global::NHibernate.Cfg.Environment.Dialect, typeof(MsSql2012Dialect).AssemblyQualifiedName);
            configuration.SetProperty(global::NHibernate.Cfg.Environment.ConnectionDriver, typeof(MicrosoftDataSqlClientDriver).AssemblyQualifiedName);
            configuration.SetProperty(global::NHibernate.Cfg.Environment.Hbm2ddlKeyWords, "none");

            foreach (var mappingXml in mappingXmls)
            {
                configuration.AddXmlString(QualifyAssembly(mappingXml, assemblyName));
            }

            using var sessionFactory = configuration.BuildSessionFactory();
            using var session = sessionFactory.OpenSession();

            // Creating the query is what compiles it; nothing is executed.
            session.CreateQuery(hql);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolveGeneratedEntities;
        }
    }

    private static string QualifyAssembly(string mappingXml, string assemblyName)
    {
        var document = XDocument.Parse(mappingXml);

        if (document.Root!.Attribute("assembly") is null)
        {
            document.Root.SetAttributeValue("assembly", assemblyName);
        }

        return document.ToString();
    }
}
