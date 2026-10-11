using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Common.Reading;

/// <summary>
/// What the text of a C# unit is to a reader (decision 111). A unit is a whole file - a
/// namespace, types, top-level statements beside them - or a fragment of one: a bare method or
/// statements, the shape the query builders of the tool write and its samples carry. A file is
/// parsed as it stands, so that a namespace and its classes stay what they are; a fragment is
/// parsed inside the wrapper the reader gives it, because a method needs a class around it to
/// be one. Which of the two it is decides only how the text is parsed, never what its classes
/// are. The parsing itself is <see cref="CSharpSource.Read"/>, which takes the other shape where
/// only that one reads without an error, and says whether the text read at all (decision 128).
/// </summary>
public static class CSharpUnit
{
    /// <summary>
    /// Whether the text declares a namespace or a type at its top level - a file - rather
    /// than members or statements alone: the shape the text is read in first (decision 128).
    /// </summary>
    public static bool IsFile(CompilationUnitSyntax root)
        => root.Members.Any(member => member is BaseNamespaceDeclarationSyntax or BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
}

/// <summary>
/// The text a fragment is parsed inside (decision 111): what the reader writes before it and
/// after it so that a bare method is a member of a class. Kept as two halves rather than as a
/// function over the fragment, because a position Roslyn reports in the wrapped text has to be
/// taken back to the text the client sent (decision 128), and only the length of the first
/// half says how far.
/// </summary>
public sealed record FragmentWrapper(string Before, string After)
{
    /// <summary>A class around the fragment and nothing else: all a method needs to be one.</summary>
    public static FragmentWrapper Class { get; } = new("public class Snippet\n{\n", "\n}\n");

    public string Wrap(string fragment) => Before + fragment + After;
}
