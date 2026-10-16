using System.Xml.Linq;
using JavaEntityParsing;

namespace JakartaPersistence;

/// <summary>
/// One hint a named query declares: <c>@QueryHint(name = …, value = …)</c> or
/// <c>&lt;hint name="…" value="…"/&gt;</c>. The key is the text where the source wrote a string,
/// the constant the last name where it wrote one - <c>QueryHints.JDBC_MAX_ROWS</c> -, so that
/// the implementation's vocabulary answers either spelling as it answers a setHint call.
/// </summary>
public sealed record JpaQueryHintFacts(string Key, string Constant, string? Value, string Spelled);

/// <summary>
/// What one definition of a named query states (decision 143), in whichever of its two
/// forms the source wrote it: the annotation on a class or the element of an orm.xml. The
/// two readers below fill this one shape and the JPQL parser reads it once, so that a hint
/// or a lock mode cannot be answered one way for the annotation and another for the element.
/// </summary>
/// <param name="Form">How the source spelled the definition, for the records: <c>@NamedQuery</c>, <c>&lt;named-native-query&gt;</c>.</param>
/// <param name="Name">The name the source gave the query; null where it gave none, which every form requires.</param>
/// <param name="Text">The query text; null where the definition states none.</param>
/// <param name="Native">True for the native form, whose text is SQL rather than JPQL.</param>
/// <param name="ResultSetMapping">The result set mapping the native form names, or null.</param>
/// <param name="LockMode">The lock mode the definition states, other than the default, or null.</param>
/// <param name="Unread">Children of the element the orm.xml schema does not give a named query, by name.</param>
public sealed record JpaNamedQueryFacts(
    string Form,
    string? Name,
    string? Text,
    bool Native,
    string? ResultSetMapping,
    IReadOnlyList<JpaQueryHintFacts> Hints,
    string? LockMode,
    IReadOnlyList<string> Unread);

/// <summary>
/// Reads the definitions of named queries out of the two places the specification lets them
/// stand (decision 143): the annotations of a Java class - <c>@NamedQuery</c>, its container
/// <c>@NamedQueries</c>, the native pair - and the elements of an orm.xml, on its root or
/// inside an <c>&lt;entity&gt;</c>. It reads the definition's own statements and nothing of
/// the text: the text goes to the JPQL parser or to the shared SQL reader, which is where
/// the query is read.
///
/// Only the specification's annotation is a definition here, written bare or with the
/// <c>jakarta.persistence</c> qualifier - the rule the entity reading applies to decide what
/// is the specification's. A vendor's annotation of the same simple name, qualified by its
/// own package, is the vendor's and stays with the entity pass, which reports it.
/// </summary>
public static class JpaNamedQueryReader
{
    private const string Jakarta = "jakarta.persistence";

    private static readonly string[] Containers = ["NamedQueries", "NamedNativeQueries"];

    private static readonly string[] Definitions = ["NamedQuery", "NamedNativeQuery"];

    /// <summary>Whether the annotation defines a named query or holds definitions of them.</summary>
    public static bool Defines(JavaAnnotation annotation)
        => (annotation.Qualifier is null || annotation.Qualifier == Jakarta)
            && (Containers.Contains(annotation.SimpleName, StringComparer.Ordinal)
                || Definitions.Contains(annotation.SimpleName, StringComparer.Ordinal));

    /// <summary>
    /// The definitions one annotation carries: one for a definition, one per nested
    /// annotation for a container, none for any other annotation. The order is the order
    /// of the text.
    /// </summary>
    public static IEnumerable<JpaNamedQueryFacts> FromAnnotation(JavaAnnotation annotation)
    {
        if (!Defines(annotation))
        {
            yield break;
        }

        if (Containers.Contains(annotation.SimpleName, StringComparer.Ordinal))
        {
            foreach (var nested in (annotation["value"]?.AsList ?? []).Select(v => v.Annotation).OfType<JavaAnnotation>())
            {
                if (Definitions.Contains(nested.SimpleName, StringComparer.Ordinal))
                {
                    yield return Definition(nested);
                }
            }

            yield break;
        }

        yield return Definition(annotation);
    }

    private static JpaNamedQueryFacts Definition(JavaAnnotation annotation)
    {
        var native = annotation.SimpleName == "NamedNativeQuery";
        var hints = new List<JpaQueryHintFacts>();

        foreach (var hint in (annotation["hints"]?.AsList ?? []).Select(v => v.Annotation).OfType<JavaAnnotation>())
        {
            var name = hint["name"];
            hints.Add(new JpaQueryHintFacts(
                Key: name is { Kind: JavaAnnotationValueKind.String } ? name.Text : string.Empty,
                Constant: name is { Kind: JavaAnnotationValueKind.Name } ? name.SimpleName : string.Empty,
                Value: hint.String("value"),
                Spelled: name is null ? "@QueryHint" : name.Kind == JavaAnnotationValueKind.String ? $"\"{name.Text}\"" : name.Text));
        }

        // LockModeType.NONE is what an absent lockMode means, so stating it states nothing.
        var lockMode = annotation["lockMode"]?.SimpleName;

        return new JpaNamedQueryFacts(
            Form: "@" + annotation.SimpleName,
            Name: annotation.String("name"),
            Text: annotation.String("query"),
            Native: native,
            ResultSetMapping: native ? annotation.String("resultSetMapping") : null,
            Hints: hints,
            LockMode: lockMode is null or "NONE" ? null : lockMode,
            Unread: []);
    }

    /// <summary>Whether the element defines a named query, in either form.</summary>
    public static bool Defines(XElement element)
        => element.Name.LocalName is "named-query" or "named-native-query";

    /// <summary>
    /// The definition one element states. The text is its &lt;query&gt; child, the hints its
    /// &lt;hint&gt; children, the lock mode its lock-mode attribute; a &lt;description&gt; says
    /// nothing about the query, and any other child is named among the unread.
    /// </summary>
    public static JpaNamedQueryFacts FromElement(XElement element)
    {
        var native = element.Name.LocalName == "named-native-query";
        var hints = new List<JpaQueryHintFacts>();
        var unread = new List<string>();
        string? text = null;

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "query":
                    text = child.Value;
                    break;

                case "hint":
                    var name = child.Attribute("name")?.Value.Trim() ?? string.Empty;
                    hints.Add(new JpaQueryHintFacts(name, string.Empty, child.Attribute("value")?.Value, $"\"{name}\""));
                    break;

                case "description":
                    break;

                default:
                    unread.Add($"<{child.Name.LocalName}>");
                    break;
            }
        }

        var lockMode = element.Attribute("lock-mode")?.Value.Trim();

        return new JpaNamedQueryFacts(
            Form: $"<{element.Name.LocalName}>",
            Name: element.Attribute("name")?.Value,
            Text: text,
            Native: native,
            ResultSetMapping: native ? element.Attribute("result-set-mapping")?.Value : null,
            Hints: hints,
            LockMode: string.IsNullOrEmpty(lockMode) || lockMode == "NONE" ? null : lockMode,
            Unread: unread);
    }
}
