using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCoreWrappers;

/// <summary>
/// The EF Core context as the source text shows it (decision 111): a class whose base list
/// names DbContext, or which declares a property of type DbSet&lt;T&gt;. EF Core does not count
/// the context among the entity types, and DbSet is the mark only a context bears - the
/// collection navigation of an entity is an ICollection, a List or a HashSet, never a DbSet.
/// One recognition for both passes: the entity pass does not read the context as an entity,
/// and the query pass reads a chain over the context's own DbSet inside it as a query.
/// </summary>
internal static class EFCoreContext
{
    public static bool IsContext(ClassDeclarationSyntax cls)
        => cls.BaseList?.Types.Any(baseType => LastName(baseType.Type) == "DbContext") == true
           || DbSetProperties(cls).Any();

    /// <summary>The DbSet properties the class declares, in the order of the text.</summary>
    public static IEnumerable<PropertyDeclarationSyntax> DbSetProperties(ClassDeclarationSyntax cls)
        => cls.Members.OfType<PropertyDeclarationSyntax>().Where(property => IsDbSet(property.Type));

    /// <summary>
    /// The context the node stands in, when the class immediately around it is one; null
    /// otherwise. Immediately, because a class nested in the context is a class of its own,
    /// and what its members walk is its own instance.
    /// </summary>
    public static ClassDeclarationSyntax? Around(SyntaxNode node)
        => node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is { } cls && IsContext(cls)
            ? cls
            : null;

    /// <summary>Whether the name is a DbSet property of the context the node stands in.</summary>
    public static bool IsOwnDbSet(SyntaxNode node, string name)
        => Around(node) is { } context
           && DbSetProperties(context).Any(property => property.Identifier.Text == name);

    private static bool IsDbSet(TypeSyntax type) => (type is NullableTypeSyntax nullable ? nullable.ElementType : type) switch
    {
        GenericNameSyntax generic => generic.Identifier.Text == "DbSet",
        QualifiedNameSyntax { Right: GenericNameSyntax generic } => generic.Identifier.Text == "DbSet",
        AliasQualifiedNameSyntax { Name: GenericNameSyntax generic } => generic.Identifier.Text == "DbSet",
        _ => false,
    };

    private static string? LastName(TypeSyntax type) => type switch
    {
        SimpleNameSyntax simple => simple.Identifier.Text,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
        _ => null,
    };
}
