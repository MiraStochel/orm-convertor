using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Model;
using Model.AbstractRepresentation;

namespace CSharpEntityParsing;

/// <summary>
/// Reads a C# entity class into the entity IR: namespace, class header and the language
/// facts of every property. Nothing here belongs to any ORM — the class structure means
/// the same thing under every framework, the same way the shared LINQ reading owns what
/// is a property of <c>System.Linq</c> (decision 026). What a framework reads on top of
/// the structure — annotations, conventions — is what a subclass adds through the hooks.
/// </summary>
public abstract class CSharpEntityParser(AbstractEntityBuilder entityBuilder) : IEntityParser
{
    protected readonly AbstractEntityBuilder entityBuilder = entityBuilder;

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType)
    {
        return contentType == ConversionContentType.CSharpEntity;
    }

    /// <summary>
    /// Parses the C# classes of one source into entities. Every class declaration in the
    /// text becomes an entity of its own - multi-class input is a shipped form of F14 -
    /// and a nested class becomes a peer entity beside its container, not a member of it.
    /// </summary>
    /// <param name="source">C# source code with one or more classes, optionally wrapped in a namespace.</param>
    /// <returns>The entity maps the unit declared; empty when it held no class (decision 066).</returns>
    public IReadOnlyCollection<EntityMap> Parse(string source)
    {
        // Before Roslyn, and not because Roslyn is careless: it is the only one of the five
        // grammars with a guard of its own, but that guard reports a diagnostic at 4096 levels
        // and the process still dies below 6000 (decision 092). Its lexer is a loop, so the
        // scan below is safe at any depth; building the tree is what is not.
        if (NestingDepthGuard.FirstBeyond(Tracked(source), Limits) is { } tooDeep)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Failure,
                Framework = entityBuilder.Descriptor.Framework,
                Artifact = ConversionContentType.CSharpEntity,
                Reason = NestingDepthGuard.Reason(tooDeep, Limits),
            });

            return [];
        }

        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        var classes = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .ToList();

        var read = new List<EntityMap>();

        foreach (var cls in classes)
        {
            entityBuilder.BeginEntity();

            var ns = GetNamespace(cls);
            if (!string.IsNullOrEmpty(ns))
            {
                entityBuilder.AddNamespace(ns);
            }

            ParseClassAttributes(cls);
            ParseClassHeader(cls);
            ParseProperties(cls);
            read.Add(entityBuilder.EntityMap);
        }

        return read;
    }

    /// <summary>
    /// The C# text as the shared nesting guard reads it (decision 092): Roslyn's own lexer,
    /// which is a loop, projected onto text and position. Each reading layer writes this for
    /// its own lexer rather than sharing one - a token type is exactly what the five languages
    /// do not have in common, and the number they are measured against is shared instead.
    /// Counting brackets in the raw text would be cheaper and wrong: a parenthesis inside a
    /// string literal is not nesting, and a Dapper unit is C# full of SQL literals.
    /// </summary>
    private static IEnumerable<SourceToken> Tracked(string source)
    {
        var text = SourceText.From(source);

        foreach (var token in SyntaxFactory.ParseTokens(source))
        {
            var position = text.Lines.GetLinePosition(token.SpanStart);
            yield return new SourceToken(token.Text, position.Line + 1, position.Character + 1);
        }
    }

    /// <summary>
    /// Class-level annotations of the source framework. The structure carries none, so the
    /// base reads none; a framework that maps through annotations overrides this.
    /// </summary>
    protected virtual void ParseClassAttributes(ClassDeclarationSyntax classDeclaration)
    {
    }

    /// <summary>
    /// The properties of the class. The default emits the language facts of each property
    /// and nothing else; a framework that reads annotations or conventions around them
    /// overrides the loop and takes the language facts from <see cref="ReadProperty"/>.
    /// </summary>
    protected virtual void ParseProperties(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var prop in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            EmitProperty(ReadProperty(prop));
        }
    }

    /// <summary>
    /// The language facts of one property declaration — the reading every framework shares.
    /// </summary>
    protected static PropertyReading ReadProperty(PropertyDeclarationSyntax prop)
    {
        var name = prop.Identifier.Text;
        var accessTokens = prop.Modifiers
            .Where(m =>
                m.IsKind(SyntaxKind.PublicKeyword) ||
                m.IsKind(SyntaxKind.PrivateKeyword) ||
                m.IsKind(SyntaxKind.InternalKeyword) ||
                m.IsKind(SyntaxKind.ProtectedKeyword))
            .Select(t => t.Text)
            .ToList();

        var otherModifiers = prop.Modifiers
                    .Where(m => !accessTokens.Contains(m.Text))
                    .Select(m => m.Text)
                    .ToList();

        bool hasGetter = prop.ExpressionBody != null
                || prop.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) == true;

        bool hasSetter = prop.AccessorList?.Accessors
                    .Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) == true;

        bool isNullable = prop.Type is NullableTypeSyntax;
        string type = ((prop.Type as NullableTypeSyntax)?.ElementType ?? prop.Type).ToString();

        return new PropertyReading(
            type,
            name,
            string.Join(" ", accessTokens),
            otherModifiers,
            hasGetter,
            hasSetter,
            prop.Initializer?.Value?.ToString(),
            isNullable);
    }

    /// <summary>
    /// Modifiers the source framework forces onto its mapped members, which the parser
    /// drops on reading (decision 076): the modifier is the framework's requirement, not a
    /// fact about the domain, so it must not travel into the model - and the parser knows
    /// only its own descriptor, which is what keeps the target free of knowing every
    /// source. NHibernate's virtual is the one case today; the base defers none.
    /// </summary>
    protected virtual IReadOnlyCollection<string> DeferredModifiers => [];

    /// <summary>
    /// Writes the language facts of one property into the builder, without the modifiers
    /// the source framework enforces.
    /// </summary>
    protected void EmitProperty(PropertyReading reading)
    {
        entityBuilder.AddProperty(
            reading.Type,
            reading.Name,
            reading.AccessModifiers,
            [.. reading.OtherModifiers.Where(m => !DeferredModifiers.Contains(m, StringComparer.Ordinal))],
            hasGetter: reading.HasGetter,
            hasSetter: reading.HasSetter,
            defaultValue: reading.DefaultValue,
            isNullable: reading.IsNullable);
    }

    private static string? GetNamespace(ClassDeclarationSyntax classDeclaration)
    {
        var namespaces = classDeclaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(ns => ns.Name.ToString())
            .Reverse()
            .ToList();

        return namespaces.Count == 0 ? null : string.Join(".", namespaces);
    }

    /// <summary>
    /// The class header. Only the access tokens are handed over, the same filtering
    /// <see cref="ReadProperty"/> does: the builder reads one access modifier, so a joined
    /// "public sealed" would match none of its arms and the class would be recorded as
    /// internal. A class with no access token stays internal, which is what C# means by it.
    /// The remaining modifiers have no home in the model and are not carried.
    /// </summary>
    private void ParseClassHeader(ClassDeclarationSyntax classDeclaration)
    {
        var accessModifiers = string.Join(" ", classDeclaration.Modifiers
            .Where(m =>
                m.IsKind(SyntaxKind.PublicKeyword) ||
                m.IsKind(SyntaxKind.PrivateKeyword) ||
                m.IsKind(SyntaxKind.InternalKeyword) ||
                m.IsKind(SyntaxKind.ProtectedKeyword))
            .Select(m => m.Text));

        entityBuilder.AddClassHeader(
            accessModifiers,
            classDeclaration.Identifier.Text
        );
    }
}
