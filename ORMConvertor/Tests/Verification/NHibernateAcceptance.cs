using System.Reflection;
using System.Xml.Linq;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Driver;

namespace Tests.Verification;

/// <summary>
/// Third verification level of decision 016: NHibernate itself accepts the generated
/// mapping together with the generated class. Building the session factory over a
/// configuration with nothing but a dialect makes NHibernate bind every mapping to the
/// persistent class before any connection is attempted - a mapping naming a property the
/// class does not have, a composite-id class without Equals/GetHashCode, a key part
/// without a type all fail here, which is the class of errors decision 006 named and no
/// shape assertion can catch.
/// </summary>
internal static class NHibernateAcceptance
{
    /// <summary>
    /// Serializes every acceptance that installs an <c>AssemblyResolve</c> handler. The
    /// event is process-global and matched by assembly name alone, so two handlers live at
    /// once would each be offered the other's resolutions; with the test collections running
    /// in parallel by default (xunit.runner.json states no otherwise), nothing but this keeps
    /// two of them apart. Held by the HQL acceptance as well, which installs the same handler.
    /// A convention that every test compiles under its own assembly name would be a rule held
    /// by hand - and a rule held by hand is exactly the scheduling-dependent outcome S2 rules
    /// out.
    /// </summary>
    internal static readonly Lock AssemblyResolveGate = new();

    /// <summary>
    /// Builds (and disposes) a session factory from the compiled generated entities and the
    /// generated mapping documents. Throws whatever NHibernate throws when it refuses them.
    /// </summary>
    public static void BuildSessionFactory(byte[] compiledEntities, IEnumerable<string> mappingXmls)
    {
        var entities = Assembly.Load(compiledEntities);
        var assemblyName = entities.GetName().Name!;

        // NHibernate resolves class names through Assembly.Load, which cannot see an
        // assembly loaded from a byte image; the handler hands it ours and nothing else.
        // The event is process-global and matched by name alone, so two handlers live at
        // once could answer each other's resolutions with the wrong assembly - the gate
        // below, not a naming convention, is what keeps only one of them installed.
        ResolveEventHandler resolveGeneratedEntities = (_, args) =>
            new AssemblyName(args.Name).Name == assemblyName ? entities : null;

        using var gate = AssemblyResolveGate.EnterScope();

        AppDomain.CurrentDomain.AssemblyResolve += resolveGeneratedEntities;
        try
        {
            var configuration = new Configuration();
            configuration.SetProperty(global::NHibernate.Cfg.Environment.Dialect, typeof(MsSql2012Dialect).AssemblyQualifiedName);

            // The dialect's default driver reflects over System.Data.SqlClient, which this
            // solution deliberately does not carry; the driver of Microsoft.Data.SqlClient
            // is stated instead. No connection is configured - none is attempted.
            configuration.SetProperty(global::NHibernate.Cfg.Environment.ConnectionDriver, typeof(MicrosoftDataSqlClientDriver).AssemblyQualifiedName);

            // By default the factory build would open a connection just to read the
            // dialect's reserved words; that is the only step of it needing a database,
            // so it is switched off rather than supplied with one.
            configuration.SetProperty(global::NHibernate.Cfg.Environment.Hbm2ddlKeyWords, "none");

            foreach (var mappingXml in mappingXmls)
            {
                configuration.AddXmlString(QualifyAssembly(mappingXml, assemblyName));
            }

            using var sessionFactory = configuration.BuildSessionFactory();
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolveGeneratedEntities;
        }
    }

    /// <summary>
    /// Which assembly the entity ends up in is a fact of the consumer project, not of the
    /// conversion, so the artifact cannot carry it and the consumer supplies it - in a real
    /// project by the assembly attribute matching the project name, here by naming the
    /// assembly the sources were compiled into.
    ///
    /// This used to add the attribute only when the mapping did not already qualify the
    /// class itself, which sounded like deference and was in fact a workaround: the builder
    /// invented an assembly name from the namespace, and the verification tests confirmed
    /// the invention by compiling under exactly that name. Since decision 028 the builder
    /// qualifies nothing, so this always applies and the level is judging the artifact
    /// rather than itself.
    /// </summary>
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
