using JavaEntityParsing;

namespace MyBatisWrappers;

/// <summary>
/// The three questions a MyBatis mapper asks about a written type name, kept in one place
/// because both mapping parsers and both query parsers ask them: what the simple name is,
/// what package it carried, and what a collection's element is. Purely textual - whether a
/// name denotes an entity is decided by the name itself (decision 001), not by resolving it.
/// </summary>
internal static class MyBatisTypeNames
{
    /// <summary>The name without its package; a generic name keeps its arguments.</summary>
    public static string SimpleName(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        return JavaTypeConvertor.StripPackage(typeName.Trim());
    }

    /// <summary>The package a qualified name carried, or null when it was written bare.</summary>
    public static string? PackageOf(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);

        var head = typeName.Trim();
        var generic = head.IndexOf('<');
        if (generic >= 0)
        {
            head = head[..generic];
        }

        var lastDot = head.LastIndexOf('.');
        return lastDot < 0 ? null : head[..lastDot];
    }

    /// <summary>
    /// The class a method's return type materializes: the element of a collection, the type
    /// itself otherwise. Null where the type names no class at all - void, a primitive, a
    /// map - because there is then no entity for a result mapping to belong to.
    /// </summary>
    public static string? MaterializedClass(string? returnType)
    {
        if (string.IsNullOrWhiteSpace(returnType))
        {
            return null;
        }

        var text = returnType.Trim();
        var open = text.IndexOf('<');

        if (open >= 0 && text.EndsWith('>'))
        {
            var arguments = text[(open + 1)..^1].Trim();

            // A map result - Map<String, Object> - materializes no class of the conversion.
            return arguments.Contains(',', StringComparison.Ordinal) ? null : MaterializedClass(arguments);
        }

        if (text is "void" || JavaTypeConvertor.IsPrimitive(text))
        {
            return null;
        }

        var simple = SimpleName(text);

        return JavaTypeConvertor.FromString(text).Category == Model.AbstractRepresentation.Enums.LangTypeCategory.Scalar
            ? null
            : simple;
    }
}
