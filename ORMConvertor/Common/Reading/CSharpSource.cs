using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Common.Reading;

/// <summary>
/// What became of one attempt to read a C# unit (decision 128): the tree it was read into, or
/// the one sentence saying where the reading stopped - never both. <see cref="Wrapper"/> is the
/// wrapper the tree was read in, or null for a text read as it stands; in a wrapped tree the
/// class at the top is the wrapper's, and its head stands at <see cref="Insertion"/> - after the
/// using directives the fragment opens with, which are the file's and stay in front of it.
/// </summary>
public readonly record struct CSharpReadResult(SyntaxTree? Tree, FragmentWrapper? Wrapper, string? Reason, int Insertion = 0)
{
    /// <summary>
    /// A position of the tree as a position of the text the client sent: past the wrapper's
    /// head it moves back by the head's length, and inside the head it stands where the head
    /// was inserted.
    /// </summary>
    public int ToClient(int position)
        => Wrapper is null || position < Insertion ? position : Math.Max(Insertion, position - Wrapper.Before.Length);
}

/// <summary>
/// The C# reading of decision 128, shared by the three places that parse a C# unit: the entity
/// pass, and the query passes of LINQ and of Dapper.
///
/// It exists because Roslyn recovers. Given a text with an error, it builds the tree it guesses
/// the author meant - a missing token written in, a stray one skipped - and reports what it did
/// as a diagnostic nobody used to ask for. A class with a missing semicolon was translated
/// without a word, and a misspelt modifier came out as a property without one. What Roslyn
/// wrote in is not what the client wrote, so nothing is translated from it: a unit whose text
/// does not read without an error is refused with the line and the column of the first error,
/// in the shape SQL, Java, HQL, JPQL and XML already refuse with (decision 093).
///
/// The diagnostics are those of parsing alone - no compilation, no references, no semantics -
/// so the translation path still compiles nothing foreign (S4). The sentence lives here and
/// nowhere else, next to <see cref="CSharpUnit"/>, for the reason <c>XmlSource</c> exists:
/// three reading sites that each wrote their own would drift apart.
/// </summary>
public static class CSharpSource
{
    /// <summary>
    /// The tree of the unit, or the reason it could not be read. A text is read in one of two
    /// shapes (decision 111) - as it stands, or inside the wrapper as the members of a class -,
    /// and the shape its top level suggests is tried first: a file as it stands, a fragment
    /// wrapped. Where that reading has an error and the other one has none, the text is read in
    /// the other one: a method beside the class of its rows, which is what the EF Core escape
    /// path writes, is no error inside a class, and statements alone are none as they stand. A
    /// text that reads in neither is refused, at the first error of the shape tried first.
    ///
    /// Two things more are the reader's rule rather than Roslyn's guess. The using directives a
    /// fragment opens with belong to the file, so they stay in front of the wrapper instead of
    /// inside its class, where no using may stand. And the text is parsed at the preview language
    /// version: the tool reads syntax and compiles nothing, so syntax newer than Roslyn's default
    /// is no reason to refuse a unit its project compiles.
    /// </summary>
    /// <param name="wrapper">
    /// What a fragment is parsed inside; a bare class where the reader asks for nothing more.
    /// The tree the reader gets depends on it and the errors do not: what a wrapper writes
    /// before its class - the LINQ reading's usings - does not change how the class body parses.
    /// </param>
    public static CSharpReadResult Read(string source, FragmentWrapper? wrapper = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var around = wrapper ?? FragmentWrapper.Class;
        var asItStands = CSharpSyntaxTree.ParseText(source, Options);
        var root = asItStands.GetCompilationUnitRoot();
        var file = CSharpUnit.IsFile(root);
        var insertion = root.Usings.LastOrDefault()?.FullSpan.End ?? root.Externs.LastOrDefault()?.FullSpan.End ?? 0;

        var first = file ? AsItStands() : Wrapped();

        if (FirstError(first.Tree!) is not { } error)
        {
            return first;
        }

        var other = file ? Wrapped() : AsItStands();

        return FirstError(other.Tree!) is null
            ? other
            : new CSharpReadResult(null, null, Reason(error, source, first));

        CSharpReadResult AsItStands() => new(asItStands, null, null);

        CSharpReadResult Wrapped() => new(
            CSharpSyntaxTree.ParseText(source[..insertion] + around.Wrap(source[insertion..]), Options),
            around,
            null,
            insertion);
    }

    private static readonly CSharpParseOptions Options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    /// <summary>
    /// The first error of the tree by position, as Java, HQL and JPQL report theirs: one unit,
    /// one sentence, and the errors after it are mostly what the first one left behind.
    /// Warnings refute nothing about the text and are not asked.
    /// </summary>
    private static Diagnostic? FirstError(SyntaxTree tree)
        => tree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .FirstOrDefault();

    /// <summary>
    /// The one sentence a C# unit is refused with, in the shape of the Java one, so that the
    /// line and the column are read off it the same way. The message is Roslyn's, word for word
    /// and in the invariant culture - the platform's text, as <c>XmlSource</c> carries the text
    /// of an <c>XmlException</c> -, and our position stands beside it as the half a machine reads.
    /// </summary>
    private static string Reason(Diagnostic error, string source, CSharpReadResult reading)
    {
        // The position is taken back to the text the client sent: the wrapper's head is
        // subtracted, and a position in the wrapper itself - a closing brace the fragment lacks
        // is missed where the wrapper ends - is put after the last thing the client wrote, so
        // that the record never names a line the client did not send. A file is its own text,
        // and only trailing blank lines are left out of it.
        var at = Math.Clamp(reading.ToClient(error.Location.SourceSpan.Start), 0, source.TrimEnd().Length);
        var position = SourceText.From(source).Lines.GetLinePosition(at);
        var message = error.GetMessage(CultureInfo.InvariantCulture);

        return $"The C# source could not be read at line {position.Line + 1}, column {position.Character + 1}: "
            + (message.EndsWith('.') ? message : message + ".");
    }
}
