using AbstractWrappers;
using CSharpEntityParsing;
using Microsoft.CodeAnalysis;

namespace NHibernateWrappers;

/// <summary>
/// Parses an NHibernate entity class from C# source code. The class carries no mapping —
/// that lives in the XML artifact read by <see cref="NHibernateXMLMappingParser"/> — so
/// the shared structural reading is the whole parser, less one word: the virtual the
/// framework forces onto every mapped member (<see cref="NHibernateDescriptor"/>) is
/// NHibernate's requirement, not a fact of the domain, and is deferred on reading
/// (decision 076). The NHibernate builder adds it back to its own output; any other
/// target neither needs nor understands it.
///
/// A class whose own members hand a LINQ query to the session is the code around queries,
/// not an entity (decision 111), and which classes those are is answered by the same search
/// the query pass reads the queries by.
/// </summary>
public class NHibernateEntityParser(AbstractEntityBuilder entityBuilder) : CSharpEntityParser(entityBuilder)
{
    protected override IReadOnlyCollection<string> DeferredModifiers => ["virtual"];

    protected override IEnumerable<SyntaxNode> FindHandovers(string source)
        => new NHibernateLinqQueryParser(() => throw new InvalidOperationException("The search for handovers builds no query."))
        {
            Limits = Limits,
        }.FindHandovers(source);
}
