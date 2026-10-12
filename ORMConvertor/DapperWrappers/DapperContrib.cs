using AbstractWrappers.Descriptors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DapperWrappers;

/// <summary>
/// What the Dapper wrapper knows of Dapper.Contrib, an add-on package over Dapper that the
/// tool does not read as Dapper (decision 133): its attributes and its methods, so that a unit
/// using it gets records naming the package rather than a sentence about Dapper alone, or no
/// record at all. Nothing of the package is read - not even its conventions, which apply only
/// in a project that uses it, and that is nothing the unit states.
///
/// The package is told by the unit's syntax alone, since the translation path compiles
/// nothing (S4): a using directive of its namespace in the unit, or a name qualified with it.
/// Two of its attribute names belong to DataAnnotations as well, which EF Core reads, so
/// those two are told only where the unit does not import their namesake too.
/// </summary>
internal static class DapperContrib
{
    private const string Namespace = "Dapper.Contrib.Extensions";

    /// <summary>The attributes of the package, each with what the record says of its statement and the category of the fact.</summary>
    private static readonly Dictionary<string, (string Dropped, MappingFactCategory? Category)> Attributes = new(StringComparer.Ordinal)
    {
        ["Table"] = ("the name of the table it states is dropped and left to the catalog", MappingFactCategory.TableName),
        ["Key"] = ("the key it states, one the database generates, is dropped and left to the catalog", MappingFactCategory.PrimaryKey),
        ["ExplicitKey"] = ("the key it states, one the application assigns, is dropped and left to the catalog", MappingFactCategory.PrimaryKey),
        ["Write"] = ("what it states about Dapper.Contrib writing the property is dropped; it says nothing about the rows a query reads", null),
        ["Computed"] = ("that Dapper.Contrib leaves the property out of the rows it writes is dropped; it says nothing about the rows a query reads", null),
    };

    /// <summary>The namespace whose attribute of the same name the package's would be, for the two names it shares with DataAnnotations.</summary>
    private static readonly Dictionary<string, string> Namesakes = new(StringComparer.Ordinal)
    {
        ["Table"] = "System.ComponentModel.DataAnnotations.Schema",
        ["Key"] = "System.ComponentModel.DataAnnotations",
    };

    /// <summary>
    /// The methods the package adds to a connection, each with whether it writes. The SQL of
    /// every one of them is composed by the package at run time, from the type and its
    /// attributes, and sent through Dapper's own methods.
    /// </summary>
    private static readonly Dictionary<string, bool> Methods = new(StringComparer.Ordinal)
    {
        ["Get"] = false, ["GetAsync"] = false,
        ["GetAll"] = false, ["GetAllAsync"] = false,
        ["Insert"] = true, ["InsertAsync"] = true,
        ["Update"] = true, ["UpdateAsync"] = true,
        ["Delete"] = true, ["DeleteAsync"] = true,
        ["DeleteAll"] = true, ["DeleteAllAsync"] = true,
    };

    /// <summary>The methods whose type argument nothing in the call can infer, so that a call of the package spells it.</summary>
    private static readonly HashSet<string> SpelledType = new(StringComparer.Ordinal)
    {
        "Get", "GetAsync", "GetAll", "GetAllAsync", "DeleteAll", "DeleteAllAsync",
    };

    /// <summary>
    /// The clause of the record of an attribute of the package and the category of the fact it
    /// states, or null where the attribute is not told as the package's: a name the package
    /// does not have, or one it shares with DataAnnotations in a unit that imports that too.
    /// </summary>
    internal static (string Why, MappingFactCategory? Category)? Attribute(AttributeSyntax attribute)
    {
        var (qualifier, name) = Split(attribute.Name);

        if (name.Length > "Attribute".Length && name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            name = name[..^"Attribute".Length];
        }

        if (!Attributes.TryGetValue(name, out var fact))
        {
            return null;
        }

        var told = qualifier is null
            ? Imports(attribute, Namespace) && !(Namesakes.TryGetValue(name, out var namesake) && Imports(attribute, namesake))
            : qualifier == Namespace;

        return told
            ? ($"it is an attribute of Dapper.Contrib, an add-on package over Dapper that the tool does not read as Dapper "
                + $"(decision 133), so {fact.Dropped}", fact.Category)
            : null;
    }

    /// <summary>
    /// Whether an invocation is a call of the package: one of its methods called on a member
    /// access, in a unit that imports its namespace - the extension methods are not in scope
    /// otherwise. A method whose type argument cannot be inferred spells exactly one; one that
    /// takes the entity takes an argument. Neither rule tells a call on a connection from a
    /// call of the same name on a list, which no syntax does; the record names the call, so a
    /// mistake is in plain sight.
    /// </summary>
    internal static bool IsCall(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax member
            && Methods.ContainsKey(member.Name.Identifier.Text)
            && (SpelledType.Contains(member.Name.Identifier.Text)
                ? member.Name is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 }
                : invocation.ArgumentList.Arguments.Count > 0
                    && member.Name is IdentifierNameSyntax or GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 })
            && Imports(invocation, Namespace);

    /// <summary>
    /// The reason of the refusal of a call of the package (decision 133): a handover of a query
    /// whose text is nowhere in the unit, since the package composes it at run time, or of a
    /// write, which the tool does not translate either way.
    /// </summary>
    internal static string Refusal(InvocationExpressionSyntax invocation)
    {
        var member = (MemberAccessExpressionSyntax)invocation.Expression;
        var call = member.Name.ToString();
        var head = $"The call '{call}' is a method of Dapper.Contrib, an add-on package over Dapper that the tool does not "
            + "read as Dapper (decision 133)";

        return Methods[member.Name.Identifier.Text]
            ? head + ", and it writes; the tool translates queries that read, so no artifact was generated."
            : head + ": the package composes the SQL of the call at run time from the type and its attributes, so the unit "
                + "holds no text to translate, and no artifact was generated.";
    }

    /// <summary>
    /// The qualifier of a name and its last identifier; no qualifier for a simple name. An
    /// alias ahead of a simple name - global::Table - is the qualifier, and names no namespace
    /// of the package.
    /// </summary>
    private static (string? Qualifier, string Name) Split(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => (Plain(qualified.Left), qualified.Right.Identifier.Text),
        AliasQualifiedNameSyntax aliased => (aliased.Alias.Identifier.Text + "::", aliased.Name.Identifier.Text),
        SimpleNameSyntax simple => (null, simple.Identifier.Text),
        _ => (null, name.ToString()),
    };

    /// <summary>A name as text, without whitespace and without global:: ahead of it, which names the same namespace.</summary>
    private static string Plain(NameSyntax name)
    {
        var text = string.Concat(name.ToString().Where(c => !char.IsWhiteSpace(c)));
        return text.StartsWith("global::", StringComparison.Ordinal) ? text["global::".Length..] : text;
    }

    /// <summary>
    /// Whether a plain using directive in scope of the node imports the namespace: one of the
    /// unit itself, or of a namespace around the node. An alias or a static using imports no
    /// namespace, and a global using stands in another file as a rule, where the unit cannot
    /// see it.
    /// </summary>
    private static bool Imports(SyntaxNode node, string ns)
    {
        var usings = node.SyntaxTree.GetCompilationUnitRoot().Usings
            .Concat(node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(declaration => declaration.Usings));

        return usings.Any(directive => directive.Alias is null
            && directive.StaticKeyword.IsKind(SyntaxKind.None)
            && directive.Name is { } name
            && Plain(name) == ns);
    }
}
