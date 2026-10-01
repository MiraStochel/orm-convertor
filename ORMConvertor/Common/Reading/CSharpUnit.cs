using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Common.Reading;

/// <summary>
/// How the text of a C# unit is parsed by a reader that looks for code rather than for
/// classes (decision 111). A unit is a whole file - a namespace, types, top-level statements
/// beside them - or a fragment of one: a bare method or statements, the shape the query
/// builders of the tool write and its samples carry. A file is parsed as it stands, so that a
/// namespace and its classes stay what they are; a fragment is parsed inside the wrapper the
/// reader gives it, because a method needs a class around it to be one. Which of the two it
/// is decides only how the text is parsed, never what its classes are.
/// </summary>
public static class CSharpUnit
{
    /// <summary>
    /// The tree of the unit: the text itself where it is a file, the wrapped text where it
    /// is a fragment.
    /// </summary>
    public static SyntaxTree Parse(string source, Func<string, string> wrapFragment)
    {
        var tree = CSharpSyntaxTree.ParseText(source);

        return IsFile(tree.GetCompilationUnitRoot())
            ? tree
            : CSharpSyntaxTree.ParseText(wrapFragment(source));
    }

    /// <summary>
    /// Whether the text declares a namespace or a type at its top level - a file - rather
    /// than members or statements alone.
    /// </summary>
    public static bool IsFile(CompilationUnitSyntax root)
        => root.Members.Any(member => member is BaseNamespaceDeclarationSyntax or BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
}
