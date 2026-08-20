using AbstractWrappers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;

namespace CSharpEntityParsing;

/// <summary>
/// Reads a C# entity class into the entity IR: namespace, class header and the language
/// facts of every property. Nothing here belongs to any ORM — the class structure means
/// the same thing under every framework, the same way the shared LINQ reading owns what
/// is a property of <c>System.Linq</c> (decision 026). What a framework reads on top of
/// the structure — annotations, conventions — is what a subclass adds through the hooks.
/// </summary>
public abstract class CSharpEntityParser(AbstractEntityBuilder entityBuilder) : IParser
{
    protected readonly AbstractEntityBuilder entityBuilder = entityBuilder;

    public bool CanParse(ConversionContentType contentType)
    {
        return contentType == ConversionContentType.CSharpEntity;
    }

    /// <summary>
    /// Parses a C# class definition (optionally within a namespace) from the provided source code string.
    /// </summary>
    /// <param name="source">C# source code containing a single class, optionally wrapped in a namespace.</param>
    public void Parse(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        var classes = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .ToList();

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
    /// Writes the language facts of one property into the builder.
    /// </summary>
    protected void EmitProperty(PropertyReading reading)
    {
        entityBuilder.AddProperty(
            reading.Type,
            reading.Name,
            reading.AccessModifiers,
            reading.OtherModifiers,
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

    private void ParseClassHeader(ClassDeclarationSyntax classDeclaration)
    {
        var modifiers = string.Join(" ", classDeclaration.Modifiers.Select(m => m.Text));

        entityBuilder.AddClassHeader(
            modifiers,
            classDeclaration.Identifier.Text
        );
    }
}
