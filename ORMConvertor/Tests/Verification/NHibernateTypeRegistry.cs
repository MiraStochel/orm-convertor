using System.Collections;
using System.Reflection;
using NHibernate.Type;

namespace Tests.Verification;

/// <summary>
/// NHibernate's <see cref="TypeFactory"/> registers a CLR type it meets as a value - the
/// element type a LINQ <c>Count()</c> ranges over, say - once per process, under the type's
/// full name as much as under its assembly-qualified one, and throws on a second registration
/// of the name. A consumer project has each entity type once; this suite compiles the same
/// entity sources into an assembly per query, source and target, so the LINQ form of a second
/// NHibernate query over <c>Shop.ShopOrderLine</c> would meet the registration the first one
/// made (decision 118). The registry is told to forget the types of a generated assembly
/// before the provider translates a chain over them - a fact of the test process, not of the
/// artifact, kept here so that no test has to know it.
/// </summary>
internal static class NHibernateTypeRegistry
{
    private static readonly Lazy<IDictionary> Registry = new(() =>
    {
        var field = typeof(TypeFactory).GetField("typeByTypeOfName", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "NHibernate.Type.TypeFactory no longer keeps its registry in the static field 'typeByTypeOfName'; the suite's reset of it needs to follow the pinned version.");

        return (IDictionary)field.GetValue(null)!;
    });

    /// <summary>Forgets every type the generated assembly declares, under both names the registry may hold it by.</summary>
    public static void Forget(Assembly generated)
    {
        var registry = Registry.Value;

        foreach (var type in generated.GetTypes())
        {
            lock (registry)
            {
                registry.Remove(type.FullName!);
                if (type.AssemblyQualifiedName is { } qualified)
                {
                    registry.Remove(qualified);
                }
            }
        }
    }
}
